using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Database.Storage;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>BM-01: the offline Black Market prices gear by level and rarity, picks gear rarities by weight and reads its
/// essentials and seed.</summary>
[NonParallelizable] // Load_ReadsPricingEssentialsAndSeed sets environment variables
public class OfflineMarketPricingTest {
    private static readonly OfflineMarket.GearPricing Pricing = new(20, [1, 2.5, 8, 50, 250, 1000]);

    [Test]
    public void GearPrice_ScalesWithLevelAndRarity() {
        Assert.That(OfflineMarket.GearPrice(Pricing, 70, 5), Is.EqualTo(24_500_000));
        Assert.That(OfflineMarket.GearPrice(Pricing, 50, 4), Is.EqualTo(2_500_000));
        Assert.That(OfflineMarket.GearPrice(Pricing, 0, 1), Is.EqualTo(20), "level counts as 1");
        Assert.That(OfflineMarket.GearPrice(Pricing, 60, 9), Is.EqualTo(OfflineMarket.GearPrice(Pricing, 60, 6)), "rarity counts as 6");
        Assert.That(OfflineMarket.GearPrice(Pricing, 70, 6), Is.GreaterThan(OfflineMarket.GearPrice(Pricing, 70, 5)));
        Assert.That(OfflineMarket.GearPrice(Pricing, 70, 5), Is.GreaterThan(OfflineMarket.GearPrice(Pricing, 60, 5)));
    }

    [Test]
    public void PickRarity_OnlyDefinedRaritiesWithWeight() {
        int[] weights = [0, 0, 30, 50, 18, 2];
        var random = new Random(1);

        List<int?> picks = Enumerable.Range(0, 2000).Select(_ => OfflineMarket.PickRarity([1, 2, 3, 4, 5], weights, random)).ToList();
        Assert.That(picks, Is.All.InRange(3, 5));
        Assert.That(picks, Does.Contain(3).And.Contain(4).And.Contain(5));

        Assert.That(OfflineMarket.PickRarity([1, 2], weights, random), Is.Null, "no weight");
        Assert.That(OfflineMarket.PickRarity([], weights, random), Is.Null, "nothing defined");
        Assert.That(OfflineMarket.PickRarity([6], weights, random), Is.EqualTo(6));
    }

    [Test]
    public void Fill_TakesTheShareNearThenTheOthers() {
        var listed = new List<int>();
        bool ListUnlessSeventh(int x) {
            if (x % 7 == 0) {
                return false;
            }
            listed.Add(x);
            return true;
        }

        List<int> pool = Enumerable.Range(0, 100).ToList();
        Assert.That(OfflineMarket.Fill(pool, x => x < 30, 0.6, 10, ListUnlessSeventh), Is.EqualTo(10));
        Assert.That(listed.Count(x => x < 30), Is.EqualTo(6));
        Assert.That(listed.Count(x => x >= 30), Is.EqualTo(4));
        Assert.That(listed, Has.None.Matches<int>(x => x % 7 == 0));

        listed.Clear();
        Assert.That(OfflineMarket.Fill(pool, x => x < 5, 0.6, 10, ListUnlessSeventh), Is.EqualTo(10), "too few near: the others fill up");
        Assert.That(listed.Count(x => x < 5), Is.EqualTo(4));

        Assert.That(OfflineMarket.Fill(Enumerable.Range(0, 8).ToList(), x => x < 5, 0.6, 10, _ => true), Is.EqualTo(8), "the whole pool");
    }

    [Test]
    public void IsNear_FromBelowToAbove() {
        var range = new OfflineMarket.LevelRange(10, 5, 0.6);
        Assert.That(OfflineMarket.IsNear(50, 60, range), Is.True);
        Assert.That(OfflineMarket.IsNear(65, 60, range), Is.True);
        Assert.That(OfflineMarket.IsNear(49, 60, range), Is.False);
        Assert.That(OfflineMarket.IsNear(66, 60, range), Is.False);
    }

