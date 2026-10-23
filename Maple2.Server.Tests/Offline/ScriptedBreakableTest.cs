using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
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
}
