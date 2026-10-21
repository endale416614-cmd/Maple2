using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Offline;

/// <summary>Offline single-player edition: earned-currency economy and embedded database.</summary>
[NonParallelizable]
public class OfflineEditionTest {
    private string? tempFile;

    [TearDown]
    public void TearDown() {
        Environment.SetEnvironmentVariable("OFFLINE_ECONOMY_FILE", null);
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", null);
        if (tempFile != null && File.Exists(tempFile)) {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void MeretCatalog_LoadsShippedFile() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", path);

        int fileEntries = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Entries").GetArrayLength();
        var shipped = new MeretMarketItemMetadata(1, 0, 10, "", default, 1, 1, 1, 0, 0, default,
            MeretMarketCurrencyType.Meret, 123, 0, 0, 0, (JobFilterFlag) 4095, false, 0, 0, 0, 0, false, false, false, "", 0, 0);
        MeretMarketTable table = OfflineEconomy.Apply(new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata> {
            [shipped.Id] = shipped,
        }));

        Assert.That(fileEntries, Is.GreaterThan(1500));
        Assert.That(table.Entries, Has.Count.EqualTo(fileEntries + 1), "every generated entry is loaded");
        Assert.That(table.Entries.Values.All(entry => entry.Price > 0 && entry.CurrencyType == MeretMarketCurrencyType.Meret));
        Assert.That(table.Entries.Values.Select(entry => entry.ItemId).Distinct().Count(), Is.EqualTo(table.Entries.Count));
        // Entries the game already ships keep priority over the generated catalog.
        Assert.That(table.Entries[shipped.Id].Price, Is.EqualTo(123));
        Assert.That(table.Entries.Values.All(entry => entry.SalePrice == entry.Price), "every entry shows its list price");
    }

    [Test]
    public void MeretCatalog_SalePriceReset() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", path);

