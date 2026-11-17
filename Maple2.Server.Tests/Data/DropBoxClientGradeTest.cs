using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>PI-10 (BT-268): a drop box entry with no rarity of its own takes the client table's grade. The Level 10 to 45 Reward Boxes
/// (items 20301178 to 20301185, drop boxes 330001731 to 330001738) gave their Navigator accessories at rarity 1 where the client shows 2.</summary>
[Category("Data")]
[NonParallelizable]
public class DropBoxClientGradeTest {
    private static IndividualItemDropTable Client(params (int Box, byte Group, int Item, int? Rarity)[] rows) => new(
        rows.GroupBy(row => row.Box).ToDictionary(box => box.Key, box => box.GroupBy(row => row.Group).ToDictionary(group => group.Key,
            group => (IList<IndividualItemDropTable.Entry>) group.Select(row => new IndividualItemDropTable.Entry([row.Item], false, 0, row.Rarity, 0, false, false, false, 1, 1)).ToList())));

    [Test]
    public void TheGradeOfTheBoxGroupAndItem_WhenTheItemExistsAtIt() {
        IndividualItemDropTable client = Client((7, 1, 100, 2), (7, 1, 101, 1), (7, 2, 102, 3), (7, 2, 103, null), (7, 3, 104, 3), (7, 3, 105, 4));
        Assert.That(ItemDropManager.ClientRarity(client, 7, 1, 100, []), Is.EqualTo(2), "an item with no option rows takes the grade");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 2, 102, [2, 3, 4]), Is.EqualTo(3), "a grade the item's tables define");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 3, 104, [4]), Is.EqualTo(-1), "a grade the item does not exist at: the default rarity");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 1, 101, []), Is.EqualTo(-1), "grade 1 is the default anyway");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 2, 103, []), Is.EqualTo(-1), "the client row has no grade");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 2, 100, []), Is.EqualTo(-1), "the item is in another group");
        Assert.That(ItemDropManager.ClientRarity(client, 8, 1, 100, []), Is.EqualTo(-1), "the box is not in the client table");
        Assert.That(ItemDropManager.ClientRarity(client, 7, 257, 100, []), Is.EqualTo(-1), "a group id above 255 is not wrapped onto group 1");
    }

    [Test]
    public void TheNavigatorBoxesGiveRareAccessories_AndTheClassIsCounted() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var tables = new TableMetadataStorage(context);
        var serverTables = new ServerTableMetadataStorage(context);
        Dictionary<int, ItemMetadata> items = context.ItemMetadata.ToDictionary(item => item.Id);

        List<int> Defined(int itemId) => items.TryGetValue(itemId, out ItemMetadata? item)
            ? ItemOptionRarity.Defined(item.Option, tables.ItemOptionConstantTable, tables.ItemOptionStaticTable, tables.ItemOptionRandomTable).ToList() : [];

        int gap = 0, changed = 0;
        var gapBoxes = new HashSet<int>();
        var navigator = new List<(int Box, int Item, int Rarity)>();
        var changes = new SortedDictionary<(int From, int To), int>();
        foreach ((int boxId, IDictionary<int, IndividualDropItemTable.Entry> groups) in serverTables.IndividualDropItemTable.Entries) {
            foreach (IndividualDropItemTable.Entry group in groups.Values) {
                foreach (IndividualDropItemTable.Item item in group.Items.Where(item => item.Rarities.Count == 0)) {
                    int grade = ItemDropManager.ClientRarity(tables.IndividualItemDropTable, boxId, group.GroupId, item.Ids[0], new[] { 2, 3, 4, 5, 6 });
                    if (grade > 0) {
                        gap++;
                        gapBoxes.Add(boxId);
                    }

                    int rarity = ItemDropManager.ClientRarity(tables.IndividualItemDropTable, boxId, group.GroupId, item.Ids[0], Defined(item.Ids[0]));
                    int today = items.TryGetValue(item.Ids[0], out ItemMetadata? metadata)
                        ? ItemOptionRarity.Default(metadata.Option, tables.ItemOptionConstantTable, tables.ItemOptionStaticTable, tables.ItemOptionRandomTable) : 1;
                    if (rarity > 0 && rarity != today) {
                        changed++;
                        changes[(today, rarity)] = changes.GetValueOrDefault((today, rarity)) + 1;
                    }
                    if (boxId is >= 330001731 and <= 330001738) {
                        navigator.Add((boxId, item.Ids[0], rarity));
                    }
                }
            }
        }

        // The 15 accessories of the eight boxes, each at the client's grade 2 (Rare) instead of 1.
        Assert.That(navigator.Count, Is.EqualTo(15));
        Assert.That(navigator.Select(entry => entry.Rarity).Distinct(), Is.EqualTo(new[] { 2 }));
        // The class: entries with no rarity of their own where the client shows a grade above 1, and how many of them change today's rarity.
        TestContext.Out.WriteLine($"gap {gap} in {gapBoxes.Count} boxes; changed {changed}: {string.Join(", ", changes.Select(change => $"{change.Key.From}->{change.Key.To} {change.Value}"))}");
        Assert.That((gap, gapBoxes.Count), Is.EqualTo((9597, 1210)));
        Assert.That(changed, Is.EqualTo(1965));
        Assert.That(changes.Select(change => (change.Key.From, change.Key.To, change.Value)), Is.EqualTo(new[] { (1, 2, 18), (1, 3, 135), (1, 4, 1413), (1, 5, 74), (1, 6, 60), (2, 6, 6), (3, 4, 259) }));
    }
}
