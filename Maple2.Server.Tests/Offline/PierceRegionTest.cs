using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-06 (BT-256): a piercing region (Guardian's Wings' shield 10100203) took the bounce path, which looks in a circle of radius 0
/// around the region's point and made each monster attack itself: 1 damage on a dummy, no kill credit. It now takes the plain path:
/// its targets come from the attack's range, up to the skill's limit, hit as the region's caster.</summary>
[TestFixture]
public class PierceRegionTest {
    [Test]
    public void OnlyAPierceTakesThePlainPath() {
        Assert.That(FieldSkill.TakesBounceBranch(BounceType.None), Is.False);
        Assert.That(FieldSkill.TakesBounceBranch(BounceType.Pierce), Is.False, "10100203, 50100951, 60128001");
        Assert.That(FieldSkill.TakesBounceBranch(BounceType.Chain), Is.True, "10900226");
        Assert.That(FieldSkill.TakesBounceBranch(BounceType.Range), Is.True);
        Assert.That(FieldSkill.TakesBounceBranch(BounceType.Boomerang), Is.True);
    }

    [Test]
    public void TheShieldHitsUpToEightTargets() {
        Assert.That(SkillUtils.MaxTargets(1, BounceType.Pierce, 8), Is.EqualTo(8));
        Assert.That(SkillUtils.MaxTargets(3, BounceType.None, 0), Is.EqualTo(3));
    }
}
