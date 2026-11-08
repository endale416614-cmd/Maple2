using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>Gear created without stats by the removed EpicGear option (repair), and the DungeonEpicGear rule that replaced it.</summary>
[NonParallelizable]
public class GearRepairTest {
    private const int RIENA = 15000224; // Berserker starter weapon: constant rows at rarity 1 only
    private const int KNUCKLES = 15500514; // Striker starter weapon (Pugilist Knuckles)

    private static readonly BasicAttribute[] Attacks = [BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk, BasicAttribute.PhysicalAtk, BasicAttribute.MagicalAtk];

    private SqliteConnection? dataConnection;
    private MetadataContext? dataContext;
    private ItemMetadataStorage items = null!;
    private TableMetadataStorage tables = null!;
    private ItemStatsCalculator calculator = null!;
    private ServerTableMetadataStorage serverTables = null!;
    private MapMetadataStorage maps = null!;
    private AchievementMetadataStorage achievements = null!;
    private QuestMetadataStorage quests = null!;
    private FunctionCubeMetadataStorage cubes = null!;

    private static string? FindDataDb() {
        foreach (string? dir in new[] { Environment.GetEnvironmentVariable("SQLITE_DATA_DIR"), Environment.GetEnvironmentVariable("SQLITE_DIR") }) {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "maple-data.db"))) {
                return Path.Combine(dir, "maple-data.db");
            }
        }
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "SaveData", "maple-data.db");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    [OneTimeSetUp]
    public void LoadGameData() {
        string? path = FindDataDb();
        if (path == null) {
            return;
        }

        dataConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        dataConnection.Open();
        dataContext = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(dataConnection).Options);
        dataContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        items = new ItemMetadataStorage(dataContext);
        tables = new TableMetadataStorage(dataContext);
        serverTables = new ServerTableMetadataStorage(dataContext);
        maps = new MapMetadataStorage(dataContext);
        achievements = new AchievementMetadataStorage(dataContext);
        quests = new QuestMetadataStorage(dataContext);
        cubes = new FunctionCubeMetadataStorage(dataContext);
        calculator = new ItemStatsCalculator { TableMetadata = tables };
    }

    [OneTimeTearDown]
    public void Unload() {
        dataContext?.Dispose();
        dataConnection?.Dispose();
    }

    private void NeedsGameData() => Assume.That(dataContext, Is.Not.Null, "maple-data.db not found (set SQLITE_DIR)");

    // The item as ItemDropManager.CreateItem builds it.
    private Item Create(int itemId, int? rarity = null) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? metadata), Is.True, $"item {itemId}");
        int useRarity = rarity ?? ItemDropManager.DefaultRarity(metadata!.Option, tables.ItemOptionConstantTable, tables.ItemOptionStaticTable, tables.ItemOptionRandomTable);
        var item = new Item(metadata!, useRarity);
        item.Stats = calculator.GetStats(item);
        return item;
    }

    private static bool HasAttack(Item item) {
        ItemStats.Option constant = item.Stats![ItemStats.Type.Constant];
        return Attacks.Any(attack => constant.Basic.TryGetValue(attack, out BasicOption value) && value.Value > 0);
    }

    // Synthetic tables, no game data.

    private static ItemMetadata Metadata(int id, ItemMetadataOption? option, bool skin = false) {
        var property = new ItemMetadataProperty(skin, 0, 1, 0, 0, "", "", default, 1, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 1, default, 0, false, false, false, false, false, false, false, 0, [], []);
        return new ItemMetadata(id, "test", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], option, null, null, null);
    }

    private static ItemOptionConstantTable Constants(int id, params int[] rarities) {
        var row = new ItemOptionConstant(new Dictionary<BasicAttribute, int> { [BasicAttribute.MinWeaponAtk] = 5 }, new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, int>(), new Dictionary<SpecialAttribute, float>());
        return new ItemOptionConstantTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>> {
            [id] = rarities.ToDictionary(rarity => rarity, _ => row),
        });
    }

    private static readonly ItemOptionPickTable NoPicks = new(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>());

    private static ItemMetadataOption Option(int id) => new(id, ItemOptionMakeType.Base, id, ItemOptionMakeType.Base, id, ItemOptionMakeType.Base, 7, 0);

    private static ItemStats Filled() {
        var stats = new ItemStats();
        stats[ItemStats.Type.Constant].Basic[BasicAttribute.MinWeaponAtk] = new BasicOption(3);
        return stats;
    }

    [Test]
    public void RepairedRarity_LowersToHighestRarityWithRows() {
        ItemMetadata weapon = Metadata(RIENA, Option(RIENA));
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, null, Constants(RIENA, 1), NoPicks), Is.EqualTo(1), "rows at 1 only");
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, new ItemStats(), Constants(RIENA, 1, 2, 3), NoPicks), Is.EqualTo(3), "rows 1-3");
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, new ItemStats(), Constants(RIENA, 5, 6), NoPicks), Is.EqualTo(5), "rows above only");
    }

    [Test]
    public void RepairedRarity_LeavesGoodItemsAlone() {
        ItemMetadata weapon = Metadata(RIENA, Option(RIENA));
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, Filled(), Constants(RIENA, 1), NoPicks), Is.Null, "has stats");
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, new ItemStats(), Constants(RIENA, 1, 2, 3, 4, 5), NoPicks), Is.Null, "rarity has rows: the rarity is not why the stats are empty");
        Assert.That(OfflineGearRepair.RepairedRarity(weapon, 4, new ItemStats(), Constants(999), NoPicks), Is.Null, "no rows at all: nothing to build");
        Assert.That(OfflineGearRepair.RepairedRarity(Metadata(RIENA, null), 4, null, Constants(RIENA, 1), NoPicks), Is.Null, "item without options");
    }

    [Test]
    public void RepairedRarity_NeverTouchesSkinsAndPets() {
        Assert.That(OfflineGearRepair.RepairedRarity(Metadata(11620048, Option(11620048), skin: true), 4, null, Constants(11620048, 1), NoPicks), Is.Null, "outfit");
        Assert.That(OfflineGearRepair.RepairedRarity(Metadata(61000079, Option(61000079)), 4, null, Constants(61000079, 1), NoPicks), Is.Null, "pet");
        Assert.That(OfflineGearRepair.RepairedRarity(Metadata(20302151, Option(20302151)), 4, null, Constants(20302151, 1), NoPicks), Is.Null, "box");
    }

    [Test]
    public void RepairedRarity_BonusLinesWithoutBaseStats() {
        ItemStats bonusOnly = new();
        bonusOnly[ItemStats.Type.Random].Basic[BasicAttribute.Strength] = new BasicOption(4);
        Assert.That(OfflineGearRepair.RepairedRarity(Metadata(RIENA, Option(RIENA)), 4, bonusOnly, Constants(RIENA, 1), NoPicks), Is.EqualTo(1));
    }

    [Test]
    public void DungeonGear_RaisedOnlyWhereEpicStatsExist() {
        ItemMetadata weapon = Metadata(RIENA, Option(RIENA));
        Assert.That(OfflineDungeonGear.Rarity(weapon, 1, Constants(RIENA, 1), NoPicks), Is.EqualTo(1), "starter weapon: no Epic rows");
        Assert.That(OfflineDungeonGear.Rarity(weapon, 2, Constants(RIENA, 1, 2, 3, 4, 5), NoPicks), Is.EqualTo(4), "rows up to Legendary");
        Assert.That(OfflineDungeonGear.Rarity(weapon, 1, Constants(RIENA, 1, 5), NoPicks), Is.EqualTo(5), "next rarity with rows");
        Assert.That(OfflineDungeonGear.Rarity(weapon, 5, Constants(RIENA, 4, 5), NoPicks), Is.EqualTo(5), "Legendary stays");
        Assert.That(OfflineDungeonGear.Rarity(Metadata(11620048, Option(11620048), skin: true), 1, Constants(11620048, 4), NoPicks), Is.EqualTo(1), "outfit");
        Assert.That(OfflineDungeonGear.Rarity(Metadata(20302151, Option(20302151)), 1, Constants(20302151, 4), NoPicks), Is.EqualTo(1), "box");
    }

    [Test]
    public void DungeonGear_RealItemsKeepTheirStats() {
        NeedsGameData();
        foreach (int itemId in new[] { RIENA, KNUCKLES, 13150288 }) {
            Assert.That(items.TryGet(itemId, out ItemMetadata? metadata), Is.True);
            int rarity = OfflineDungeonGear.Rarity(metadata!, Create(itemId).Rarity, tables.ItemOptionConstantTable, tables.ItemOptionPickTable);
            Item item = Create(itemId, rarity);
            Assert.That(OfflineGearRepair.HasNoBaseStats(item.Stats), Is.False, $"{metadata!.Name} at rarity {rarity}");
        }
        Assert.That(items.TryGet(RIENA, out ItemMetadata? riena), Is.True);
        Assert.That(OfflineDungeonGear.Rarity(riena!, 1, tables.ItemOptionConstantTable, tables.ItemOptionPickTable), Is.EqualTo(1), "starter weapon stays Normal");
    }

    // Against the game data (skipped without maple-data.db).

    [Test]
    public void StarterItemsOfEveryClass_HaveStats() {
        NeedsGameData();
        int checkedItems = 0;
        foreach ((JobCode job, JobTable.Entry entry) in tables.JobTable.Entries) {
            Assert.That(entry.Tutorial.StartItem, Is.Not.Empty, $"{job} has start items");
            foreach (JobTable.Item start in entry.Tutorial.StartItem) {
                Item item = Create(start.Id); // what TutorialItemHandler does: no rarity given
                Assert.That(item.Stats![ItemStats.Type.Constant].Count, Is.GreaterThan(0), $"{job}: {item.Metadata.Name} has constant stats");
                if (item.Type.IsWeapon && item.Metadata.SlotNames.Contains(EquipSlot.RH)) { // main-hand weapons; shields and books carry defense/magic
                    Assert.That(HasAttack(item), Is.True, $"{job}: {item.Metadata.Name} has attack");
                }
                checkedItems++;
            }
        }
        Assert.That(checkedItems, Is.GreaterThanOrEqualTo(12));
    }

    [Test]
    public void StarterWeaponBuiltAtEpic_IsRepaired() {
        // What 1.1.5 did with EpicGear on: the rarity was raised before the stats were built.
        NeedsGameData();
        Item broken = Create(RIENA, 4);
        Assert.That(OfflineGearRepair.HasNoBaseStats(broken.Stats), Is.True);
        Item? repaired = OfflineGearRepair.Repair(broken, calculator, tables);
        Assert.That(repaired, Is.Not.Null);
        Assert.That(repaired!.Rarity, Is.EqualTo(1));
        Assert.That(HasAttack(repaired), Is.True);
    }

    [Test]
    public void Repair_KeepsEpicGearThatHasRows() {
        NeedsGameData();
        // 13150288 Murpagoth Warrior Dagger has rows at Epic only
        Item dagger = Create(13150288);
        Assert.That(dagger.Rarity, Is.EqualTo(4));
        Assert.That(HasAttack(dagger), Is.True);
        Assert.That(OfflineGearRepair.Repair(dagger, calculator, tables), Is.Null);
    }

    // Repair inside a save.

    private SqliteConnection saveConnection = null!;
    private DbContextOptions saveOptions = null!;

    private GameStorage NewStorage() {
        return new GameStorage(saveOptions, items, maps, achievements, quests, tables, serverTables, NullLogger<GameStorage>.Instance, cubes);
    }

    private long SeedSave(out long uidRiena, out long uidKnuckles, out long uidGood, out long uidSkin, out long uidPet) {
        saveConnection = new SqliteConnection("Data Source=:memory:");
        saveConnection.Open();
        saveOptions = new DbContextOptionsBuilder().UseSqlite(saveConnection).Options;
        using (var context = new Ms2Context(saveOptions)) {
            context.Database.EnsureCreated();
        }

        using GameStorage.Request db = NewStorage().Context();
        long owner = db.CreateAccount(new Account { Username = "repair", MachineId = Guid.NewGuid() }, "password").Id;

        long Stored(Item item, ItemGroup group) {
            item.Group = group;
            return db.CreateItem(owner, item)!.Uid;
        }

        Item equipped = Create(RIENA, 4);
        equipped.Slot = (short) EquipSlot.RH;
        uidRiena = Stored(equipped, ItemGroup.Gear);
        uidKnuckles = Stored(Create(KNUCKLES, 4), ItemGroup.Default);
        uidGood = Stored(Create(13150288), ItemGroup.Default);
        Assert.That(items.TryGet(11620048, out ItemMetadata? skinMetadata), Is.True);
        uidSkin = Stored(new Item(skinMetadata!, 4), ItemGroup.Outfit);
        Assert.That(items.TryGet(61000079, out ItemMetadata? petMetadata), Is.True);
        uidPet = Stored(new Item(petMetadata!, 3), ItemGroup.Default);
        return owner;
    }

    [Test]
    public void RepairOwners_FixesBrokenGear_Idempotent_LogsOneLinePerItem() {
        NeedsGameData();
        long owner = SeedSave(out long riena, out long knuckles, out long good, out long skin, out long pet);
        try {
            var lines = new List<string>();
            ILogger logger = new LoggerConfiguration().WriteTo.Sink(new CollectSink(lines)).CreateLogger();

            int first;
            using (GameStorage.Request db = NewStorage().Context()) {
                first = OfflineGearRepair.RepairOwners(db, calculator, tables, logger, owner, 0);
            }
            Assert.That(first, Is.EqualTo(2));
            Assert.That(lines.Count(line => line.StartsWith("Repaired item")), Is.EqualTo(2), "one line per item");

            using (GameStorage.Request db = NewStorage().Context()) {
                Dictionary<long, Item> all = db.GetAllItems(owner).ToDictionary(item => item.Uid);
                Assert.That(all[riena].Rarity, Is.EqualTo(1));
                Assert.That(HasAttack(all[riena]), Is.True, "Riena has attack");
                Assert.That(all[riena].Group, Is.EqualTo(ItemGroup.Gear), "stays equipped");
                Assert.That(all[riena].Slot, Is.EqualTo((short) EquipSlot.RH), "keeps its slot");
                Assert.That(all[knuckles].Rarity, Is.EqualTo(1));
                Assert.That(HasAttack(all[knuckles]), Is.True, "Knuckles have attack");
                Assert.That(all[good].Rarity, Is.EqualTo(4), "valid Epic untouched");
                Assert.That(all[skin].Rarity, Is.EqualTo(4), "outfit untouched");
                Assert.That(all[pet].Rarity, Is.EqualTo(3), "pet untouched");
            }

            int second;
            using (GameStorage.Request db = NewStorage().Context()) {
                second = OfflineGearRepair.RepairOwners(db, calculator, tables, logger, owner, 0);
            }
            Assert.That(second, Is.EqualTo(0), "second run changes nothing");
        } finally {
            saveConnection.Dispose();
        }
    }

    // SO-02 (BT-190): gems an older upgrade left behind, in a save, through the pass a login runs. Golden: the data's rows.
    private const int WISDOM_1 = 40200001, WISDOM_2 = 40200002, WISDOM_5 = 40200005, WISDOM_10 = 40200010, DESTRUCTION_5 = 40200205, DUST = 40300001, EARRINGS = 11250046;

    private ItemGemstone Socketed(int gemId, int statsOf) => new(gemId, stats: Create(statsOf, Constant.GemstoneGrade).Stats);

    private static int Intelligence(ItemStats? stats) => stats![ItemStats.Type.Constant].Basic[BasicAttribute.Intelligence].Value;

    private static void Set(object target, string member, object? value) {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo? property = target.GetType().GetProperty(member, any);
        if (property != null) {
            property.SetValue(target, value);
            return;
        }
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(member, any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingMemberException(target.GetType().Name, member);
    }

    [Test]
    public void TheLoginPass_GivesGemsTheirTiersStats_AlsoOnGearItRepairs_Idempotent() {
        NeedsGameData();
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        saveOptions = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(saveOptions)) {
            context.Database.EnsureCreated();
        }

        long owner, bagUid, goodUid, dustUid, earringsUid, weaponUid;
        using (GameStorage.Request db = NewStorage().Context()) {
            owner = db.CreateAccount(new Account { Username = "gems", MachineId = Guid.NewGuid() }, "password").Id;

            Item bag = Create(WISDOM_5);
            bag.Stats = Create(WISDOM_1).Stats;
            bagUid = db.CreateItem(owner, bag)!.Uid;
            goodUid = db.CreateItem(owner, Create(WISDOM_2))!.Uid;
            dustUid = db.CreateItem(owner, Create(DUST))!.Uid;

            Item earrings = Create(EARRINGS);
            earrings.Socket = new ItemSocket(3, 2);
            earrings.Socket.Sockets[0] = Socketed(WISDOM_10, WISDOM_1);
            earrings.Socket.Sockets[1] = Socketed(DESTRUCTION_5, DESTRUCTION_5); // a rate holds its value through the save
            earringsUid = db.CreateItem(owner, earrings)!.Uid;

            // Both repairs on one item: built at Epic with no base stats, and with an old gem.
            Item weapon = Create(RIENA, 4);
            weapon.Slot = (short) EquipSlot.RH;
            weapon.Group = ItemGroup.Gear;
            weapon.Socket = new ItemSocket(1, 1);
            weapon.Socket.Sockets[0] = Socketed(WISDOM_2, WISDOM_1);
            weaponUid = db.CreateItem(owner, weapon)!.Uid;
        }

        var lines = new List<string>();
        ILogger logger = new LoggerConfiguration().WriteTo.Sink(new CollectSink(lines)).CreateLogger();
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session);
        Set(session, "Logger", logger);
        Set(session, "CharacterId", owner);
        Set(session, "AccountId", 0L);
        Set(session, "GameStorage", NewStorage());
        Set(session, "TableMetadata", tables);
        Set(session, "ItemMetadata", items);
        Set(session, "ItemStatsCalc", calculator);

        typeof(GameSession).GetMethod("RepairGearWithoutStats", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);

        Assert.That(lines.Any(line => line.StartsWith("Gear repair failed")), Is.False, string.Join(Environment.NewLine, lines));
        using (GameStorage.Request db = NewStorage().Context()) {
            Dictionary<long, Item> all = db.GetAllItems(owner).ToDictionary(item => item.Uid);
            Assert.That(Intelligence(all[bagUid].Stats), Is.EqualTo(13), "a gem in the bag");
            Assert.That(Intelligence(all[goodUid].Stats), Is.EqualTo(4), "a gem that was right");
            Assert.That(all[dustUid].Stats![ItemStats.Type.Constant].Count, Is.Zero, "dust");
            Assert.That(Intelligence(all[earringsUid].Socket!.Sockets[0]!.Stats), Is.EqualTo(36), "a gem in a socket");
            Assert.That(all[earringsUid].Socket!.Sockets[1]!.Stats![ItemStats.Type.Constant].Basic[BasicAttribute.MaxWeaponAtk].Rate, Is.EqualTo(0.026f));
            Assert.That(all[earringsUid].Stats![ItemStats.Type.Constant].Count, Is.GreaterThan(0), "the earrings' own stats stay");
            Assert.That(all[weaponUid].Rarity, Is.EqualTo(1), "the gear pass ran");
            Assert.That(HasAttack(all[weaponUid]), Is.True);
            Assert.That(Intelligence(all[weaponUid].Socket!.Sockets[0]!.Stats), Is.EqualTo(4), "the gem pass ran on the same item");
            Assert.That(all[weaponUid].Group, Is.EqualTo(ItemGroup.Gear));
            Assert.That(all[weaponUid].Slot, Is.EqualTo((short) EquipSlot.RH));
        }

        using (GameStorage.Request db = NewStorage().Context()) {
            Assert.That(OfflineGearRepair.RepairGemstones(db, calculator, items, logger, owner), Is.Zero, "second run changes nothing");
        }
    }

    private sealed class CollectSink(List<string> lines) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }
}
