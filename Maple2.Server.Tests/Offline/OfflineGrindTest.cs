using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}
