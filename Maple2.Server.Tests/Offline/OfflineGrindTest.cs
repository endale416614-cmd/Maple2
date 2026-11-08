using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

public class OfflineGrindTest {
    [Test]
    public void BossChanceAndMultiplier() {
        OfflineGrind.Config? config = OfflineGrind.Parse("""
            {
              "TargetKillsPerLevel": 200, "MaxExpMultiplier": 50, "DropRateMultiplier": 3, "BossMultiplier": 5,
              "UpgradeDrops": [
                { "ItemId": 1, "Name": "Boss only", "MinLevel": 1, "Chance": 0, "BossChance": 1, "Amount": 1, "AmountPerLevel": 0, "BossMultiplier": 1 },
                { "ItemId": 2, "Name": "Everyone", "MinLevel": 1, "Chance": 1, "Amount": 2, "AmountPerLevel": 0 }
              ]
            }
            """);
        Assert.That(config, Is.Not.Null);

        var random = new Random(1);
        Assert.That(OfflineGrind.RollUpgradeDrops(config, 1, false, random), Is.EqualTo(new[] { (2, 2) }));
        // Item 1: its own BossChance and BossMultiplier; item 2: Chance and the config's BossMultiplier 5.
        Assert.That(OfflineGrind.RollUpgradeDrops(config, 1, true, random), Is.EqualTo(new[] { (1, 1), (2, 10) }));
    }

    [Test]
    public void ShippedMeret() {
        OfflineGrind.Config? config = OfflineGrind.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json")));
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.UpgradeDrops.Count(drop => drop.ItemId == 90000004), Is.EqualTo(6));

