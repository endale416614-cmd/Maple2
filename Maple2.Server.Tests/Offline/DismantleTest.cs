using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

public class DismantleTest {
    [Test]
    public void ChaosOnyx() {
        Assert.That(ItemDismantleHandler.ChaosOnyx(3), Is.EqualTo((0, 0)));
        Assert.That(ItemDismantleHandler.ChaosOnyx(4), Is.EqualTo((1, 3)));
        Assert.That(ItemDismantleHandler.ChaosOnyx(5), Is.EqualTo((2, 6)));
        Assert.That(ItemDismantleHandler.ChaosOnyx(6), Is.EqualTo((3, 9)));
    }

    [Test]
    public void CanDismantle() {
        Assert.That(ItemDismantleHandler.CanDismantle(false, "(Navigator) Explosive Cape"), Is.True);
        Assert.That(ItemDismantleHandler.CanDismantle(false, "(PC Cafe) Cape"), Is.False);
        Assert.That(ItemDismantleHandler.CanDismantle(true, "Cape"), Is.True);
        Assert.That(ItemDismantleHandler.CanDismantle(false, null), Is.False);
    }
}