        var shipped = new MeretMarketItemMetadata(1, 0, 10, "", default, 1, 1, 1, 0, 0, default,
            MeretMarketCurrencyType.Meret, 500, 250, 0, 0, (JobFilterFlag) 4095, false, 0, 0, 0, 0, false, false, false, "", 0, 0);
        MeretMarketTable table = OfflineEconomy.Apply(new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata> {
            [shipped.Id] = shipped,
        }));

        // Every entry costs and shows its list price (the client shows SalePrice).
        Assert.That(table.Entries[shipped.Id].Price, Is.EqualTo(500));
        Assert.That(table.Entries[shipped.Id].SalePrice, Is.EqualTo(500));
    }

    [Test]
    public void MeretCatalog_SellsItemsAtTheirOwnRarity() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");
        Dictionary<int, int> rarity = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Entries")
            .EnumerateArray().ToDictionary(entry => entry.GetProperty("ItemId").GetInt32(), entry => entry.GetProperty("Rarity").GetInt32());

        Assert.That(rarity.Values, Is.All.InRange(1, 6));
        // Air mounts sold at rarity 1 could not be used in the client; their own rank is 4 (option constant).
        Assert.That(rarity[40400044], Is.EqualTo(4), "Surfboard");
        Assert.That(rarity[40400040], Is.EqualTo(4), "Neon Hoverboard");
        Assert.That(rarity.Values.Distinct().Count(), Is.GreaterThan(1), "not one rarity for everything");
    }

    [Test]
    public void Grind_ExpScaledToTargetKillsPerLevel() {
        // Values from the game's exp table (ExpBase table 2 / NextExp).
        Assert.That(Maple2.Server.Game.Util.OfflineGrind.ExpMultiplier(5966, 46, 200, 50), Is.EqualTo(1f), "Lv10 (130 kills) is never slowed down");
        float lv40 = Maple2.Server.Game.Util.OfflineGrind.ExpMultiplier(23172258, 6060, 200, 50);
        Assert.That(23172258 / (6060 * lv40), Is.EqualTo(200).Within(1), "Lv40: ~3,800 kills -> 200");
        Assert.That(Maple2.Server.Game.Util.OfflineGrind.ExpMultiplier(723217050732, 120744, 200, 50), Is.EqualTo(50f), "capped");
    }

    [Test]
    public void Grind_ShippedFileParses() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json");
        Assume.That(File.Exists(path), "offline-grind.json missing");
        var config = System.Text.Json.JsonSerializer.Deserialize<Maple2.Server.Game.Util.OfflineGrind.Config>(File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.TargetKillsPerLevel, Is.GreaterThan(0));
        Assert.That(config.DropRateMultiplier, Is.GreaterThanOrEqualTo(1));
        Assert.That(config.UpgradeDrops.Select(drop => drop.ItemId), Does.Contain(40100023), "Onyx Crystal");
        Assert.That(config.UpgradeDrops.Select(drop => drop.Chance), Is.All.InRange(0f, 1f));
        Assert.That(config.MesoMultiplier, Is.EqualTo(6f), "monster mesos x6");
    }

    [Test]
    public void Grind_MesoScaling() {
        Assert.That(Maple2.Server.Game.Util.OfflineGrind.ScaledMeso(37, 6f), Is.EqualTo(222));
        Assert.That(Maple2.Server.Game.Util.OfflineGrind.ScaledMeso(1, 1f), Is.EqualTo(1));
        Assert.That(Maple2.Server.Game.Util.OfflineGrind.ScaledMeso(int.MaxValue / 2, 6f), Is.EqualTo(int.MaxValue), "no overflow");
        var old = Maple2.Server.Game.Util.OfflineGrind.Parse("""{ "TargetKillsPerLevel": 200, "UpgradeDrops": [] }""");
        Assert.That(old!.MesoMultiplier, Is.EqualTo(1f), "a grind file without the setting keeps the original mesos");
        Assert.That(old.Chests, Is.Null, "a grind file without chest drops adds nothing to chests");
    }

    [Test]
    public void Grind_ChestDrops() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json");
        Assume.That(File.Exists(path), "offline-grind.json missing");
        Maple2.Server.Game.Util.OfflineGrind.ChestBonus? chests = Maple2.Server.Game.Util.OfflineGrind.Parse(File.ReadAllText(path))!.Chests;
        Assert.That(chests, Is.Not.Null);
        Assert.That(chests!.InteractIds, Does.Contain(11000004), "wooden field chest");
        Assert.That(chests.RareInteractIds, Does.Contain(11000009), "shadow rare chest");
        Assert.That(chests.Drops.Select(drop => drop.Chance), Is.All.InRange(0f, 1f));

        var random = new Random(1);
        List<(int ItemId, int Amount)> lv25 = Enumerable.Range(0, 500)
            .SelectMany(_ => Maple2.Server.Game.Util.OfflineGrind.RollChestDrops(chests, 11000004, 25, random)).ToList();
        Assert.That(lv25.Select(drop => drop.ItemId), Does.Contain(40100001), "Crystal Fragment");
        Assert.That(lv25.Select(drop => drop.ItemId), Has.Some.InRange(31000111, 31000117), "Basic enchant scrolls at Lv25");
        Assert.That(lv25.Select(drop => drop.ItemId), Has.None.InRange(31000120, 31000126), "no Standard scrolls below Lv30");
        Assert.That(lv25.Select(drop => drop.ItemId), Has.None.EqualTo(40100026), "no Metacell below Lv50");

        List<int> lv55 = Enumerable.Range(0, 500)
            .SelectMany(_ => Maple2.Server.Game.Util.OfflineGrind.RollChestDrops(chests, 11000009, 55, random)).Select(drop => drop.ItemId).ToList();
        Assert.That(lv55.Any(id => id is 31000763 or 31000764 or 31000766 or 31000780), "Epic re-rollers");
        Assert.That(lv55.Any(id => id is 31000365 or 31000366 or 31000398 or 31000399 or >= 31000955 and <= 31000957), "socket scrolls");

        Assert.That(Maple2.Server.Game.Util.OfflineGrind.RollChestDrops(chests, 20000001, 55, random), Is.Empty, "telescopes are not chests");
    }

    [Test]
    public void DungeonClears_RaisedOnlyWhenEnabled() {
        // Data as shipped: union dungeon 1006 (30/week, 15/day) and a dungeon without a daily limit (0).
        DungeonRoomMetadata Room(int id, int weekly, int daily) => new(id, 50, default, default, default, 0, 0, 0, [],
            new DungeonRoomRewardMetadata(false, weekly, daily, 0, 0, 0, [], [], 0, 0, 0),
            null!, 0, 0, 0, false, 0, 0, false, [], [], false);
        var shipped = new DungeonRoomTable(new Dictionary<int, DungeonRoomMetadata> {
            [1006] = Room(1006, 30, 15),
            [20001001] = Room(20001001, 3, 0),
        });

        try {
            Environment.SetEnvironmentVariable("OFFLINE_UNLIMITED_DUNGEON_CLEARS", "false");
            Assert.That(OfflineDungeons.Apply(shipped), Is.SameAs(shipped));

            Environment.SetEnvironmentVariable("OFFLINE_UNLIMITED_DUNGEON_CLEARS", "true");
            DungeonRoomTable table = OfflineDungeons.Apply(shipped);
            Assert.That(table.Entries.Values.Select(room => room.Reward.Count), Is.All.EqualTo(9999));
            Assert.That(table.Entries.Values.Select(room => room.Reward.SubRewardCount), Is.All.EqualTo(9999));
            Assert.That(table.Entries[1006].Level, Is.EqualTo(50), "everything else unchanged");
        } finally {
            Environment.SetEnvironmentVariable("OFFLINE_UNLIMITED_DUNGEON_CLEARS", null);
        }
    }

    [Test]
    public void MeretCategories_PremiumSectionGetsFullTabs() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", path);

        MeretMarketCategoryTable.Tab Tab(params int[] sub) => new([], false, false, sub);
        var shipped = new MeretMarketCategoryTable(new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>> {
            // End-of-service layout: the premium section only keeps Functional and Lifestyle.
            [100000] = new Dictionary<int, MeretMarketCategoryTable.Tab> { [40300] = Tab(), [40600] = Tab() },
            [100001] = new Dictionary<int, MeretMarketCategoryTable.Tab> {
                [40100] = Tab(40101, 40102), [40101] = Tab(), [40102] = Tab(), [40200] = Tab(40203), [40203] = Tab(), [40300] = Tab(),
            },
        });

        MeretMarketCategoryTable table = OfflineEconomy.Apply(shipped);

        IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab> premium = table.Entries[100000];
        Assert.That(premium.Keys, Is.SupersetOf(new[] { 40100, 40101, 40102, 40200, 40203, 40300, 40600, 40700, 40800 }));
        Assert.That(premium[40700].SortGender, Is.True, "Design Templates come in (M) and (F) versions");
        Assert.That(premium.Keys, Does.Not.Contain(40110), "capsules are listed under Featured, not Crate Spotlight");
        Assert.That(table.Entries[100001], Is.SameAs(shipped.Entries[100001]));
    }

    [Test]
    public void MeretCatalog_EveryEntrySitsInAKnownTab() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");

        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(path));
        int[] tabs = catalog.RootElement.GetProperty("Entries").EnumerateArray().Select(entry => entry.GetProperty("TabId").GetInt32()).ToArray();
        // Featured, Packages, Outfits and its sub-tabs, Summons sub-tabs, Functional, Expression, Badges, Lifestyle, Design Templates, Furniture
        int[] known = [10, 40000, 40101, 40102, 40103, 40104, 40105, 40106, 40107, 40108, 40109, 40201, 40202, 40203, 40300, 40400, 40500, 40600, 40700, 40800];
        Assert.That(tabs.Except(known), Is.Empty);
        Assert.That(tabs.Count(tab => tab == 10), Is.LessThan(255 - 6), "the Featured page is one packet with a byte slot count (6 shipped entries too)");
        Assert.That(tabs.Count(tab => tab == 40700), Is.GreaterThan(0));
        Assert.That(tabs.Count(tab => tab == 40800), Is.GreaterThan(0));
    }

    [Test]
    public void MeretCatalog_ListsOnlyItemsTheClientShows() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assume.That(File.Exists(path), "offline-meret-catalog.json not generated");

        HashSet<int> items = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("Entries")
            .EnumerateArray().Select(entry => entry.GetProperty("ItemId").GetInt32()).ToHashSet();

        // The NA client hides Wedding and Mentoring items (empty rows) and names 20300484 and 20301697 after other items.
        Assert.That(items.Intersect(new[] { 11050067, 11050068, 11250210, 11301057, 20300484, 20301697 }), Is.Empty);
    }

    [Test]
    public void MeretExchange_AddsPouchesPricedInMesos() {
        tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, """
            { "MesosPerMeret": 200,
              "MeretExchange": { "ShopIds": [117, 999],
                "Pouches": [ { "ItemId": 20300055, "Meret": 10 }, { "ItemId": 20300071, "Meret": 20000000 } ] } }
            """);
        Environment.SetEnvironmentVariable("OFFLINE_ECONOMY_FILE", tempFile);

        ShopItemMetadata potion = new(1, 117, 2000001, 1, new ShopCost { Type = ShopCurrencyType.Meso, Amount = 50 }, 0, "SH",
            new ShopItemMetadata.Requirement(0, new(12345, 1), new(0, 0), new(default, 0), new(default, 0)),
            null, 1, ShopItemLabel.None, "", false, false, 10000, false);
        var table = new ShopItemTable(new Dictionary<int, Dictionary<int, ShopItemMetadata>> {
            [117] = new() { [potion.Id] = potion },
        });

        OfflineEconomy.Apply(table);

        ShopItemMetadata[] pouches = table.Entries[117].Values.Where(item => item.ItemId != potion.ItemId).ToArray();
        Assert.That(pouches, Has.Length.EqualTo(1), "an over-priced pouch must be skipped, not overflow");
        Assert.That(pouches[0].ItemId, Is.EqualTo(20300055));
        Assert.That(pouches[0].Cost.Type, Is.EqualTo(ShopCurrencyType.Meso));
        Assert.That(pouches[0].Cost.Amount, Is.EqualTo(2000));
        Assert.That(pouches[0].SellUnit, Is.EqualTo(potion.SellUnit));
        Assert.That(pouches[0].Category, Is.EqualTo("PS"), "pouches go to the Consumables tab, not the template's tab");
        Assert.That(pouches[0].Requirements.Achievement.Id, Is.EqualTo(0), "the template's requirements must not gate the pouches");
        Assert.That(table.Entries[117][potion.Id], Is.SameAs(potion));
    }

    [Test]
    public void MeretExchange_ShippedConfigCoversEveryShopKind() {
        OfflineEconomy.Config? config = OfflineEconomy.Load();
        Assert.That(config?.MeretExchange, Is.Not.Null, "offline-economy.json must parse (comments inside the array)");
        // General store, potions, food, armor + weapons (Bonin), weapons (Morgan), crafting (Rachael).
        Assert.That(config!.MeretExchange.ShopIds, Is.SupersetOf(new[] { 105, 116, 123, 109, 126, 178 }));
        Assert.That(config.MeretExchange.Category, Is.EqualTo("PS"));
    }

    [Test]
    public void ClassDrops_OnlyGearThePlayerCanWear() {
        JobCode[] knightOnly = [JobCode.Knight];
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor(knightOnly, [JobCode.Knight]), Is.True);
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor(knightOnly, [JobCode.Wizard]), Is.False, "other class gear");
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([], [JobCode.Wizard]), Is.True, "items for every class");
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([JobCode.None], [JobCode.Wizard]), Is.True);
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([JobCode.Archer, JobCode.Knight], [JobCode.Knight]), Is.True);
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([], [JobCode.Berserker], [JobCode.Knight]), Is.False, "Berserker weapon for Knight");
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([], [JobCode.Knight], [JobCode.Knight]), Is.True);
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([], [JobCode.None], [JobCode.Knight]), Is.True);
        Assert.That(Maple2.Server.Game.Util.OfflineClassDrops.AllowedFor([], [], [JobCode.Knight]), Is.True);
    }

    [Test]
    public void TestKit_ShippedKnightKitIsComplete() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-testkit.json");
        Assume.That(File.Exists(path), "offline-testkit.json missing");
        var options = new System.Text.Json.JsonDocumentOptions {
            CommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        System.Text.Json.JsonElement kit = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), options)
            .RootElement.GetProperty("Kits").GetProperty("knight70");

        int[] items = kit.GetProperty("Items").EnumerateArray().Select(item => item.GetInt32()).ToArray();
        Assert.That(items, Has.Length.EqualTo(12).And.Unique);
        Assert.That(kit.GetProperty("Enchant").GetInt32(), Is.EqualTo(15));
        Assert.That(kit.GetProperty("Rarity").GetInt32(), Is.EqualTo(6));
        // The client mod must cover exactly the kit items, or the client would still refuse to equip them.
        string mod = Path.Combine(Paths.SOLUTION_DIR, "..", "client-mods", "offline-testkit", "Data", "Xml", "item");
        Assume.That(Directory.Exists(mod), "client mod not generated");
        int[] modItems = Directory.GetFiles(mod, "*.xml", SearchOption.AllDirectories)
            .Select(file => int.Parse(Path.GetFileNameWithoutExtension(file))).ToArray();
        int[] allKitItems = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path), options).RootElement.GetProperty("Kits")
            .EnumerateObject().SelectMany(entry => entry.Value.GetProperty("Items").EnumerateArray().Select(item => item.GetInt32()))
            .Distinct().ToArray();
        Assert.That(modItems, Is.EquivalentTo(allKitItems));
    }

    [Test]
    public void SaveUpgrade_AddsMissingTablesAndColumns() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var context = new Ms2Context(options);
        context.Database.EnsureCreated();

        // Simulate a save written by an older version: one table and one column are missing.
        context.Database.ExecuteSqlRaw("DROP TABLE \"mail\"");
        context.Database.ExecuteSqlRaw("ALTER TABLE \"account\" DROP COLUMN \"PremiumTime\"");
        int tablesBefore = TableCount(connection);

        IReadOnlyList<string> applied = DbProvider.UpgradeSqliteSchema(context);

        Assert.That(applied, Does.Contain("table mail"));
        Assert.That(applied, Does.Contain("column account.PremiumTime"));
        Assert.That(TableCount(connection), Is.EqualTo(tablesBefore + 1));
        Assert.That(DbProvider.UpgradeSqliteSchema(context), Is.Empty, "running it again changes nothing");

        static int TableCount(SqliteConnection connection) {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    [Test]
    public void SaveMigration_VersionsOldSavesAndRefusesNewerOnes() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var context = new Ms2Context(options);
        context.Database.EnsureCreated();
        Assume.That(DbProvider.GetSaveVersion(context), Is.EqualTo(0), "a save from before versioning");

        IReadOnlyList<string> applied = DbProvider.MigrateSqliteSave(context);
        Assert.That(applied, Has.Some.StartsWith("migration 1"));
        Assert.That(DbProvider.GetSaveVersion(context), Is.EqualTo(DbProvider.SaveSchemaVersion));
        Assert.That(DbProvider.MigrateSqliteSave(context), Is.Empty, "an up-to-date save is left alone");

        context.Database.ExecuteSqlRaw($"PRAGMA user_version = {DbProvider.SaveSchemaVersion + 1}");
        var ex = Assert.Throws<InvalidOperationException>(() => DbProvider.MigrateSqliteSave(context));
        Assert.That(ex!.Message, Does.Contain("newer version"));
    }

    [Test]
    public void Sqlite_TextLookupsIgnoreCaseLikeMySql() {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;

        using (var context = new MetadataContext(options)) {
            context.Database.EnsureCreated();
            context.AnimationMetadata.Add(new AnimationMetadata("11001545_m_bravo", new Dictionary<string, AnimationSequenceMetadata>()));
            context.SaveChanges();
        }

        using (var context = new MetadataContext(options)) {
            // NPC metadata names the model "11001545_M_Bravo"; the animation table stores it lower-case.
            Assert.That(context.AnimationMetadata.Find("11001545_M_Bravo"), Is.Not.Null);
        }
    }
}
