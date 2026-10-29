using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools;

namespace Maple2.Server.Tests.Offline;

/// <summary>The Meret Market's New tab (TabId 11) lists the catalog's New ids; the file keeps each entry's own tab.</summary>
[NonParallelizable]
public class MeretNewTabTest {
    private string? tempFile;

    [TearDown]
    public void TearDown() {
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", null);
        if (tempFile != null && File.Exists(tempFile)) {
            File.Delete(tempFile);
        }
    }

    private static MeretMarketItemMetadata Entry(int id, int tabId) => new(id, 0, tabId, "", default, id, 1, 1, 0, 0, default,
        MeretMarketCurrencyType.Meret, 300, 0, 0, 0, (JobFilterFlag) 4095, false, 0, 0, 0, 0, false, false, false, "", 0, 0);

    private MeretMarketTable ApplyFile(MeretMarketItemMetadata[] entries, int[] newIds) {
        tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, JsonSerializer.Serialize(new { Entries = entries, New = newIds }));
        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", tempFile);
        return OfflineEconomy.Apply(new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata>()));
    }

    [Test]
    public void Apply_ShowsTheNewIdsInTheNewTab() {
        MeretMarketTable table = ApplyFile([Entry(1001, 40400), Entry(1002, 40203), Entry(1003, 40500)], [1001, 1002, 9999]);

        Assert.That(table.Entries[1001].TabId, Is.EqualTo(11));
        Assert.That(table.Entries[1002].TabId, Is.EqualTo(11));
        Assert.That(table.Entries[1003].TabId, Is.EqualTo(40500));
        Assert.That(table.Entries.Values.Count(entry => entry.TabId == 11), Is.EqualTo(2));
    }

    [Test]
    public void Apply_ShowsAtMost90NewEntries() {
        int[] ids = Enumerable.Range(1, 300).ToArray();
        MeretMarketTable table = ApplyFile(ids.Select(id => Entry(id, 40400)).ToArray(), ids);

        // The client pages the New tab itself, 5 pages of 18 tiles (BT-123): the first 90 ids of the list.
        Assert.That(table.Entries.Values.Count(entry => entry.TabId == 11), Is.EqualTo(90));
        Assert.That(table.Entries.Values.Where(entry => entry.TabId == 11).Select(entry => entry.Id), Is.EquivalentTo(ids.Take(90)));
    }

    [Test]
    public void FeaturedOrder_NewInListOrderAnyOtherById() {
        MeretMarketTable table = ApplyFile([Entry(5, 10), Entry(3, 10), Entry(9, 10), Entry(1, 10), Entry(7, 10)], [7, 3, 9]);
        int[] Page(int tabId) => table.Entries.Values.Where(entry => entry.TabId == tabId).Select(entry => entry.Id)
            .OrderBy(OfflineEconomy.FeaturedOrder).ToArray();

        // HandleFeatured sends a Featured-style page in this order, not the cache's hash order (BT-123).
        Assert.That(Page(11), Is.EqualTo(new[] { 7, 3, 9 }));
        Assert.That(Page(10), Is.EqualTo(new[] { 1, 5 }));
    }

    [Test]
    public void ShippedCatalog_NewListsThisReleasesAdditions() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        Assert.That(File.Exists(path), Is.True, path);
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(path));
        int[] newIds = catalog.RootElement.GetProperty("New").EnumerateArray().Select(id => id.GetInt32()).ToArray();
        Dictionary<int, JsonElement> entries = catalog.RootElement.GetProperty("Entries").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("Id").GetInt32());

        Assert.That(newIds, Has.Length.InRange(1, 90).And.Unique);
        Assert.That(newIds.All(id => entries.TryGetValue(id, out JsonElement entry) && entry.GetProperty("Price").GetInt64() > 0), Is.True);
        Assert.That(entries.Values.Count(entry => entry.GetProperty("TabId").GetInt32() == 11), Is.EqualTo(0), "the file keeps each entry's own tab");

        Environment.SetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE", path);
        MeretMarketTable table = OfflineEconomy.Apply(new MeretMarketTable(new Dictionary<int, MeretMarketItemMetadata>()));
        Assert.That(table.Entries.Values.Count(entry => entry.TabId == 11), Is.EqualTo(newIds.Length));
    }
}
