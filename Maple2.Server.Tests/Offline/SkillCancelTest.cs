using NUnit.Framework;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class SkillCancelTest {
    [Test]
    public void CancelsMotion() {
        Assert.That(SkillHandler.CancelsMotion(2, 1), Is.False);
        Assert.That(SkillHandler.CancelsMotion(2, 2), Is.True);
        Assert.That(SkillHandler.CancelsMotion(null, 1), Is.False);
    }
}
