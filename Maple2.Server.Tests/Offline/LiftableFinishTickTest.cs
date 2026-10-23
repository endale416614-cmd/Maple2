using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class LiftableFinishTickTest {
    [Test]
    public void TestFinishTickFor() {
        long now = 1000;

        // (lifetime 0, finish 0, off the box) long.MaxValue
        var liftable1 = new Liftable(30000362, 1, 0, 600000, 0, "0", "", "0", "0", true, Vector3.Zero, Vector3.Zero);
        Assert.That(FieldLiftable.FinishTickFor(now, liftable1, false), Is.EqualTo(long.MaxValue));

        // (0, 0, on the box) 1000
        Assert.That(FieldLiftable.FinishTickFor(now, liftable1, true), Is.EqualTo(1000L));

        // (0, 5000, off) long.MaxValue
        var liftable2 = new Liftable(30000362, 1, 0, 600000, 5000, "0", "", "0", "0", true, Vector3.Zero, Vector3.Zero);
        Assert.That(FieldLiftable.FinishTickFor(now, liftable2, false), Is.EqualTo(long.MaxValue));

        // (0, 5000, on) 6000
        Assert.That(FieldLiftable.FinishTickFor(now, liftable2, true), Is.EqualTo(6000L));

        // (3000, 3000, off) 4000
        var liftable3 = new Liftable(30000362, 1, 3000, 600000, 3000, "0", "", "0", "0", true, Vector3.Zero, Vector3.Zero);
        Assert.That(FieldLiftable.FinishTickFor(now, liftable3, false), Is.EqualTo(4000L));
    }
}
