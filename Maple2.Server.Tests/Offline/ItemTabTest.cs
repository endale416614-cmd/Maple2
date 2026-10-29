using System.Linq;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class ItemTabTest {
    private static ItemMetadata Metadata(int type, int subType) {
        var fakeProperty = new ItemMetadataProperty(false, 0, 100, type, subType, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var fakeCustomize = new ItemMetadataCustomize(0, 0);
        var fakeLimit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var fakeLife = new ItemMetadataLife(0, 0);
        return new ItemMetadata(1000, "1000", [], "", [], fakeLife, fakeProperty, fakeCustomize, fakeLimit, null, null, [], null, null, null, null);
    }

    [TestCase(8)]
    [TestCase(23)]
    [TestCase(24)]
    [TestCase(25)]
    [TestCase(26)]
    [TestCase(27)]
    [TestCase(-1)]
    [TestCase(99)]
    public void TypeWithoutTab_FilesUnderMisc(int type) {
        Assert.That(ModelExtensions.HasTab(type), Is.False);
        Assert.That(Metadata(type, 0).Inventory(), Is.EqualTo(InventoryType.Misc));
        Assert.DoesNotThrow(() => _ = new Item(Metadata(type, 0)));
    }

    [Test]
    public void GlobalTypesKeepTheirTab() {
        Assert.That(Enumerable.Range(0, 23).Where(t => t != 8).All(ModelExtensions.HasTab), Is.True);
        Assert.That(new Item(Metadata(2, 14)).IsEmote(), Is.True);
        Assert.That(Metadata(2, 14).Inventory(), Is.EqualTo(InventoryType.Misc));
        Assert.That(Metadata(10, 20).Inventory(), Is.EqualTo(InventoryType.FishingMusic));
        Assert.That(Metadata(10, 17).Inventory(), Is.EqualTo(InventoryType.Misc));
        Assert.That(Metadata(2, 2).Inventory(), Is.EqualTo(InventoryType.Consumable));
        Assert.That(Metadata(9, 0).Inventory(), Is.EqualTo(InventoryType.Mount));
    }
}
