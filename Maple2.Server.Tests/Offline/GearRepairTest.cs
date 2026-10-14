using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
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

    private sealed class CollectSink(List<string> lines) : Serilog.Core.ILogEventSink {
        public void Emit(Serilog.Events.LogEvent logEvent) => lines.Add(logEvent.RenderMessage());
    }
}
