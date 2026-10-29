using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FS-01: a sensing region lives for its Delay plus its intervals, and counts the caster only inside the sensor.</summary>
[TestFixture]
public class SensingRegionTest {
    private static SkillEffectMetadataSplash Splash(int interval, int delay) {
        return new SkillEffectMetadataSplash(interval, delay, 0, false, false, false, true, false, false, null);
    }

    [TestCase(30000, 0, 1, 31000)] // Med Kit
    [TestCase(0, 10000, 1, 11000)] // Barbecue Party
    [TestCase(500, 0, -1, 1500)]
    public void SensingEnd(int interval, int delay, int fireCount, int expected) {
        Assert.That(FieldSkill.SensingEnd(1000, Splash(interval, delay), fireCount), Is.EqualTo(expected));
    }

    private static IEnumerable<Prism> Sensor() {
        return [new Prism(new Circle(Vector2.Zero, 75), 0, 150)];
    }

    [Test]
    public void SensesCasterInsideRange() {
        Assert.That(FieldSkill.SensesCaster(IncludeCasterType.Priority, Sensor(), new Vector3(50, 0, 0)), Is.True);
        Assert.That(FieldSkill.SensesCaster(IncludeCasterType.Last, Sensor(), new Vector3(0, 0, 100)), Is.True);
    }

    [Test]
    public void IgnoresCasterOutsideRangeOrExcluded() {
        Assert.That(FieldSkill.SensesCaster(IncludeCasterType.Priority, Sensor(), new Vector3(100, 0, 0)), Is.False);
        Assert.That(FieldSkill.SensesCaster(IncludeCasterType.Exclude, Sensor(), new Vector3(50, 0, 0)), Is.False);
    }
}
