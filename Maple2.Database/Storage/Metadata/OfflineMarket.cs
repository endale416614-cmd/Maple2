using System.Text.Json;
using Maple2.Database.Context;
using Maple2.Tools;
using Serilog;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline Black Market (R12-22, Adapted only): with one player the market is always empty. offline-market.json (path:
/// OFFLINE_MARKET_FILE, default next to the solution) names a pool of items; at every World start Count of them, picked
/// at random, are listed by a synthetic seller (<see cref="SellerId"/>) in place of the previous synthetic set. Without
/// the file, or under Legacy (OFFLINE_MARKET=false), there is no synthetic listing.
/// </summary>
public static class OfflineMarket {
    public const long SellerId = -1; // CharacterId of the synthetic listings: no character has it

    /// <summary>PriceMultiplier applies to the item's sell price when an offer has no Price of its own and there is no Pricing. Essentials are listed at every World start, each in its own Amount and Price; with a Seed every World start lists the same set. Levels takes a share of the gear near the player's level.</summary>
    public sealed record Config(int Count, double PriceMultiplier, Offer[] Items, GearPricing? Pricing = null, Offer[]? Essentials = null, int? Seed = null, LevelRange? Levels = null);
    /// <summary>Rarity 0 = 1; the item gets its stats when it is bought (Open Decision 12).</summary>
    public sealed record Offer(int Id, int Rarity = 0, int Amount = 1, long Price = 0);

    /// <summary>A Share (0-1) of the gear listings comes from gear of a level from playerLevel - Below to playerLevel + Above, the
    /// player level being that of the character saved last; the rest from the other levels (BM-04).</summary>
    public sealed record LevelRange(int Below, int Above, double Share);

    /// <summary>Gear price = PerLevelSquared x level x level x Rarity[rarity - 1] (level at least 1). RarityWeights (Normal to
    /// Ascendant) pick the rarity of a gear offer without one, among the rarities its option tables define.</summary>
    public sealed record GearPricing(double PerLevelSquared, double[] Rarity, int[]? RarityWeights = null);

    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(OfflineMarket));

    public static Config? Load() {
        if (!OfflineProfile.Flag("OFFLINE_MARKET", true)) {
            return null;
        }

        string path = Environment.GetEnvironmentVariable("OFFLINE_MARKET_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-market.json");
        if (!File.Exists(path)) {
            return null;
        }

        try {
            return JsonSerializer.Deserialize<Config>(File.ReadAllText(path), new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        } catch (JsonException ex) {
            Logger.Error(ex, "Invalid offline market file {Path}; no synthetic listings", path);
            return null;
        }
    }

    public static long GearPrice(GearPricing pricing, int level, int rarity) {
        double factor = pricing.Rarity.Length == 0 ? 1 : pricing.Rarity[Math.Min(Math.Clamp(rarity, 1, 6), pricing.Rarity.Length) - 1];
        double price = pricing.PerLevelSquared * Math.Max(1, level) * Math.Max(1, level) * factor;
        return price >= long.MaxValue ? long.MaxValue : (long) Math.Max(0, price);
    }

    /// <summary>One of <paramref name="defined"/> by weight (weights[rarity - 1]); null when none of them has a positive weight.</summary>
    public static int? PickRarity(IEnumerable<int> defined, int[] weights, Random random) {
        List<int> candidates = defined.Where(rarity => rarity >= 1 && rarity <= weights.Length && weights[rarity - 1] > 0)
            .Distinct().OrderBy(rarity => rarity).ToList();
        int total = candidates.Sum(rarity => weights[rarity - 1]);
        if (total <= 0) {
            return null;
        }

        int roll = random.Next(total);
        foreach (int rarity in candidates) {
            roll -= weights[rarity - 1];
            if (roll < 0) {
                return rarity;
            }
        }
        return candidates[^1];
    }

    public static bool IsNear(int itemLevel, int playerLevel, LevelRange range) {
        return itemLevel >= playerLevel - range.Below && itemLevel <= playerLevel + range.Above;
    }

    /// <summary>Lists up to <paramref name="count"/> of <paramref name="shuffled"/>: first <paramref name="share"/> of the count from the
    /// near ones, then the rest from the others, then from whatever is left. <paramref name="list"/> lists one and returns false
    /// when it cannot (that one does not count and is not tried again). Returns how many were listed.</summary>
    public static int Fill<T>(IReadOnlyList<T> shuffled, Func<T, bool> near, double share, int count, Func<T, bool> list) {
        var tried = new bool[shuffled.Count];
        int Take(Func<T, bool> where, int limit) {
            int taken = 0;
            for (int i = 0; i < shuffled.Count && taken < limit; i++) {
                if (!tried[i] && where(shuffled[i])) {
                    tried[i] = true;
                    if (list(shuffled[i])) {
                        taken++;
                    }
                }
            }
            return taken;
        }

        int listed = Take(near, (int) Math.Round(count * Math.Clamp(share, 0, 1)));
        listed += Take(item => !near(item), count - listed);
        listed += Take(_ => true, count - listed);
        return listed;
    }
}
