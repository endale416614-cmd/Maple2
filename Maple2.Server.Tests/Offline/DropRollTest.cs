using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Offline;

/// <summary>DR-01 (BT-99): a drop roll never lands on an entry whose item has no data, and a box with no entry that has data is refused before it is used up.</summary>
public class DropRollTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Real = 20001050; // Auto-Fishing Voucher (1 Hour): an entry of box 340000660 with data
    private const int Other = 20302776; // Channel Chat Voucher: an entry of box 340000660 with data
    private const int Missing = 20300653; // the entry of box 340000660 without data
    private const int Box = 340000660; // opened by the batch 3 boxes 20303301 to 20303313
    private const int GlobalBox = 1; // a made-up global drop box
    private const int GachaId = 1003001; // 22003001's gacha
    private const int GachaBox = 530000001; // 22003001's box: groups that list no entry
    private const int Capsule = 22003001; // Orange Mushroom Power Up Capsule

    private static readonly GlobalDropItemBoxTable NoGlobal = new(new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>>(), new Dictionary<int, IList<GlobalDropItemBoxTable.Item>>());
    private static readonly IndividualDropItemTable NoIndividual = new(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>>());

    private SqliteConnection connection = null!;
    private MetadataContext context = null!;
    private ItemMetadataStorage items = null!;

    [OneTimeSetUp]
    public void SetUp() {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        context.ItemMetadata.AddRange(Meta(Real), Meta(Other));
        context.SaveChanges();
        items = new ItemMetadataStorage(context);
    }

    [OneTimeTearDown]
    public void TearDown() {
        context.Dispose();
        connection.Dispose();
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

    private static IndividualDropItemTable.Item Entry(int weight, params int[] ids) {
        return new IndividualDropItemTable.Item(ids, false, 0, 0, weight, new IndividualDropItemTable.Range<int>(1, 1), [new IndividualDropItemTable.Item.Rarity(100, 1)], 0, 0, false, false, false, false, [], 0);
    }

    private static IndividualDropItemTable.Entry Group(params IndividualDropItemTable.Item[] entries) {
        return new IndividualDropItemTable.Entry(1, 0, [new IndividualDropItemTable.Entry.DropCount(1, 100)], 0, false, false, entries);
    }

    private static IndividualDropItemTable Individual(int boxId, params IndividualDropItemTable.Entry[] groups) {
        return new IndividualDropItemTable(new Dictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> {
            [boxId] = groups.Select((group, index) => (group, index)).ToDictionary(pair => pair.index + 1, pair => pair.group),
        });
    }

    private static ServerTableMetadataStorage DropTables(IndividualDropItemTable individual, GlobalDropItemBoxTable global) {
        var storage = Bare<ServerTableMetadataStorage>();
        SetField(storage, "individualDropItemTable", new Lazy<IndividualDropItemTable>(() => individual));
        SetField(storage, "globalDropItemBoxTable", new Lazy<GlobalDropItemBoxTable>(() => global));
        return storage;
    }

    private static TableMetadataStorage GachaTables() {
        var storage = Bare<TableMetadataStorage>();
        var gacha = new GachaInfoTable(new Dictionary<int, GachaInfoTable.Entry> { [GachaId] = new(0, GachaBox, 0, 0, 0) });
        SetField(storage, "gachaInfoTable", new Lazy<GachaInfoTable>(() => gacha));
        return storage;
    }

    // Managers without a server or database: only what each test sets.
    private static (ItemDropManager Drop, GameSession Session) Build(ItemMetadataStorage items, ServerTableMetadataStorage serverTables, TableMetadataStorage tables) {
        var field = Bare<FieldManager>();
        SetProperty(field, "ItemMetadata", items);
        SetProperty(field, "ServerTableMetadata", serverTables);
        SetProperty(field, "TableMetadata", tables);
        SetProperty(field, "ItemStatsCalc", new ItemStatsCalculator { TableMetadata = tables });
        var drop = new ItemDropManager(field);
        SetField(field, "<ItemDrop>k__BackingField", drop);

        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "roller" }, new Character { Name = "Roller", Mastery = null! }, 0) {
            Home = null!,
            Currency = null!,
            Unlock = null!,
        });
        var session = Bare<GameSession>();
        SetProperty(session, "Player", player);
        SetProperty(session, "Field", field);
        SetProperty(session, "ServerTableMetadata", serverTables);
        SetProperty(session, "TableMetadata", tables);
        return (drop, session);
    }

    [Test]
    public void IndividualRoll_NeverLandsOnAnEntryWithoutData() {
        (ItemDropManager drop, GameSession session) = Build(items, DropTables(Individual(Box, Group(Entry(1000, Missing, 0), Entry(1, Real, 0))), NoGlobal), GachaTables());

        for (int i = 0; i < 200; i++) {
            Assert.That(drop.GetIndividualDropItems(session, 1, Box).Select(item => item.Id), Is.EqualTo(new[] { Real }));
        }
    }

    [Test]
    public void GlobalRoll_NeverLandsOnAnEntryWithoutData() {
        var group = new GlobalDropItemBoxTable.Group(1, 0, 0, [new(1, 100)], false, default, default);
        var global = new GlobalDropItemBoxTable(
            new Dictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> { [GlobalBox] = new() { [1] = [group] } },
            new Dictionary<int, IList<GlobalDropItemBoxTable.Item>> {
                [1] = [
                    new GlobalDropItemBoxTable.Item(Missing, 0, 0, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1000, [], false),
                    new GlobalDropItemBoxTable.Item(Real, 0, 0, new GlobalDropItemBoxTable.Range<int>(1, 1), 1, 1, [], false),
                ],
            });
        (ItemDropManager drop, _) = Build(items, DropTables(NoIndividual, global), GachaTables());

        for (int i = 0; i < 200; i++) {
            Assert.That(drop.GetGlobalDropItems(GlobalBox, 1).Select(item => item.Id), Is.EqualTo(new[] { Real }));
        }
    }

    [Test]
    public void FullBox_StillRollsEveryEntry() {
        (ItemDropManager drop, GameSession session) = Build(items, DropTables(Individual(Box, Group(Entry(3, Real, 0), Entry(1, Other, 0))), NoGlobal), GachaTables());

        var seen = new HashSet<int>();
        for (int i = 0; i < 400; i++) {
            int id = drop.GetIndividualDropItems(session, 1, Box).Single().Id;
            Assert.That(id, Is.AnyOf(Real, Other));
            seen.Add(id);
        }
        Assert.That(seen, Is.EquivalentTo(new[] { Real, Other }));
    }

    [Test]
    public void CanGiveItem_NeedsAnEntryWithData() {
        (ItemDropManager drop, _) = Build(items, DropTables(NoIndividual, NoGlobal), GachaTables());

        Assert.That(drop.CanGiveItem(new Dictionary<int, IndividualDropItemTable.Entry> { [1] = Group(Entry(1000, Missing, 0)) }), Is.False);
        Assert.That(drop.CanGiveItem(new Dictionary<int, IndividualDropItemTable.Entry> { [1] = Group() }), Is.False, "530000001's shape");
        Assert.That(drop.CanGiveItem(new Dictionary<int, IndividualDropItemTable.Entry> { [1] = Group(Entry(1000, Missing, 0)), [2] = Group(Entry(1, Real, 0)) }), Is.True);
        Assert.That(drop.CanGiveItem(new Dictionary<int, IndividualDropItemTable.Entry> { [1] = Group(Entry(1, Missing, Real)) }), Is.True);
    }

    [Test]
    public void GachaWithNoItemData_IsRefusedBeforeTheCapsuleIsUsed() {
        (_, GameSession session) = Build(items, DropTables(Individual(GachaBox, Group(Entry(1000, Missing, 0))), NoGlobal), GachaTables());
        var capsule = new Item(Meta(Capsule) with { Function = new ItemMetadataFunction(ItemFunction.OpenGachaBox, "OpenGachaBox", $"{GachaId},22103001", false) });

        var boxes = new ItemBoxManager(session);
        // session.Item is not set, so reaching the inventory (using up the capsule) would throw.
        Assert.That(boxes.Open(capsule), Is.EqualTo(ItemBoxError.s_err_cannot_open_multi_itembox_inventory_fail));
        Assert.That(boxes.BoxCount, Is.EqualTo(0));
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void Box340000660_EveryRollGivesAnItem() {
        string? path = DataDb.Find();
        Assume.That(path, Is.Not.Null, "maple-data.db not found (set MAPLE_DATA_DB)");

        using var dataConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        dataConnection.Open();
        using var dataContext = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(dataConnection).Options);
        dataContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var realItems = new ItemMetadataStorage(dataContext);
        var tables = new TableMetadataStorage(dataContext);
        var serverTables = new ServerTableMetadataStorage(dataContext);

        bool found = serverTables.IndividualDropItemTable.Entries.TryGetValue(Box, out IDictionary<int, IndividualDropItemTable.Entry>? box);
        Assume.That(found && box!.Values.Any(group => group.Items.Any(entry => !entry.Ids.Any(id => id > 0 && realItems.TryGet(id, out _)))),
            "box 340000660 names no item without data (20300653) in this data");

        (ItemDropManager drop, GameSession session) = Build(realItems, serverTables, tables);
        for (int i = 0; i < 300; i++) {
            Item rolled = drop.GetIndividualDropItems(session, 1, Box).Single();
            Assert.That(realItems.TryGet(rolled.Id, out _), Is.True);
        }
    }
}