    [Test]
    public void Load_ReadsPricingEssentialsAndSeed() {
        string path = Path.Combine(Path.GetTempPath(), $"offline-market-{Guid.NewGuid():N}.json");
        string?[] saved = new[] { "OFFLINE_PROFILE", "OFFLINE_MARKET", "OFFLINE_MARKET_FILE" }.Select(Environment.GetEnvironmentVariable).ToArray();
        try {
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", "Adapted");
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", null);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", path);
            File.WriteAllText(path, "{ \"count\": 1, \"priceMultiplier\": 3, \"seed\": 7, " +
                "\"pricing\": { \"perLevelSquared\": 20, \"rarity\": [1, 2.5, 8, 50, 250, 1000], \"rarityWeights\": [0, 0, 30, 50, 18, 2] }, " +
                "\"items\": [ { \"id\": 11200006 } ], \"essentials\": [ { \"id\": 40100024, \"amount\": 999, \"price\": 30000 } ] }");

            OfflineMarket.Config? config = OfflineMarket.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Seed, Is.EqualTo(7));
            Assert.That(config.Pricing, Is.Not.Null);
            Assert.That(config.Pricing!.PerLevelSquared, Is.EqualTo(20));
            Assert.That(config.Pricing.Rarity, Has.Length.EqualTo(6));
            Assert.That(config.Pricing.RarityWeights![3], Is.EqualTo(50));
            Assert.That(config.Essentials!.Select(offer => (offer.Id, offer.Rarity, offer.Amount, offer.Price)),
                Is.EqualTo(new[] { (40100024, 0, 999, 30000L) }));

            File.WriteAllText(path, "{ \"count\": 1, \"priceMultiplier\": 3, \"items\": [] }");
            config = OfflineMarket.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Seed, Is.Null);
            Assert.That(config.Pricing, Is.Null);
            Assert.That(config.Essentials, Is.Null);
        } finally {
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", saved[0]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", saved[1]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", saved[2]);
            File.Delete(path);
        }
    }

    [Test]
    public void ShippedConfig_PricesGearByLevelAndListsEssentials() {
        string?[] saved = new[] { "OFFLINE_PROFILE", "OFFLINE_MARKET", "OFFLINE_MARKET_FILE" }.Select(Environment.GetEnvironmentVariable).ToArray();
        try {
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", "Adapted");
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", null);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", Path.Combine(Paths.SOLUTION_DIR, "offline-market.json"));
            OfflineMarket.Config? config = OfflineMarket.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Pricing, Is.Not.Null);
            Assert.That(config.Pricing!.Rarity, Has.Length.EqualTo(6).And.Ordered.Ascending);
            Assert.That(config.Pricing.Rarity.Distinct().Count(), Is.EqualTo(6), "strictly increasing");
            Assert.That(config.Pricing.RarityWeights, Has.Length.EqualTo(6));
            Assert.That(config.Pricing.RarityWeights![0], Is.EqualTo(0), "no Normal gear (maintainer 2026-10-22)");
            Assert.That(config.Levels, Is.Not.Null);
            Assert.That(config.Levels!.Share, Is.EqualTo(0.6));
            Assert.That(config.Items, Has.Length.AtLeast(200));
            Assert.That(config.Essentials, Is.Not.Null.And.Length.AtLeast(200));
            Assert.That(config.Essentials!.All(offer => offer.Price > 0 && offer.Amount >= 1), Is.True);
            Dictionary<int, long> prices = config.Essentials.ToDictionary(offer => offer.Id, offer => offer.Price);
            Assert.That(prices, Contains.Key(40100023).And.ContainKey(40100024), "Onyx and Chaos Onyx Crystals");
            Assert.That(prices[40100024], Is.GreaterThan(prices[40100023]));
            Assert.That(config.Items.Select(offer => offer.Id).Intersect(prices.Keys), Is.Empty, "gear and essentials do not overlap");
        } finally {
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", saved[0]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", saved[1]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", saved[2]);
        }
    }
}
