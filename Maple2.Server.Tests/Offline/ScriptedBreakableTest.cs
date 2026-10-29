using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class ScriptedBreakableTest {
    [Test]
    public void NextTick() {
        Assert.That(FieldBreakable.NextTick(BreakableState.Hide, triggerOnly: true, 1000, 5000, 40000), Is.EqualTo(0));
        Assert.That(FieldBreakable.NextTick(BreakableState.Break, triggerOnly: true, 1000, 5000, 40000), Is.EqualTo(0));
        Assert.That(FieldBreakable.NextTick(BreakableState.Hide, triggerOnly: false, 1000, 5000, 40000), Is.EqualTo(41000));
        Assert.That(FieldBreakable.NextTick(BreakableState.Break, triggerOnly: false, 1000, 5000, 40000), Is.EqualTo(6000));
        Assert.That(FieldBreakable.NextTick(BreakableState.Show, triggerOnly: false, 1000, 5000, 40000), Is.EqualTo(0));
    }

    [Test]
    public void CycleOffset() {
        foreach (long baseTick in new long[] { 5_000, 0x7FFF_FF00, 0xFFFF_FF00 }) {
            long now = baseTick + 4000;
            Assert.That(unchecked((uint) ((int) now + BreakablePacket.CycleOffset(baseTick))), Is.EqualTo(4100u));
        }
    }
}
