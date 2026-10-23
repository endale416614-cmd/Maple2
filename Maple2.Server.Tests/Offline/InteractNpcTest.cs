using Maple2.Server.Game.PacketHandlers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class InteractNpcTest {
    [Test]
    public void Cooldown() {
        long ready = 0;
        Assert.That(InteractNpcHandler.TryUse(ref ready, 1000, 7000), Is.True);
        Assert.That(ready, Is.EqualTo(8000));
        Assert.That(InteractNpcHandler.TryUse(ref ready, 7999, 7000), Is.False);
        Assert.That(ready, Is.EqualTo(8000));
        Assert.That(InteractNpcHandler.TryUse(ref ready, 8000, 7000), Is.True);
    }
}
