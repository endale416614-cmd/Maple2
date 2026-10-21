using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>B-01: boss HP holds are standing floors that the next phase lowers; a floor is released 60 s after health first reached it.</summary>
public class BossHpFloorTest {
    private const long Total = 1_000_000;

    [Test]
    public void FloorHealth_IsThePercentOfMaxHealth_AtLeast1() {
        Assert.Multiple(() => {
            Assert.That(BossHpFloor.FloorHealth(59, Total), Is.EqualTo(590_000));
            Assert.That(BossHpFloor.FloorHealth(0, Total), Is.EqualTo(0));
            Assert.That(BossHpFloor.FloorHealth(1, 50), Is.EqualTo(1));
        });
    }

    [Test]
    public void AboveTheFloor_HealthStays() {
        Assert.That(BossHpFloor.Step(600_000, Total, 59, 0, 1_000), Is.EqualTo((600_000L, false, 0L)));
    }

    [Test]
    public void BelowTheFloor_HealthRisesToIt_AndTheHoldStarts() {
        Assert.That(BossHpFloor.Step(100, Total, 59, 0, 1_000), Is.EqualTo((590_000L, false, 1_000L)));
    }

    [Test]
    public void HeldUnderTheLimit_TheFloorStays() {
        Assert.That(BossHpFloor.Step(0, Total, 59, 1_000, 1_000 + BossHpFloor.HoldLimitMs - 1), Is.EqualTo((590_000L, false, 1_000L)));
    }

    [Test]
    public void HeldForTheLimit_TheFloorIsReleased() {
        Assert.That(BossHpFloor.Step(0, Total, 59, 1_000, 1_000 + BossHpFloor.HoldLimitMs), Is.EqualTo((0L, true, 0L)));
    }

    [Test]
    public void AHealAboveTheFloor_DoesNotRestartTheCount() {
        // Reached at tick 1,000, healed above it since: the count goes on and still ends at the limit.
        Assert.Multiple(() => {
            Assert.That(BossHpFloor.Step(700_000, Total, 59, 1_000, 30_000), Is.EqualTo((700_000L, false, 1_000L)));
            Assert.That(BossHpFloor.Step(700_000, Total, 59, 1_000, 1_000 + BossHpFloor.HoldLimitMs), Is.EqualTo((700_000L, true, 0L)));
        });
    }

    [Test]
    public void NoFloor_DamageGoesThrough() {
        Assert.That(BossHpFloor.Step(0, Total, 0, 0, 1_000), Is.EqualTo((0L, false, 0L)));
    }
}
