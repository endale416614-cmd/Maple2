using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class SkillTargetLimitTest {
    [Test]
    public void MaxTargets() {
        Assert.That(SkillUtils.MaxTargets(1, BounceType.Pierce, 4), Is.EqualTo(4)); // Rapid Shot
        Assert.That(SkillUtils.MaxTargets(3, BounceType.Pierce, 8), Is.EqualTo(8)); // Aegis Barrage
        Assert.That(SkillUtils.MaxTargets(1, BounceType.Range, 5), Is.EqualTo(6)); // Storm Trigger
        Assert.That(SkillUtils.MaxTargets(1, BounceType.Chain, 3), Is.EqualTo(4)); // Chain Arts
        Assert.That(SkillUtils.MaxTargets(8, BounceType.None, 0), Is.EqualTo(8));
    }

    [Test]
    public void PickClientTargets() {
        Assert.That(SkillUtils.PickClientTargets([5, 5, 7, 9, 11, 13], new HashSet<int> {5, 7, 9, 11, 13}, 4),
            Is.EqualTo(new List<int> { 5, 7, 9, 11 })); // the client's order
        Assert.That(SkillUtils.PickClientTargets([42], new HashSet<int>(), 4), Is.Empty);
    }
}
