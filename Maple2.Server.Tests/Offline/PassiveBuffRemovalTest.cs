using System.Collections.Generic;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-05 (BT-254): a passive skill that is unlearned or lowered (a reset, a point taken back, another skill tab) keeps its
/// buff until the next login. The buffs of passives that no longer hold their level are now picked for removal.</summary>
[TestFixture]
public class PassiveBuffRemovalTest {
    private static readonly IReadOnlyDictionary<int, int> Wanted = new Dictionary<int, int> { [10200111] = 0, [10200121] = 3 };

    [Test]
    public void AnUnlearnedPassiveLosesItsBuff() {
        Assert.That(SkillManager.StalePassiveBuffs(Wanted, [(10200111, 5)]), Is.EqualTo(new[] { 10200111 }));
    }

    [Test]
    public void AChangedLevelIsReplaced() {
        Assert.That(SkillManager.StalePassiveBuffs(Wanted, [(10200121, 5)]), Is.EqualTo(new[] { 10200121 }));
    }

    [Test]
    public void AnUnchangedPassiveAndOtherBuffsStay() {
        Assert.That(SkillManager.StalePassiveBuffs(Wanted, [(10200121, 3), (70000001, 1)]), Is.Empty);
    }
}
