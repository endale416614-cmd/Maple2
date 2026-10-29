using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class BuyCubeTest {
    [Test]
    public void BuyCubeCounter() {
        Assert.That(HousingManager.BuyCubeCounter(new FurnishingShopTable.Entry(1, true, FurnishingCurrencyType.Meso, 500)), Is.EqualTo(500));
        Assert.That(HousingManager.BuyCubeCounter(new FurnishingShopTable.Entry(1, true, FurnishingCurrencyType.Meret, 30)), Is.EqualTo(0));
        Assert.That(HousingManager.BuyCubeCounter(new FurnishingShopTable.Entry(1, true, FurnishingCurrencyType.Meso, 0)), Is.EqualTo(0));
    }
}
