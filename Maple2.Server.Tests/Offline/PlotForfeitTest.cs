using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Grpc.Core;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Service;
using Maple2.Server.Game.Session;
using Maple2.Tools.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Offline;

/// <summary>HS-03: a plot forfeited or expired takes its function cubes and assistants out of the field, on the field's own thread. HS-04: a forfeit stores the placed cubes as expiry does (BT-192), and the door to the home and the UGC portals leave with the plot (BT-193). Bare fields, as in FieldDisposeTest; a small data set in memory, as in AutoGemUpgradeTest; a save that stays empty unless a test buys the plot, so UpdateAllPlots finds no plot to replace these with.</summary>
public class PlotForfeitTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Map = 2000164; // the plot's map in the one forfeit the captures hold (bank_and_housing_contract.msb)
    private const int Forfeited = 29; // and its number
    private const int Neighbour = 30;
    private const int AnyPlayer = 1;
    private const int AssistantObject = 11;
    private const int NeighbourObject = 12;
    private const long NeighbourOwner = 2;
    private const int Block = 50100286; // Fennel Leaves Block
    private const int Door = 50200179; // Nomad Tent, one of the 56 items with Install.IndoorPortal
    private const int NoData = 59999999; // an item id the data does not have (a pack item without the pack)

    // The shipped row of Momo's cube, as in AssistantSouvenirTest.
    private static readonly FunctionCubeMetadata Momo = new(80000000, 0, ConfigurableCubeType.None, InteractCubeState.InUse, InteractCubeControlType.SpawnNPC, [], 0, null);

    // The shipped row of Interior Portal BETA, whose item and function cube share the id.
    private static readonly FunctionCubeMetadata UgcPortal = new(Constant.InteriorPortalCubeId, 0, ConfigurableCubeType.UGCPortal, InteractCubeState.None, InteractCubeControlType.Portal, [], 0, null);

    private static readonly Vector3B MomoSpot = new(1, 2, 3);
    private static readonly Vector3B BlockSpot = new(1, 2, 4);
    private static readonly Vector3B NeighbourSpot = new(9, 2, 3);

    private SqliteConnection data = null!;
    private MetadataContext metadata = null!;
    private ItemMetadataStorage items = null!;
    private GameStorage storage = null!;
    private SqliteConnection save = null!;
#pragma warning disable NUnit1032 // A bare field has no thread: its Dispose can not run.
    private FieldManager field = null!;
    private GameSession owner = null!;
