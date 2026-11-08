using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

public class DismantleTest {
    [Test]
    public void ChaosOnyx() {
        // Dismantling_Stuffs-4FCC1.msb: Epic 3, Legendary 4, Ascendant 5.
        Assert.That(ItemDismantleHandler.ChaosOnyx(3), Is.EqualTo(0));
        Assert.That(ItemDismantleHandler.ChaosOnyx(4), Is.EqualTo(3));
        Assert.That(ItemDismantleHandler.ChaosOnyx(5), Is.EqualTo(4));
        Assert.That(ItemDismantleHandler.ChaosOnyx(6), Is.EqualTo(5));
    }

    [Test]
    public void ListedOnyx() {
        // The same capture: Epic 296, Legendary 385, Ascendant 696.
        Assert.That(ItemDismantleHandler.ListedOnyx(3), Is.EqualTo(0));
        Assert.That(ItemDismantleHandler.ListedOnyx(4), Is.EqualTo(296));
        Assert.That(ItemDismantleHandler.ListedOnyx(5), Is.EqualTo(385));
        Assert.That(ItemDismantleHandler.ListedOnyx(6), Is.EqualTo(696));
    }

    [Test]
    public void CanDismantle() {
        Assert.That(ItemDismantleHandler.CanDismantle(false, "(Navigator) Explosive Cape"), Is.True);
        Assert.That(ItemDismantleHandler.CanDismantle(false, "(PC Cafe) Cape"), Is.False);
        Assert.That(ItemDismantleHandler.CanDismantle(true, "Cape"), Is.True);
        Assert.That(ItemDismantleHandler.CanDismantle(false, null), Is.False);
    }
}