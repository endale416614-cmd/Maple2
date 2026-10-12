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
/// game's own "N-Meret Pouch" items to ordinary meso NPC shops. Without the file nothing changes.
/// </summary>
public static class OfflineEconomy {
    private const int FIRST_ENTRY_ID = 900000; // shop item ids no shipped shop uses

    public sealed record Config(int MesosPerMeret, MeretExchangeConfig MeretExchange);
    // Category = the shop tab the pouches are listed under ("PS" = Consumables; shops without it show them under All).
    public sealed record MeretExchangeConfig(int[] ShopIds, Pouch[] Pouches, string? Category = null);
    public sealed record Pouch(int ItemId, int Meret);

    private static readonly ILogger Logger = Log.Logger.ForContext(typeof(OfflineEconomy));

    // Doctor shop (offline-doctor-shop.json, built by tools/build_doctor_shop.py): NPCs that sell every potion and
    // support item for Mesos. Path: OFFLINE_DOCTOR_SHOP_FILE, default next to the solution. Without the file, no change.
    public sealed record DoctorShop(int[] NpcIds, int ShopId, int TemplateShopId, DoctorShopItem[] Items);
    public sealed record DoctorShopItem(int ItemId, string Name, int Price);
    private static readonly Lazy<DoctorShop?> Doctor = new(() => LoadDoctorShop(
        Environment.GetEnvironmentVariable("OFFLINE_DOCTOR_SHOP_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-doctor-shop.json")));

    public static DoctorShop? LoadDoctorShop(string path) {
        if (!File.Exists(path)) {
            return null;
        }
        try {
            DoctorShop? shop = JsonSerializer.Deserialize<DoctorShop>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return shop is { ShopId: > 0, NpcIds: not null, Items: not null } ? shop : null;
        } catch (JsonException ex) {
            Logger.Error(ex, "Invalid doctor shop file {Path}; not loaded", path);
            return null;
        }
    }

    /// <summary>The shop an NPC without a shipped shop opens in the offline edition (0 = none).</summary>
    public static int ShopForNpc(int npcId) => Doctor.Value is { } shop && shop.NpcIds.Contains(npcId) ? shop.ShopId : 0;

    /// <summary>Adds the doctor shop (its window copied from the template shop).</summary>
    public static ShopTable Apply(ShopTable table) => Apply(table, Doctor.Value);

    public static ShopTable Apply(ShopTable table, DoctorShop? doctor) {
        if (doctor == null || table.Entries.ContainsKey(doctor.ShopId) || !table.Entries.TryGetValue(doctor.TemplateShopId, out ShopMetadata? template)) {
            return table;
        }
        var entries = new Dictionary<int, ShopMetadata>(table.Entries) {
            [doctor.ShopId] = template with { Id = doctor.ShopId, DisplayOnlyUsable = false },
        };
        return new ShopTable(entries);
    }

    /// <summary>The doctor shop's items: one ordinary entry of the template shop per item, priced in Mesos.</summary>
    public static void AddDoctorShopItems(Dictionary<int, Dictionary<int, ShopItemMetadata>> entries, DoctorShop? doctor) {
        if (doctor == null || entries.ContainsKey(doctor.ShopId)
            || !entries.TryGetValue(doctor.TemplateShopId, out Dictionary<int, ShopItemMetadata>? templateItems) || templateItems.Count == 0) {
            return;
        }
        ShopItemMetadata template = templateItems.Values.First();
        var items = new Dictionary<int, ShopItemMetadata>();
        int entryId = 1;
        foreach (DoctorShopItem item in doctor.Items.Where(item => item.Price > 0)) {
            items[entryId] = template with {
                Id = entryId,
                ShopId = doctor.ShopId,
                ItemId = item.ItemId,
                Rarity = 1,
                Cost = new ShopCost { Type = ShopCurrencyType.Meso, Amount = item.Price },
                SellCount = 0,
                Category = "PS",
                RestrictedBuyData = null,
                Label = ShopItemLabel.None,
                IconTag = "",
                RandomOption = false,
                Probability = 10000,
                IsPremiumItem = false,
            };
            entryId++;
        }
        entries[doctor.ShopId] = items;
        Logger.Information("Offline doctor shop {ShopId}: {Count} potions and support items for NPCs {Npcs}", doctor.ShopId, items.Count, doctor.NpcIds);
    }

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

    private sealed record CatalogFile(MeretMarketItemMetadata[] Entries);

    /// <summary>
    /// Adds the offline Meret catalog (offline-meret-catalog.json, built by tools/build_meret_catalog.py from the
    /// original shop_merat.xml and the gacha prize pools) to the Meret Market. Shipped entries keep priority.
    /// </summary>
    public static MeretMarketTable Apply(MeretMarketTable table) {
        string path = Environment.GetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        if (!File.Exists(path)) {
            return table;
        }

        CatalogFile? catalog;
        try {
            catalog = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        } catch (JsonException ex) {
            Logger.Error(ex, "Invalid offline Meret catalog {Path}; not loaded", path);
            return table;
        }

        var entries = new Dictionary<int, MeretMarketItemMetadata>(table.Entries);
        int added = 0;
        foreach (MeretMarketItemMetadata entry in catalog?.Entries ?? []) {
            if (entry.Price > 0 && entries.TryAdd(entry.Id, entry)) {
                added++;
            }
        }
        Logger.Information("Offline Meret catalog: {Added} entries added from {Path}", added, path);
        return new MeretMarketTable(entries);
    }

    private const int PREMIUM_SECTION = 100000;
    private const int RED_MERET_SECTION = 100001;
    private const int OUTFITS_TAB = 40100;
    private const int CRATE_SPOTLIGHT_TAB = 40110;

    /// <summary>
    /// The NA final client enables "MeratMarketNewBM"/"MeratMarketClosing" (end of service), which cut the
    /// premium section down to Functional + Lifestyle. The offline client mod turns those features off again,
    /// so the server must also know the full original tab list: Outfits (incl. Crate Spotlight), Summons, ...
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
        if (tabs.TryGetValue(OUTFITS_TAB, out MeretMarketCategoryTable.Tab? outfits) && !outfits.SubTabIds.Contains(CRATE_SPOTLIGHT_TAB)) {
            tabs[OUTFITS_TAB] = outfits with { SubTabIds = [CRATE_SPOTLIGHT_TAB, .. outfits.SubTabIds] };
            tabs.TryAdd(CRATE_SPOTLIGHT_TAB, new MeretMarketCategoryTable.Tab([], true, false, []));
        }

        var entries = new Dictionary<int, IReadOnlyDictionary<int, MeretMarketCategoryTable.Tab>>(table.Entries) {
            [PREMIUM_SECTION] = tabs,
        };
        return new MeretMarketCategoryTable(entries);
    }

    /// <summary>Adds Meret pouches, priced in Mesos, to the configured shops, and the doctor shop.</summary>
    public static ShopItemTable Apply(ShopItemTable table) {
        if (table.Entries is Dictionary<int, Dictionary<int, ShopItemMetadata>> mutable) {
            AddDoctorShopItems(mutable, Doctor.Value);
        } else {
            var copy = new Dictionary<int, Dictionary<int, ShopItemMetadata>>(table.Entries);
            AddDoctorShopItems(copy, Doctor.Value);
            table = new ShopItemTable(copy);
        }
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
