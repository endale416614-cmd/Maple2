using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-23, NB-26 (BT-42, BT-68): an attack places its splash at each of its targets only without a cube path, whose points place
/// it instead, an NPC's missile's too. Goldens: the GMS captures: the Arcroller's 40353032 at the caster (Port 54177, Port 54209;
/// SK-13); Storm Whip 10900225, one RegionSkill add per CubeMagicPath report whatever the targets (72 of 73 casts, one to eight
/// targets); an NPC's path around the target it hits, a missile's when it hits (CubePointsTest).</summary>
[TestFixture]
public class SplashTargetTest {
    [Test]
    public void AnAttackWithNoCubePathPlacesItAtEachTarget() {
        Assert.That(SkillRecord.SplashAtTargets(0), Is.True);
    }

    // Storm Whip 10900225 (path 109002261) and Smiting Aura 10400121 (path 104001811): the client reports where. Vine Wave 40011001 and
    // Rex's 50002455 (path 7565): SkillState lays the path around each target. Green Hood 29000233's missile 49286011 (path 490450011)
    // and Cyber-Balrog 24100003's 42010201 (path 50000657): SkillState lays it around each target the missile hits, when it hits (NB-26).
    [TestCase(109002261)]
    [TestCase(104001811)]
    [TestCase(40011001)]
    [TestCase(7565)]
    [TestCase(490450011)]
    [TestCase(50000657)]
    public void ACubePathPlacesItInstead(long cubeMagicPathId) {
        Assert.That(SkillRecord.SplashAtTargets(cubeMagicPathId), Is.False);
    }
}
