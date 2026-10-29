using System;
using System.Numerics;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>AI-02: an NPC turns before it casts, and its SkillUse carries the turned facing as (0, 0, facing), as in the GMS captures.</summary>
[TestFixture]
public class NpcCastFacingTest {
    private const float Tolerance = 1e-3f;

    [Test]
    public void NpcCastCarriesItsFacing() {
        Assert.That(SkillRecord.CastRotation(new Vector3(0, 0, 120), default), Is.EqualTo(new Vector3(0, 0, 120)));
    }

    [Test]
    public void PlayerCastKeepsTheRotationItSent() {
        Assert.That(SkillRecord.CastRotation(new Vector3(0, 0, 30), new Vector3(0, 0, 45)), Is.EqualTo(new Vector3(0, 0, 45)));
        Assert.That(SkillRecord.CastRotation(default, new Vector3(0, 0, 45)), Is.EqualTo(new Vector3(0, 0, 45)));
    }

    [Test]
    public void TurnedFacingIsWhatTheCastCarries() {
        var transform = new Transform { RotationAnglesDegrees = new Vector3(0, 0, 30) };
        var toTarget = new Transform { RotationAnglesDegrees = new Vector3(0, 0, 120) };
        transform.LookTo(toTarget.FrontAxis);

        Vector3 rotation = SkillRecord.CastRotation(transform.RotationAnglesDegrees, default);

        Assert.That(MathF.Abs(rotation.X), Is.LessThan(Tolerance));
        Assert.That(MathF.Abs(rotation.Y), Is.LessThan(Tolerance));
        Assert.That(MathF.Abs(MathF.IEEERemainder(rotation.Z - 120, 360)), Is.LessThan(Tolerance));
    }
}
