using System.Numerics;
using System.Text.Json;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>AI-07: an NPC skill motion moves the caster along its front axis turned by the motion's moveangle.</summary>
[TestFixture]
public class CastMoveAngleTest {
    private const float Tolerance = 1e-4f;

    private static Transform Turned(float z) {
        return new Transform { RotationAnglesDegrees = new Vector3(0, 0, z) };
    }

    private static void AssertNear(Vector3 actual, Vector3 expected) {
        Assert.That((actual - expected).Length(), Is.LessThan(Tolerance), $"{actual} is not {expected}");
    }

    [Test]
    public void AngleZeroIsTheFrontAxis() {
        Transform transform = Turned(30);

        AssertNear(MovementState.CastMoveDirection(transform, 0), transform.FrontAxis);
    }

    [Test]
    public void Angle180IsStraightBack() {
        Transform transform = Turned(30);

        AssertNear(MovementState.CastMoveDirection(transform, 180), -transform.FrontAxis);
    }

    [Test]
    public void AngleTurnsTheWayRotationZDoes() {
        AssertNear(MovementState.CastMoveDirection(Turned(30), 90), Turned(120).FrontAxis);
    }

    [Test]
    public void MoveAngleMissingFromOlderDataIsZero() {
        const string json = """{"SequenceName":"Attack_01_A","SequenceSpeed":1,"MoveDistance":100,"FaceTarget":true}""";

        SkillMetadataMotionProperty? property = JsonSerializer.Deserialize<SkillMetadataMotionProperty>(json);

        Assert.That(property, Is.Not.Null);
        Assert.That(property!.MoveAngle, Is.EqualTo(0f));
        Assert.That(property.MoveDistance, Is.EqualTo(100f));
    }
}
