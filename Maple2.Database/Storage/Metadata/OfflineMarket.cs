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

    /// <summary>PriceMultiplier applies to the item's sell price when an offer has no Price of its own.</summary>
    public sealed record Config(int Count, double PriceMultiplier, Offer[] Items);
    /// <summary>Rarity 0 = 1; the item gets its stats when it is bought (Open Decision 12).</summary>
    public sealed record Offer(int Id, int Rarity = 0, int Amount = 1, long Price = 0);

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
}
