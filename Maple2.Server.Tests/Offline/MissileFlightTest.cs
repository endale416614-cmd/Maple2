using System;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-25 (BT-68, C4): an NPC's missile hits when it arrives, its nearest target's distance over its path's velocity after the
/// fire. Goldens: the GMS captures' SkillDamage fires and hits of NPC missiles (11,838 pairs, no client packet before the hits), against
/// the caster's NpcControl position and the capturing player's UserSync at the fire.</summary>
[TestFixture]
public class MissileFlightTest {
    // A magic path move; only Velocity is read.
    private static MagicPath Move(float velocity, float distance) =>
        new(false, 0, true, false, true, false, false, false, default, default, default, default, default, default,
            false, 0, velocity, distance, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    [TestCase(1200f, 2500f, 2431f, 2025.8)] // Green Hood 29000233's 49286011 (path 49063010): captured 2,051 ms; 223 hits a median 26 ms late
    [TestCase(600f, 3500f, 2155f, 3591.7)] // Monto 21421716's 50000671 (path 5620): captured 3,597 ms; 85 hits a median 5 ms late
    [TestCase(1600f, 450f, 411f, 256.9)] // Devil Stirge 29000236's 40148011: captured 243 ms; 332 hits a median 14 ms early
    [TestCase(0f, 600f, 539f, 0.0)] // Asimov Barrier Launch 29000278's 70000561 does not move: 415 hits a median 0 ms after the fire
    public void AMissileFliesItsTargetsDistanceOverItsVelocity(float velocity, float distance, float targetDistance, double milliseconds) {
        Assert.That(SkillRecord.MissileFlight([Move(velocity, distance)], targetDistance).TotalMilliseconds, Is.EqualTo(milliseconds).Within(0.1));
    }

    [Test]
    public void APathWithNoMoveHitsAtOnce() {
        Assert.That(SkillRecord.MissileFlight([], 500), Is.EqualTo(TimeSpan.Zero));
    }

    // The cast's next attack point clears its record's targets (TrySetAttackPoint) while the missile flies: the copy keeps them.
    [Test]
    public void AMissileKeepsItsTargetsWhenTheCastMovesOn() {
        var cast = new SkillRecord(new SkillMetadata(40148011, 1, null, null!, null!, null!), 7, null!) { TargetUid = 3 };
        cast.Targets[101] = null!;
        SkillRecord missile = cast.Missile();
        cast.Targets.Clear();
        cast.Targets[102] = null!;

        Assert.Multiple(() => {
            Assert.That(missile.Targets.Keys, Is.EqualTo(new[] { 101 }));
            Assert.That(cast.Targets.Keys, Is.EqualTo(new[] { 102 }));
            Assert.That((missile.CastUid, missile.TargetUid, missile.SkillId), Is.EqualTo((7L, 3L, 40148011)));
        });
    }
}