#pragma warning restore NUnit1032
    private Plot plot = null!;
    private Plot neighbour = null!;
    private string momoId = null!;
    private string neighbourId = null!;
    private long account;
    private Plot owned = null!;
    private HousingManager housing = null!;

    [SetUp]
    public void NewField() {
        data = new SqliteConnection("Data Source=:memory:");
        data.Open();
        metadata = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(data).Options);
        metadata.Database.EnsureCreated();
        metadata.ItemMetadata.AddRange(Meta(Block, new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none)),
            Meta(Door, new ItemMetadataInstall(false, 0, 0, true, MapAttribute.none)),
            Meta(UgcPortal.Id, new ItemMetadataInstall(false, 0, UgcPortal.Id, false, MapAttribute.none)));
        metadata.FunctionCubeMetadata.Add(UgcPortal);
        metadata.UgcMapMetadata.Add(new UgcMapMetadata(Map, new Dictionary<int, UgcMapGroup> { [Forfeited] = Group(Forfeited), [Neighbour] = Group(Neighbour) }));
        metadata.SaveChanges();
        items = new ItemMetadataStorage(metadata);

        save = new SqliteConnection("Data Source=:memory:");
        save.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(save).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }

        storage = new GameStorage(options, items, new MapMetadataStorage(metadata), null!, null!, null!, null!, NullLogger<GameStorage>.Instance, new FunctionCubeMetadataStorage(metadata));

        field = Bare<FieldManager>();
        var scheduler = new EventQueue(Log.Logger);
        scheduler.Start();
        SetField(field, "Scheduler", scheduler);
        SetField(field, "FieldActor", Bare<FieldActor>());
        SetField(field, "<GameStorage>k__BackingField", storage);
        SetField(field, "<MapId>k__BackingField", Map);
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        SetField(field, "<Npcs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
        SetField(field, "<Mobs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
        SetField(field, "fieldFunctionInteracts", new ConcurrentDictionary<string, FieldFunctionInteract>());
        SetField(field, "fieldPortals", new ConcurrentDictionary<int, FieldPortal>());
        SetField(field, "Plots", new ConcurrentDictionary<int, Plot>());

        plot = NewPlot(Forfeited);
        neighbour = NewPlot(Neighbour);
        momoId = Place(plot, MomoSpot, AssistantObject);
        neighbourId = Place(neighbour, NeighbourSpot, NeighbourObject);
        var block = Bare<PlotCube>(); // a cube without a function cube
        block.Position = BlockSpot;
        plot.Cubes.Add(BlockSpot, block);
    }

    [TearDown]
    public void CloseSave() {
        save.Dispose();
        metadata.Dispose();
        data.Dispose();
    }

    [Test]
    public void AnOccupiedFieldEmptiesThePlotOnItsOwnThread() {
        field.Players[AnyPlayer] = Bare<FieldPlayer>();

        field.ForfeitPlot(Forfeited, null); // the gRPC thread

        Assert.That(plot.Cubes, Has.Count.EqualTo(2), "the caller's thread does not touch the plot");
        Assert.That(field.TryGetFieldFunctionInteract(momoId), Is.Not.Null);
        Assert.That(Scheduler.Queued, Is.EqualTo(1));

        Scheduler.InvokeAll(); // the field's next frame
        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(field.TryGetFieldFunctionInteract(momoId), Is.Null);
        Assert.That(field.Npcs.ContainsKey(AssistantObject), Is.True, "RemoveNpc schedules her removal");

        Scheduler.InvokeAll();
        Assert.That(field.Npcs.ContainsKey(AssistantObject), Is.False, "the assistant leaves the emptied plot");
    }

    [Test]
    public void AnEmptyFieldEmptiesThePlotAtOnce() {
        // Update runs no Scheduler without players, and a field GetField has just made never had any. The owner's message goes at once, as before.
        field.ForfeitPlot(Forfeited, null);

        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(field.TryGetFieldFunctionInteract(momoId), Is.Null);

        field.Players[AnyPlayer] = Bare<FieldPlayer>(); // the first visitor's frame runs RemoveNpc's event
        Scheduler.InvokeAll();
        Assert.That(field.Npcs.ContainsKey(AssistantObject), Is.False);
    }

    [Test]
    public void TheNeighbourKeepsItsCubesAndAssistant() {
        field.ForfeitPlot(Forfeited, null);
        Scheduler.InvokeAll();

        Assert.That(neighbour.Cubes, Has.Count.EqualTo(1));
        Assert.That(field.TryGetFieldFunctionInteract(neighbourId), Is.Not.Null);
        Assert.That(field.Npcs.ContainsKey(NeighbourObject), Is.True);
    }

    [Test]
    public void EmptyPlot_IsSafeTwice() {
        field.EmptyPlot(plot, plot.OwnerId);
        field.EmptyPlot(plot, plot.OwnerId);

        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(Scheduler.Queued, Is.EqualTo(1), "one removal of the assistant");
    }

    [Test]
    public void WorldsExpiryCallReachesTheFieldsThread() {
        field.Players[AnyPlayer] = Bare<FieldPlayer>();

        Call(forfeit: true);

        Assert.That(plot.Cubes, Has.Count.EqualTo(2), "the gRPC thread does not touch the plot");
        Scheduler.InvokeAll();
        Assert.That(plot.Cubes, Is.Empty);
        Assert.That(field.TryGetFieldFunctionInteract(momoId), Is.Null);
    }

    [Test]
    public void ACallWithoutForfeitLeavesThePlot() {
        // World's later call, when a pending plot opens: its cubes left long ago.
        field.Players[AnyPlayer] = Bare<FieldPlayer>();

        Call(forfeit: false);

        Assert.That(Scheduler.Queued, Is.EqualTo(0));
        Assert.That(plot.Cubes, Has.Count.EqualTo(2));
        Assert.That(field.TryGetFieldFunctionInteract(momoId), Is.Not.Null);
    }

    [Test]
    public void AForfeitPutsPlacedCubesBackOnTheirStack() {
        BuyThePlot();
        long stack = Stock(Block, 3);
        OpenSession();
        PlaceFromStorage(stack, Block, MomoSpot);
        PlaceFromStorage(stack, Block, BlockSpot);
        Assert.That(owner.Item.Furnishing.GetCube(stack)!.Amount, Is.EqualTo(1));
        Assert.That(Amount(stack), Is.EqualTo(3), "placing drops the amount only in memory until the next save");

        Assert.That(housing.ForfeitPlot(), Is.Not.Null);

        Assert.That(Amount(stack), Is.EqualTo(3), "two came back to the one left: no more, no less");
        Assert.That(owner.Item.Furnishing.GetCube(stack)!.Amount, Is.EqualTo(3), "the storage is read back");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {account}"), Is.EqualTo(1));
        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {owned.Id}"), Is.EqualTo(0));
        Assert.That(Count($"SELECT OwnerId FROM ugcmap WHERE Id = {owned.Id}"), Is.EqualTo(0), "the plot is pending");
        Assert.That(Count("SELECT COUNT(*) FROM item WHERE OwnerId = 0"), Is.EqualTo(0), "nothing for owner 0");
        Assert.That(owned.Cubes, Is.Empty);

        using (GameStorage.Request db = storage.Context()) {
            owner.Item.Furnishing.Save(db);
        }
        Assert.That(Amount(stack), Is.EqualTo(3), "the next autosave keeps them");
    }

    [Test]
    public void TheLastCubeOfAStackComesBack() {
        BuyThePlot();
        long stack = Stock(Block, 1);
        OpenSession();
        PlaceFromStorage(stack, Block, MomoSpot);
        Assert.That(owner.Item.Furnishing.GetCube(stack)!.Amount, Is.EqualTo(0));

        Assert.That(housing.ForfeitPlot(), Is.Not.Null);

        Assert.That(Amount(stack), Is.EqualTo(1));
        Assert.That(owner.Item.Furnishing.GetCube(stack)!.Amount, Is.EqualTo(1));
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {account}"), Is.EqualTo(1), "the stack's row stays, as placing keeps it");
    }

    [Test]
    public void ACubeWithoutItsDataWaitsInTheStorage() {
        BuyThePlot();
        OpenSession();
        PlotCube hidden = NewCube(Block, BlockSpot);
        Assert.That(Sql($"UPDATE \"ugcmap-cube\" SET ItemId = {NoData} WHERE Id = {hidden.Id}"), Is.EqualTo(1));

        Assert.That(housing.ForfeitPlot(), Is.Not.Null);

        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {owned.Id}"), Is.EqualTo(0), "the next buyer finds no cube");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {account} AND ItemId = {NoData} AND \"Group\" = {(int) ItemGroup.Furnishing}"), Is.EqualTo(1));
    }

    [Test]
    public void AStoreThatFailsChangesNothing() {
        BuyThePlot();
        long stack = Stock(Block, 2);
        OpenSession();
        PlaceFromStorage(stack, Block, MomoSpot);
        owned.Cubes.Add(BlockSpot, NewCube(Door, BlockSpot)); // no stack: the store writes a new row
        Sql("CREATE TRIGGER refused BEFORE INSERT ON item BEGIN SELECT RAISE(ABORT, 'refused'); END");
        field.Players[AnyPlayer] = Bare<FieldPlayer>();

        Assert.That(housing.ForfeitPlot(), Is.Null);

        var refused = CubePacket.Error(UgcMapError.s_ugcmap_db);
        Assert.That(owner.GetLastSentPacket(SendOp.ResponseCube), Is.EqualTo(refused.Buffer.Take(refused.Length).ToArray()));
        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {owned.Id}"), Is.EqualTo(2));
        Assert.That(Count($"SELECT OwnerId FROM ugcmap WHERE Id = {owned.Id}"), Is.EqualTo(account));
        Assert.That(Amount(stack), Is.EqualTo(1), "what memory holds: the store's addition went back with the rest");
        Assert.That(owner.Item.Furnishing.GetCube(stack)!.Amount, Is.EqualTo(1));
        Assert.That(owned.Cubes, Has.Count.EqualTo(2));
        Assert.That(owned.OwnerId, Is.EqualTo(account));
        Assert.That(Scheduler.Queued, Is.EqualTo(0));
        Assert.That(owner.Player.Value.Home.Outdoor, Is.Not.Null);
    }

    [Test]
    public void AForfeitOfAnEmptyPlotSendsNoStorageList() {
        // The one captured forfeit: its packets stay as they are.
        BuyThePlot();
        long stack = Stock(Block, 2);
        OpenSession();

        Assert.That(housing.ForfeitPlot(), Is.Not.Null);

        Assert.That(owner.GetLastSentPacket(SendOp.FurnishingStorage), Is.Null);
        Assert.That(owner.GetLastSentPacket(SendOp.FurnishingInventory), Is.Null);
        Assert.That(Amount(stack), Is.EqualTo(2));
    }

    [Test]
    public void AForfeitTakesTheDoorAndPortalsAwayOnTheFieldsThread() {
        BuyThePlot();
        long doors = Stock(Door, 1);
        long portals = Stock(UgcPortal.Id, 1);
        OpenSession();
        field.Players[AnyPlayer] = Bare<FieldPlayer>();
        FieldPortal mapPortal = MapPortal();
        FieldPortal neighbourDoor = NeighbourDoor();
        FieldPortal door = field.SpawnFieldToHomePortal(PlaceFromStorage(doors, Door, MomoSpot), account)!;
        FieldPortal ugcPortal = field.SpawnCubePortal(PlaceFromStorage(portals, UgcPortal.Id, BlockSpot))!;

        Assert.That(housing.ForfeitPlot(), Is.Not.Null);

        Assert.That(field.GetPortals(), Is.EquivalentTo(new[] { mapPortal, neighbourDoor, door, ugcPortal }), "the caller's thread does not touch them");
        Assert.That(owned.OwnerId, Is.EqualTo(0), "UpdatePlotInfo has cleared the owner when the field's frame comes");

        Scheduler.InvokeAll();
        Assert.That(field.GetPortals(), Is.EquivalentTo(new[] { mapPortal, neighbourDoor }));
        Assert.That(Amount(doors), Is.EqualTo(1));
        Assert.That(Amount(portals), Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AnExpiryTakesTheDoorAndPortalsAway(bool occupied) {
        neighbour.OwnerId = NeighbourOwner;
        if (occupied) {
            field.Players[AnyPlayer] = Bare<FieldPlayer>();
        }
        FieldPortal mapPortal = MapPortal();
        FieldPortal neighbourDoor = NeighbourDoor();
        var portalCube = new PlotCube(Meta(UgcPortal.Id, new ItemMetadataInstall(false, 0, UgcPortal.Id, false, MapAttribute.none))) {
            Type = PlotCube.CubeType.Construction,
            Position = NeighbourSpot,
            Interact = new InteractCube(NeighbourSpot, UgcPortal),
        };
        plot.Cubes.Add(NeighbourSpot, portalCube);
        field.SpawnCubePortal(portalCube);
        field.SpawnFieldToHomePortal(new PlotCube(Meta(Door, new ItemMetadataInstall(false, 0, 0, true, MapAttribute.none))) { Type = PlotCube.CubeType.Construction, Position = MomoSpot }, plot.OwnerId);

        Call(forfeit: true);
        Scheduler.InvokeAll();

        Assert.That(field.GetPortals(), Is.EquivalentTo(new[] { mapPortal, neighbourDoor }));
    }

    [Test]
    public void APlotWithNoOwnerLeavesTheMapsOwnPortals() {
        FieldPortal mapPortal = MapPortal();

        field.EmptyPlot(plot, 0);

        Assert.That(field.GetPortals(), Is.EquivalentTo(new[] { mapPortal }), "a map's portal has HomeId 0");
    }

    private EventQueue Scheduler => (EventQueue) GetField(field, "Scheduler")!;

    // Install decides what a cube is; the rest is what any item needs.
    private static ItemMetadata Meta(int id, ItemMetadataInstall install) {
        var property = new ItemMetadataProperty(false, 0, 9999, 6, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false), install);
    }

    private static UgcMapGroup Group(int number) => new(number, 0, 0, new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Cost(0, 0, 0), new UgcMapGroup.Limits(0, 0, 0, 0, 0, 0));

    private int Sql(string sql) {
        using SqliteCommand command = save.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private long Count(string sql) {
        using SqliteCommand command = save.CreateCommand();
        command.CommandText = sql;
        return (long) command.ExecuteScalar()!;
    }

    private long Amount(long stack) => Count($"SELECT Amount FROM item WHERE Id = {stack}");

    private FieldPortal MapPortal() => field.SpawnPortal(new Portal(1, Map, 1, PortalType.Field, PortalActionType.Interact, default, default, default, 0, 0, true, true, true));

    private FieldPortal NeighbourDoor() =>
        field.SpawnFieldToHomePortal(new PlotCube(Meta(Door, new ItemMetadataInstall(false, 0, 0, true, MapAttribute.none))) { Type = PlotCube.CubeType.Construction, Position = NeighbourSpot }, NeighbourOwner)!;

    // The plot bought in the save, as the field holds it.
    private void BuyThePlot() {
        using GameStorage.Request db = storage.Context();
        account = db.CreateAccount(new Account { Username = "homeowner", MachineId = Guid.NewGuid() }, "password").Id;
        Assert.That(db.InitUgcMap(metadata.UgcMapMetadata.ToList()), Is.True);
        PlotInfo? bought = db.BuyPlot("Homeowner", account, db.LoadPlotsForMap(Map).Single(open => open.Number == Forfeited), TimeSpan.FromDays(30));
        Assert.That(bought, Is.Not.Null);
        owned = db.LoadPlotsForMap(Map).Single(open => open.Number == Forfeited);
        field.Plots[Forfeited] = owned;
    }

    private long Stock(int itemId, int amount) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? item), Is.True);
        using GameStorage.Request db = storage.Context();
        return db.CreateItem(account, new Item(item!, 1, amount) { Group = ItemGroup.Furnishing })!.Uid;
    }

    private PlotCube NewCube(int itemId, Vector3B position) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? item), Is.True);
        using GameStorage.Request db = storage.Context();
        return db.CreateCube(owned, new PlotCube(item!) {
            Type = PlotCube.CubeType.Construction,
            Position = position,
            Interact = itemId == UgcPortal.Id ? new InteractCube(position, UgcPortal) : null,
        })!;
    }

    // What TryPlaceCube leaves: the cube's row in the save, the stack's amount dropped in memory only, the cube on the plot.
    private PlotCube PlaceFromStorage(long stack, int itemId, Vector3B position) {
        PlotCube cube = NewCube(itemId, position);
        Assert.That(owner.Item.Furnishing.TryAddCube(stack, cube), Is.True);
        owned.Cubes.Add(position, cube);
        return cube;
    }

    // As far as HousingManager.ForfeitPlot reaches: a real FurnishingManager over the save, no socket, World answers.
    private void OpenSession() {
        owner = Bare<GameSession>();
        SetField(owner, "Logger", Log.Logger);
        SetField(owner, "sendQueue", new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>()));
        SetField(owner, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        SetField(owner, "<AccountId>k__BackingField", account);
        SetField(owner, "<GameStorage>k__BackingField", storage);
        SetField(owner, "<World>k__BackingField", new WorldThatAnswers());
        SetField(owner, "<Field>k__BackingField", field);
        SetField(owner, "server", Server());

        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "homeowner" }, new Character { Name = "Homeowner", Mastery = null! }, 0) {
            Home = new Home { Indoor = new PlotInfo(null!) { MapId = Constant.DefaultHomeMapId }, Outdoor = owned },
            Currency = new Currency(),
            Unlock = null!,
        });
        SetField(owner, "<Player>k__BackingField", player);

        var itemManager = Bare<ItemManager>();
        SetField(itemManager, "session", owner);
        SetField(owner, "<Item>k__BackingField", itemManager);
        using (GameStorage.Request db = storage.Context()) {
            SetField(itemManager, "<Furnishing>k__BackingField", new FurnishingManager(db, owner));
        }
        housing = new HousingManager(owner, null!);
    }

    private sealed class WorldThatAnswers : WorldClient {
        public override FieldPlotResponse UpdateFieldPlot(FieldPlotRequest request, Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default) => new();
    }

    private Plot NewPlot(int number) {
        var newPlot = new Plot(Group(number)) { Number = number, MapId = Map, OwnerId = 1, ExpiryTime = 1 };
        field.Plots[number] = newPlot;
        return newPlot;
    }

    // What AddFieldFunctionInteract leaves: the cube on the plot, its function cube and its NPC in the field.
    private string Place(Plot target, Vector3B position, int npcObject) {
        var npc = Bare<FieldNpc>();
        SetField(npc, "<ObjectId>k__BackingField", npcObject);
        field.Npcs[npcObject] = npc;

        var cube = Bare<PlotCube>();
        cube.Position = position;
        cube.Interact = new InteractCube(position, Momo);
        target.Cubes.Add(position, cube);

        var interact = new FieldFunctionInteract(field, npcObject, cube.Interact, 0) { Assistant = npc };
        ((ConcurrentDictionary<string, FieldFunctionInteract>) GetField(field, "fieldFunctionInteracts")!)[cube.Interact.Id] = interact;
        return cube.Interact.Id;
    }

    // The real entry of World's call. World names no channel to ignore and the offline channel is 1; this test's GameServer is channel 0, so it names 1.
    private void Call(bool forfeit) {
        var service = Bare<ChannelService>();
        SetField(service, "server", Server());

        service.UpdateFieldPlot(new FieldPlotRequest {
            IgnoreChannel = 1,
            MapId = Map,
            PlotNumber = Forfeited,
            UpdatePlot = new FieldPlotRequest.Types.UpdatePlot { Forfeit = forfeit },
        }, null!);
    }

    private GameServer Server() {
        var tables = Bare<ServerTableMetadataStorage>();
        SetField(tables, "instanceFieldTable", new Lazy<InstanceFieldTable>(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));
        var factory = Bare<FieldManager.Factory>();
        SetField(factory, "<ServerTableMetadata>k__BackingField", tables);
        SetField(factory, "mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        SetField(factory, "fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [Map] = new ConcurrentDictionary<int, FieldManager> { [field.RoomId] = field },
        });
        var server = Bare<GameServer>();
        SetField(server, "fieldFactory", factory);
        return server;
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static FieldInfo FindField(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) => FindField(target, name).SetValue(target, value);

    private static object? GetField(object target, string name) => FindField(target, name).GetValue(target);
}