        // Bosses always get the 1-1,000 drop, and up to two more.
        var random = new Random(1);
        for (int i = 0; i < 100; i++) {
            int[] meret = OfflineGrind.RollUpgradeDrops(config, 70, true, random).Where(drop => drop.ItemId == 90000004).Select(drop => drop.Amount).ToArray();
            Assert.That(meret, Has.Length.InRange(1, 3));
            Assert.That(meret, Is.All.InRange(1, 1000));
        }
    }

    [TestCase(300, true, 10, 10, TestName = "A field Elite waits the shorter time")]
    [TestCase(300, false, 10, 300, TestName = "Other spawns keep their own time")]
    [TestCase(5, true, 10, 5, TestName = "A shorter time of its own stays")]
    [TestCase(0, true, 10, 0, TestName = "A spawn without respawn stays without")]
    [TestCase(300, true, 0, 300, TestName = "EliteRespawnSeconds 0 keeps the original time")]
    public void RespawnSeconds(int cooldown, bool fieldElite, int eliteSeconds, int expected) {
        Assert.That(OfflineGrind.RespawnSeconds(cooldown, fieldElite, eliteSeconds), Is.EqualTo(expected));
    }

    [Test]
    public void ShippedEliteRespawn() {
        OfflineGrind.Config? config = OfflineGrind.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json")));
        Assert.That(config?.EliteRespawnSeconds, Is.EqualTo(10));
    }

    [Test]
    public void RandomAmountAndShippedChaosOnyx() {
        OfflineGrind.Config? config = OfflineGrind.Parse("""
            {
              "TargetKillsPerLevel": 200, "MaxExpMultiplier": 50, "DropRateMultiplier": 3, "BossMultiplier": 5,
              "UpgradeDrops": [
                { "ItemId": 3, "Name": "Range", "MinLevel": 1, "Chance": 1, "Amount": 1, "MaxAmount": 10, "AmountPerLevel": 0 }
              ]
            }
            """);
        Assert.That(config, Is.Not.Null);

        var random = new Random(1);
        var amounts = new HashSet<int>();
        for (int i = 0; i < 500; i++) {
            (int ItemId, int Amount)[] drops = OfflineGrind.RollUpgradeDrops(config, 1, false, random).ToArray();
            Assert.That(drops, Has.Length.EqualTo(1));
            amounts.Add(drops[0].Amount);
        }
        Assert.That(amounts, Is.All.InRange(1, 10));
        Assert.That(amounts, Does.Contain(1));
        Assert.That(amounts, Does.Contain(10));

        // Every boss, any level: exactly one Chaos Onyx drop of 1-10 (the Lv40+ monster row does not apply to bosses).
        config = OfflineGrind.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json")));
        Assert.That(config, Is.Not.Null);
        foreach (int level in new[] { 30, 70 }) {
            for (int i = 0; i < 100; i++) {
                int[] chaosOnyx = OfflineGrind.RollUpgradeDrops(config, level, true, random).Where(drop => drop.ItemId == 40100024).Select(drop => drop.Amount).ToArray();
                Assert.That(chaosOnyx, Has.Length.EqualTo(1));
                Assert.That(chaosOnyx[0], Is.InRange(1, 10));
            }
        }
    }

    // FT-12 (A16): the exp multiplier is that of a common monster of the level, so a boss and a dungeon boss get the same multiple
    // of their own column and keep the original ratio to a common monster, with no cap of their own. Golden: monsterExpTable level 40.
    [Test]
    public void ABossKeepsItsRankRatio() {
        var table = new Maple2.Model.Metadata.MonsterExpTable(new Dictionary<int, Maple2.Model.Metadata.MonsterExpTable.Entry> {
            [40] = new(Common: 6060, Elite: 13786, DungeonElite: 45450, Boss: 155829, DungeonBoss: 218160),
        });
        float multiplier = OfflineGrind.ExpMultiplier(23172258, table.Entries[40].Common, 200, 50); // NextExp of level 40
        long Exp(int npcClass) => (long) (Maple2.Server.Game.Manager.ExperienceManager.MonsterExp(table, npcClass, 40)!.Value * multiplier);

        Assert.That(multiplier, Is.EqualTo(19.12f).Within(0.01f));
        Assert.That(Exp(3) / (double) Exp(1), Is.EqualTo(155829 / 6060.0).Within(0.001), "a boss: 25.7 times a common monster, as in the table");
        Assert.That(Exp(6) / (double) Exp(1), Is.EqualTo(218160 / 6060.0).Within(0.001), "a dungeon boss: 36 times");
    }

    // GR-01 (R-19, R-21): the shipped Metacell and gem dust rows against what the game asks for them.
    private const int CrystalFragment = 40100001, BlueCrystal = 40100003, Metacell = 40100026;
    private const int Blue = 40300001, Purple = 40300002, Orange = 40300003, White = 40300004, Red = 40300005, Green = 40300006, Cyan = 40300007, Yellow = 40300008;
    private const int ArmorType = 13, Epic = 4, Legendary = 5;
    private const int WoodenChest = 11000004, RareChest = 11000009;

    /// <summary>Every roll drops, so a row's amount can be read at a level.</summary>
    private sealed class EveryRollDrops : Random {
        public override float NextSingle() => 0f;
    }

    private static OfflineGrind.Config Shipped() => OfflineGrind.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json")))!;

    private static double PerKill(OfflineGrind.Config config, int itemId, int level) =>
        (double) config.UpgradeDrops.Single(drop => drop.ItemId == itemId).Chance
        * OfflineGrind.RollUpgradeDrops(config, level, false, new EveryRollDrops()).Single(drop => drop.ItemId == itemId).Amount;

    /// <summary>Kills of monsters of this level until the rows have paid every cost: the slowest material decides.</summary>
    private static int Kills(OfflineGrind.Config config, int level, params (int ItemId, int Cost)[] costs) =>
        (int) Math.Ceiling(costs.Max(cost => cost.Cost / PerKill(config, cost.ItemId, level)));

    private static (int ItemId, int Cost)[] FirstArmorAttempt(int grade) {
        (_, int crystals, _, int fragments, _, int metacells) = Maple2.Server.Game.LuaFunctions.Lua.CalcGetItemRemakeIngredientNew(ArmorType, 0, grade, 60);
        return [(BlueCrystal, crystals), (CrystalFragment, fragments), (Metacell, metacells)];
    }

    [Test]
    public void ShippedMetacellRows() {
        OfflineGrind.Config config = Shipped();
        OfflineGrind.UpgradeDrop kill = config.UpgradeDrops.Single(drop => drop.ItemId == Metacell);
        Assert.That((kill.MinLevel, kill.Chance, kill.Amount, kill.AmountPerLevel), Is.EqualTo((50, 0.15f, 1, 0.10f)));
        OfflineGrind.ChestDrop chest = config.Chests!.Drops.Single(drop => drop.ItemIds.Contains(Metacell));
        Assert.That((chest.MinLevel, chest.Chance, chest.Amount, chest.AmountPerLevel), Is.EqualTo((50, 0.40f, 3, 0.20f)));
    }

    [Test]
    public void ShippedGemDustRows() {
        OfflineGrind.Config config = Shipped();
        int[] colors = [Blue, Purple, Orange, White, Red, Green, Cyan, Yellow];
        foreach (int color in colors) {
            OfflineGrind.UpgradeDrop kill = config.UpgradeDrops.Single(drop => drop.ItemId == color);
            Assert.That((kill.MinLevel, kill.Chance, kill.Amount, kill.AmountPerLevel), Is.EqualTo((color == Blue ? 30 : 50, 0.05f, 1, 0.20f)));
        }
        OfflineGrind.ChestDrop chest = config.Chests!.Drops.Single(drop => drop.ItemIds.Contains(Blue));
        Assert.That(chest.ItemIds, Is.EquivalentTo(colors));
        Assert.That((chest.MinLevel, chest.Chance, chest.Amount, chest.AmountPerLevel), Is.EqualTo((30, 0.60f, 2, 0.10f)));
    }

    [Test]
    public void ChestAmountsAtLevel60() {
        OfflineGrind.ChestBonus chests = Shipped().Chests!;
        int Amount(int interactId, Func<int, bool> item) =>
            OfflineGrind.RollChestDrops(chests, interactId, 60, new EveryRollDrops()).Single(drop => item(drop.ItemId)).Amount;

        Assert.That(Amount(WoodenChest, id => id == Metacell), Is.EqualTo(15));
        Assert.That(Amount(RareChest, id => id == Metacell), Is.EqualTo(45));
        Assert.That(Amount(WoodenChest, id => id is >= Blue and <= Yellow), Is.EqualTo(8));
        Assert.That(Amount(RareChest, id => id is >= Blue and <= Yellow), Is.EqualTo(24));
    }

    // Golden: the cost formula (the client's calcGetItemRemakeIngredientNew) and Wisdom 40200001's first level in itemgemstoneupgrade.xml.
    [TestCase(50, 112, 223, 146)]
    [TestCase(60, 96, 191, 124)]
    [TestCase(70, 84, 167, 107)]
    public void FirstAttemptsAndAGemLevelInKills(int level, int epic, int legendary, int gemLevel) {
        OfflineGrind.Config config = Shipped();
        Assert.That(FirstArmorAttempt(Epic), Is.EqualTo(new[] { (BlueCrystal, 1), (CrystalFragment, 200), (Metacell, 11) }));
        Assert.That(FirstArmorAttempt(Legendary), Is.EqualTo(new[] { (BlueCrystal, 5), (CrystalFragment, 400), (Metacell, 165) }));

        Assert.That(Kills(config, level, FirstArmorAttempt(Epic)), Is.EqualTo(epic));
        Assert.That(Kills(config, level, FirstArmorAttempt(Legendary)), Is.EqualTo(legendary));
        Assert.That(Kills(config, level, (Metacell, 165)), Is.LessThan(legendary), "Crystal Fragments are the limit, not Metacells");
        Assert.That(Kills(config, level, (Blue, 80), (Cyan, 24), (Orange, 16), (CrystalFragment, 40)), Is.EqualTo(gemLevel));
    }

    // Golden: itemgemstoneupgrade.xml. Every gem's first level asks 80, 24 and 16 of its three colors and 40 Crystal Fragments.
    [TestCase(Blue, Cyan, Orange, TestName = "Wisdom's first level can be farmed")]
    [TestCase(Purple, Orange, White, TestName = "Luck's first level can be farmed")]
    [TestCase(Orange, Green, Red, TestName = "Destruction's first level can be farmed")]
    [TestCase(White, Blue, Green, TestName = "Life's first level can be farmed")]
    [TestCase(Red, White, Yellow, TestName = "Power's first level can be farmed")]
    [TestCase(Green, Yellow, Cyan, TestName = "Dexterity's first level can be farmed")]
    [TestCase(Cyan, Red, Purple, TestName = "Accuracy's first level can be farmed")]
    [TestCase(Yellow, Purple, Blue, TestName = "Offense's first level can be farmed")]
    public void EveryGemIsPaidByKills(int main, int second, int third) {
        Assert.That(Kills(Shipped(), 60, (main, 80), (second, 24), (third, 16), (CrystalFragment, 40)), Is.EqualTo(124));
    }

    [Test]
    public void LegacyDoesNotReadTheRows() {
        // Enabled reads the environment once, so the gate is checked by its source lines and the pure rule.
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Util", "OfflineGrind.cs"));
        Assert.That(source, Does.Contain("OfflineProfile.Flag(\"OFFLINE_GRIND\", true)"));
        Assert.That(source, Does.Contain("Settings => Enabled ? LazyConfig.Value : null"));
        Assert.That(source, Does.Contain("RollUpgradeDrops(Settings, monsterLevel, boss, random)"));
        Assert.That(source, Does.Contain("RollChestDrops(Settings?.Chests, interactId, playerLevel, random)"));

        Assert.That(OfflineProfile.Resolve("Legacy", null, true), Is.False);
        Assert.That(OfflineProfile.Resolve("Adapted", null, true), Is.True);
        Assert.That(OfflineGrind.RollUpgradeDrops(null, 60, false, new EveryRollDrops()), Is.Empty);
        Assert.That(OfflineGrind.RollChestDrops(null, WoodenChest, 60, new EveryRollDrops()), Is.Empty);
    }
}
