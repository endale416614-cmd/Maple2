using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>HS-01: a furnishing the storage cannot take stays with its source, and a refused placement or replace changes nothing.</summary>
public class FurnishingStorageTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ItemA = 50100001;
    private const int ItemB = 50100002;
    private const int ItemX = 50100003;
    private const int ItemY = 50100004;
    private const int ItemZ = 50100005;
    private const int ItemN = 50100006;

    private sealed class Rig {
        public required GameSession Session { get; init; }
        public required InventoryManager Inventory { get; init; }
        public required HousingManager Housing { get; init; }
        public required ItemCollection Storage { get; init; }
        public required ConcurrentDictionary<long, PlotCube> Placed { get; init; }
        public required List<Item> Delete { get; init; }
        public required Plot Plot { get; init; }
        public required Home Home { get; init; }
        public required Currency Money { get; init; }
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetProperty(object target, string name, object? value) {
        target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static ItemMetadata Meta(int id) {
        var property = new ItemMetadataProperty(false, 0, 100, 6, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false), new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none));
    }

    private static FurnishingShopTable.Entry Shop(int itemId, int price) => new(itemId, true, FurnishingCurrencyType.Meso, price);

    private static PlotCube Cube(int itemId, long id) => new(Meta(itemId), id) { Type = PlotCube.CubeType.Construction };

    // Managers without a server or database: only what each test sets.
    private static Rig Build(int slots, params FurnishingShopTable.Entry[] shop) {
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>()));
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());

        var plot = new Plot(null!) { MapId = Constant.DefaultHomeMapId };
        var home = new Home { Indoor = plot };
        home.SetArea(10);
        home.SetHeight(4);
        var money = new Currency();
        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "stocker" }, new Character { Name = "Stocker", Mastery = null! }, 0) {
            Home = home,
            Currency = money,
            Unlock = null!,
        });
        SetProperty(session, "Player", player);
        SetProperty(session, "Field", Bare<FieldManager>());
        SetProperty(session, "Currency", new CurrencyManager(session));

        var storage = new ItemCollection((short) slots);
        var placed = new ConcurrentDictionary<long, PlotCube>();
        var delete = new List<Item>();

        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", delete);
        SetField(inventory, "logger", Log.Logger);
        // The furnishing's own tab has room: the furnishing storage decides, not the tab.
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [InventoryType.FishingMusic] = new ItemCollection(10) });

        var furnishing = Bare<FurnishingManager>();
        SetField(furnishing, "session", session);
        SetField(furnishing, "storage", storage);
        SetField(furnishing, "inventory", placed);
        SetField(furnishing, "logger", Log.Logger);

        var items = Bare<ItemManager>();
        SetField(items, "Inventory", inventory);
        SetProperty(items, "Furnishing", furnishing);
        SetProperty(session, "Item", items);

        var functionCubes = Bare<FunctionCubeMetadataStorage>();
        SetField(functionCubes, "FunctionCubeMetadata", new Dictionary<int, FunctionCubeMetadata>());
        SetProperty(session, "FunctionCubeMetadata", functionCubes);

        var tables = Bare<TableMetadataStorage>();
        Dictionary<int, FurnishingShopTable.Entry> entries = shop.ToDictionary(entry => entry.ItemId);
        SetField(tables, "furnishingShopTable", new Lazy<FurnishingShopTable>(() => new FurnishingShopTable(entries)));

        return new Rig {
            Session = session,
            Inventory = inventory,
            Housing = new HousingManager(session, tables),
            Storage = storage,
            Placed = placed,
            Delete = delete,
            Plot = plot,
            Home = home,
            Money = money,
        };
    }

    private static Item Store(Rig rig, int itemId, long uid, int amount = 1) {
        var item = new Item(Meta(itemId), 1, amount) { Uid = uid };
        Assert.That(rig.Storage.Add(item), Is.Not.Empty);
        return item;
    }

    private static string Snapshot(Rig rig) => string.Join(",", rig.Storage.Select(item => $"{item.Uid}:{item.Id}:{item.Amount}"));

    private static bool Place(Rig rig, PlotCube cube, Vector3B position, bool replace = false) =>
        rig.Housing.TryPlaceCube(cube, rig.Plot, cube.Metadata, position, 0f, out _, replace);

    private static void AssertError(Rig rig, UgcMapError code) {
        var expected = CubePacket.Error(code);
        Assert.That(rig.Session.GetLastSentPacket(SendOp.ResponseCube), Is.EqualTo(expected.Buffer.Take(expected.Length).ToArray()), $"expected {code}");
    }

    [Test]
    public void FullStorage_RefusesFurnishing_AndKeepsIt() {
        Rig rig = Build(1);
        Item stored = Store(rig, ItemA, 55);
        var refused = new Item(Meta(ItemB), 1, 1) { Uid = 77 };

        Assert.That(rig.Inventory.CanAdd(refused), Is.False, "CanAdd asked the furnishing tab, not the storage");
        Assert.That(rig.Inventory.Add(refused), Is.False, "Add reported a furnishing the storage could not take as added");
        Assert.That(rig.Delete, Does.Not.Contain(refused), "the refused furnishing was discarded");
        Assert.That(Snapshot(rig), Is.EqualTo("55:" + ItemA + ":1"));
        Assert.That(stored.Amount, Is.EqualTo(1));
    }

    [Test]
    public void StorageStack_StillTakesFurnishing() {
        Rig rig = Build(1);
        Item stored = Store(rig, ItemA, 55);
        var more = new Item(Meta(ItemA), 1, 1);

        Assert.That(rig.Inventory.CanAdd(more), Is.True);
        Assert.That(rig.Inventory.Add(more), Is.True);
        Assert.That(stored.Amount, Is.EqualTo(2));
    }

    [Test]
    public void RefusedPurchase_LeavesNoCopy() {
        var position = new Vector3B(-1, -1, 1);

        // no shop entry
        Rig rig = Build(4);
        AssertPurchaseRefused(rig, position, UgcMapError.s_err_cannot_buy_limited_item_more);

        // not enough meso
        rig = Build(4, Shop(ItemN, 1000));
        AssertPurchaseRefused(rig, position, UgcMapError.s_err_ugcmap_not_enough_meso_balance);

        // full storage
        rig = Build(1, Shop(ItemN, 0));
        Store(rig, ItemA, 55);
        AssertPurchaseRefused(rig, position, UgcMapError.s_ugcmap_not_for_sale);
    }

    private static void AssertPurchaseRefused(Rig rig, Vector3B position, UgcMapError code) {
        string before = Snapshot(rig);

        Assert.That(Place(rig, Cube(ItemN, 0), position), Is.False);

        AssertError(rig, code);
        Assert.That(Snapshot(rig), Is.EqualTo(before), "a copy stayed in storage");
        Assert.That(rig.Plot.Cubes, Is.Empty);
        Assert.That(rig.Money.Meso, Is.Zero);
    }

    [Test]
    public void RefusedReplace_KeepsOldCube() {
        var inside = new Vector3B(-1, -1, 1);

        // outside the area
        Rig rig = Build(4, Shop(ItemN, 1000));
        AssertReplaceRefused(rig, new Vector3B(-12, -1, 1), Cube(ItemN, 0), UgcMapError.s_ugcmap_area_limit);

        // not enough meso
        rig = Build(4, Shop(ItemN, 1000));
        AssertReplaceRefused(rig, inside, Cube(ItemN, 0), UgcMapError.s_err_ugcmap_not_enough_meso_balance);

        // the old cube has no room in a full storage
        rig = Build(1);
        Item owned = Store(rig, ItemY, 66);
        AssertReplaceRefused(rig, inside, Cube(ItemY, 66), UgcMapError.s_ugcmap_cant_be_removed);
        Assert.That(owned.Amount, Is.EqualTo(1));
    }

    private static void AssertReplaceRefused(Rig rig, Vector3B position, PlotCube replacement, UgcMapError code) {
        PlotCube old = Cube(ItemX, 9001);
        old.Position = position;
        rig.Plot.Cubes[position] = old;
        rig.Placed[9001] = old;
        string before = Snapshot(rig);

        Assert.That(Place(rig, replacement, position, replace: true), Is.False);

        AssertError(rig, code);
        Assert.That(rig.Plot.Cubes[position], Is.SameAs(old), "the old cube left the plot");
        Assert.That(rig.Placed.ContainsKey(9001), Is.True, "the old cube left the furnishing inventory");
        Assert.That(Snapshot(rig), Is.EqualTo(before));
        Assert.That(rig.Money.Meso, Is.Zero);
    }

    [Test]
    public void PlannerReplace_KeepsOldCubeOnRefusal() {
        // outside the area
        Rig rig = PlannerRig();
        var outside = new Vector3B(-12, -1, 1);
        PlotCube old = Cube(ItemZ, 9002);
        rig.Plot.Cubes[outside] = old;

        Assert.That(Place(rig, Cube(ItemZ, 88), outside, replace: true), Is.False);

        AssertError(rig, UgcMapError.s_ugcmap_area_limit);
        Assert.That(rig.Plot.Cubes[outside], Is.SameAs(old), "the old cube left the plot");

        // inside: the replaced cube is not counted against the stock
        rig = PlannerRig();
        var inside = new Vector3B(-1, -1, 1);
        old = Cube(ItemZ, 9002);
        rig.Plot.Cubes[inside] = old;

        Assert.That(rig.Housing.TryPlaceCube(Cube(ItemZ, 88), rig.Plot, Meta(ItemZ), inside, 0f, out PlotCube? result, true), Is.True);

        Assert.That(rig.Plot.Cubes[inside], Is.SameAs(result));
        Assert.That(result, Is.Not.SameAs(old));
    }

    private static Rig PlannerRig() {
        Rig rig = Build(4);
        rig.Plot.PlotMode = PlotMode.DecorPlanner;
        rig.Home.EnterPlanner(PlotMode.DecorPlanner);
        Store(rig, ItemZ, 88);
        return rig;
    }
}
