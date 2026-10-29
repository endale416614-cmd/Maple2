using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Every box the offline Meret catalog sells opens to something: the drop data behind it, by the rule of ItemBoxManager.Open.
/// The catalog is kept so by tools/organize_meret_market.py (same rule, in Python).
/// </summary>
public class MeretCatalogBoxTest {
    [Test]
    public void ParseParameters_EmptyFieldIsZero() {
        // 20301245 Music Star's Weapon Box
        Assert.That(ItemBoxManager.ParseParameters("1,,330001788"), Is.EqualTo(new[] { 1, 0, 330001788 }));
        Assert.That(ItemBoxManager.ParseParameters("0,1,0,330001942"), Is.EqualTo(new[] { 0, 1, 0, 330001942 }));
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void EveryCatalogBoxOpens() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        // the variable OfflineEconomy.Apply reads too, so another catalog can be checked
        string catalogPath = Environment.GetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assert.That(File.Exists(catalogPath), catalogPath);
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var entryIds = new Dictionary<int, List<int>>(); // item id -> catalog entry ids
        foreach (JsonElement entry in catalog.RootElement.GetProperty("Entries").EnumerateArray()) {
            int itemId = entry.GetProperty("ItemId").GetInt32();
            if (!entryIds.TryGetValue(itemId, out List<int>? ids)) {
                entryIds[itemId] = ids = [];
            }
            ids.Add(entry.GetProperty("Id").GetInt32());
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var tables = new TableMetadataStorage(context);
        var serverTables = new ServerTableMetadataStorage(context);
        IReadOnlyDictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> individual = serverTables.IndividualDropItemTable.Entries;
        IDictionary<int, Dictionary<int, IList<GlobalDropItemBoxTable.Group>>> global = serverTables.GlobalDropItemBoxTable.DropGroups;
        IReadOnlyDictionary<int, GachaInfoTable.Entry> gacha = tables.GachaInfoTable.Entries;
        HashSet<int> withData = context.ItemMetadata.Select(item => item.Id).ToHashSet();

        // The same checks as ItemBoxManager.Open; a parameter the item does not have makes the server throw, so it counts as a box that is not there.
        bool Opens(ItemMetadataFunction function) {
            if (string.IsNullOrEmpty(function.Parameters)) {
                return false;
            }
            int[] p = ItemBoxManager.ParseParameters(function.Parameters);
            int At(int index) => index < p.Length ? p[index] : -1;
            bool HasRows(int boxId) => individual.TryGetValue(boxId, out IDictionary<int, IndividualDropItemTable.Entry>? box) && CanGiveItem(box);
            // ItemDropManager.CanGiveItem (DR-01): an entry counts only when one of its ids has item data (0 pads the id lists).
            bool CanGiveItem(IDictionary<int, IndividualDropItemTable.Entry> box) => box.Values.Any(group => group.Items.Any(entry => entry.Ids.Any(id => id > 0 && withData.Contains(id))));
            switch (function.Type) {
                case ItemFunction.SelectItemBox:
                    return HasRows(At(1));
                case ItemFunction.OpenItemBoxWithKey:
                    return HasRows(At(5));
                case ItemFunction.OpenGachaBox:
                    return gacha.TryGetValue(At(0), out GachaInfoTable.Entry? entry) && HasRows(entry.DropBoxId);
                default: // OpenItemBox
                    Dictionary<int, IList<GlobalDropItemBoxTable.Group>>? groups = [];
                    return individual.TryGetValue(At(3), out IDictionary<int, IndividualDropItemTable.Entry>? rows)
                           && (At(0) <= 0 || global.TryGetValue(At(0), out groups))
                           && (CanGiveItem(rows) || groups.Count > 0);
            }
        }

        var failures = new List<string>();
        int checkedBoxes = 0;
        List<int> itemIds = entryIds.Keys.ToList();
        foreach (ItemMetadata item in context.ItemMetadata.Where(item => itemIds.Contains(item.Id))) {
            if (item.Function?.Type is not (ItemFunction.SelectItemBox or ItemFunction.OpenItemBox or ItemFunction.OpenItemBoxWithKey or ItemFunction.OpenGachaBox)) {
                continue;
            }
            foreach (int entryId in entryIds[item.Id]) {
                checkedBoxes++;
                if (!Opens(item.Function)) {
                    failures.Add($"{entryId} {item.Name}");
                }
            }
        }

        Assert.That(failures, Is.Empty, $"{failures.Count} catalog box entries open to nothing: {string.Join(", ", failures)}");
        Assert.That(checkedBoxes, Is.GreaterThan(1000), "box entries checked");
    }
}
