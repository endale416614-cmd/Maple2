using System.Text.Json;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.Tools;
using Serilog;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline edition economy: makes Meret (the former cash currency) obtainable with Mesos earned in play.
/// Reads offline-economy.json (path: OFFLINE_ECONOMY_FILE, default next to the solution) and adds the
/// game's own "N-Meret Pouch" items to ordinary meso NPC shops. Without the file nothing changes. Applies in both
/// game modes (Legacy and Adapted).
/// </summary>
public static class OfflineEconomy {
    private const int FIRST_ENTRY_ID = 900000; // shop item ids no shipped shop uses

    public sealed record Config(int MesosPerMeret, MeretExchangeConfig MeretExchange);
    // Category = the shop tab the pouches are listed under ("PS" = Consumables; shops without it show them under All).
    public sealed record MeretExchangeConfig(int[] ShopIds, Pouch[] Pouches, string? Category = null);
    public sealed record Pouch(int ItemId, int Meret);

    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(OfflineEconomy));

    public static Config? Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_ECONOMY_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-economy.json");
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
            Logger.Error(ex, "Invalid offline economy file {Path}; Meret exchange disabled", path);
            return null;
        }
    }

    private sealed record CatalogFile(MeretMarketItemMetadata[] Entries, int[]? New = null);

    // Each New id's place in the file's New list, for FeaturedOrder.
    private static Dictionary<int, int> newPositions = new();

    /// <summary>
    /// Adds the offline Meret catalog (offline-meret-catalog.json, built by tools/build_meret_catalog.py from the
    /// original shop_merat.xml and the gacha prize pools) to the Meret Market. Shipped entries keep priority.
    /// The entries the file's New list names show in the New tab this release; the file keeps their own tab.
    /// </summary>
    public static MeretMarketTable Apply(MeretMarketTable table) {
        newPositions = new Dictionary<int, int>();
        // The client shows SalePrice as the price to pay (the ingest sets it to Price for items without a sale). Offline every item costs and shows its list price, without the end-of-service sale of the client's last data.
        var entries = table.Entries.ToDictionary(pair => pair.Key, pair => pair.Value with { SalePrice = pair.Value.Price });
        string path = Environment.GetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        if (!File.Exists(path)) {
            return new MeretMarketTable(entries);
        }

        CatalogFile? catalog;
        try {
            catalog = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        } catch (JsonException ex) {
            Logger.Error(ex, "Invalid offline Meret catalog {Path}; not loaded", path);
            return new MeretMarketTable(entries);
        }

        int added = 0;
        foreach (int id in (catalog?.New ?? []).Take(NEW_TAB_MAX)) {
            newPositions.TryAdd(id, newPositions.Count);
        }
        foreach (MeretMarketItemMetadata entry in catalog?.Entries ?? []) {
            if (entry.Price > 0 && entries.TryAdd(entry.Id, entry with { SalePrice = entry.Price, TabId = newPositions.ContainsKey(entry.Id) ? NEW_TAB : entry.TabId })) {
                added++;
            }
        }
        Logger.Information("Offline Meret catalog: {Added} entries added from {Path}, {New} New ids", added, path, newPositions.Count);
        return new MeretMarketTable(entries);
    }

    /// <summary>
    /// Where an entry sits on a Featured-style page (Featured, New), which the client pages itself (BT-123): an id of the file's New
    /// list at its place in the list, any other by id. MeretMarketHandler.HandleFeatured sends a page in this order.
    /// </summary>
    public static (int, int) FeaturedOrder(int entryId) => (newPositions.GetValueOrDefault(entryId, NEW_TAB_MAX), entryId);

    private const int PREMIUM_SECTION = 100000;
    private const int RED_MERET_SECTION = 100001;
    // Tabs added by tools/organize_meret_market.py (same ids in the client mod offline-shop).
    private const int DESIGN_TEMPLATES_TAB = 40700;
    private const int FURNITURE_TAB = 40800;
    // The New tab is a Featured page, which the client pages itself: 5 pages of 18 tiles, so 90 entries at most (BT-123).
    private const int NEW_TAB = 11;
    private const int NEW_TAB_MAX = 90;

    /// <summary>
    /// The NA final client enables "MeratMarketNewBM"/"MeratMarketClosing" (end of service), which cut the
    /// premium section down to Functional + Lifestyle. The offline client mod turns those features off again,
    /// so the server must also know the full original tab list: Outfits, Summons, ... plus the offline Design Templates
    /// and Furniture tabs. Featured lists the gacha capsules and the other capsule boxes, Pets the pet capsules.
    /// </summary>
    public static MeretMarketCategoryTable Apply(MeretMarketCategoryTable table) {
        if (!File.Exists(Environment.GetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json")) ||
                !table.Entries.TryGetValue(PREMIUM_SECTION, out IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>? premium) ||
                !table.Entries.TryGetValue(RED_MERET_SECTION, out IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>? full)) {
            return table;
        }

        var tabs = new Dictionary<int, MeretMarketCategoryTable.Tab>(premium);
        foreach ((int tabId, MeretMarketCategoryTable.Tab tab) in full) {
            tabs.TryAdd(tabId, tab);
        }
        tabs.TryAdd(DESIGN_TEMPLATES_TAB, new MeretMarketCategoryTable.Tab([], SortGender: true, SortJob: false, []));
        tabs.TryAdd(FURNITURE_TAB, new MeretMarketCategoryTable.Tab([], SortGender: false, SortJob: false, []));

        var entries = new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>>(table.Entries) {
            [PREMIUM_SECTION] = tabs,
        };
        return new MeretMarketCategoryTable(entries);
    }

    /// <summary>Adds Meret pouches, priced in Mesos, to the configured shops.</summary>
    public static ShopItemTable Apply(ShopItemTable table) {
        Config? config = Load();
        if (config?.MeretExchange == null || config.MesosPerMeret <= 0) {
            return table;
        }

        string category = string.IsNullOrEmpty(config.MeretExchange.Category) ? "PS" : config.MeretExchange.Category;
        var noRequirements = new ShopItemMetadata.Requirement(0, new(0, 0), new(0, 0), new(default, 0), new(default, 0));
        foreach (int shopId in config.MeretExchange.ShopIds) {
            if (!table.Entries.TryGetValue(shopId, out Dictionary<int, ShopItemMetadata>? items) || items.Count == 0) {
                Logger.Warning("Offline economy: shop {ShopId} not found, skipped", shopId);
                continue;
            }

            // Copy an ordinary entry of the same shop so category, sell unit and requirements match its UI.
            ShopItemMetadata template = items.Values.First();
            int entryId = FIRST_ENTRY_ID;
            foreach (Pouch pouch in config.MeretExchange.Pouches) {
                long price = (long) pouch.Meret * config.MesosPerMeret;
                if (pouch.Meret <= 0 || price > int.MaxValue) {
                    Logger.Warning("Offline economy: pouch {ItemId} price out of range, skipped", pouch.ItemId);
                    continue;
                }

                items[entryId] = template with {
                    Id = entryId,
                    ItemId = pouch.ItemId,
                    Rarity = 1,
                    Cost = new ShopCost { Type = ShopCurrencyType.Meso, Amount = (int) price },
                    SellCount = 0,
                    Category = category,
                    Requirements = noRequirements, // the template may need an achievement or reputation
                    RestrictedBuyData = null,
                    Label = ShopItemLabel.None,
                    IconTag = "",
                    RandomOption = false,
                    Probability = 10000,
                    IsPremiumItem = false,
                };
                entryId++;
            }
        }

        return table;
    }
}
