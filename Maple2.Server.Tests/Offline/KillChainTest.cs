using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class KillChainTest {
    [Test]
    public void NextChain() {
        Assert.That(ExperienceManager.NextChain(0, 50, 50, long.MaxValue), Is.EqualTo(1));
        Assert.That(ExperienceManager.NextChain(3, 30, 32, 1000), Is.EqualTo(4));
        Assert.That(ExperienceManager.NextChain(3, 30, 40, 1000), Is.EqualTo(1)); // 10 levels apart, target under 50
        Assert.That(ExperienceManager.NextChain(3, 30, 60, 1000), Is.EqualTo(4)); // target 50+
        Assert.That(ExperienceManager.NextChain(9, 30, 30, 5000), Is.EqualTo(1));
    }
}
