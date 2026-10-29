using System.Numerics;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Offline;

/// <summary>AI-01: an AI standby node turns the NPC to its facePos (a map position) or, with faceTarget 1, to its target.</summary>
[TestFixture]
public class StandbyFacingTest {
    private static readonly Vector3 Npc = new(150, -600, 4350);

    [Test]
    public void FacePosWinsOverTheTarget() {
        Assert.That(MovementState.StandbyFacing(Npc, new Vector3(150, 600, 4550), 1, new Vector3(1150, -600, 4350)), Is.EqualTo(new Vector3(0, 1200, 200)));
    }

    [Test]
    public void FaceTargetOneTurnsToTheTarget() {
        Vector3? direction = MovementState.StandbyFacing(Npc, Vector3.Zero, 1, new Vector3(1150, -600, 4450));
        Assert.That(direction, Is.EqualTo(new Vector3(1000, 0, 100)));

        var transform = new Transform { RotationAnglesDegrees = new Vector3(0, 0, 90) };
        transform.LookTo(direction!.Value);
        Assert.That(Vector3.Distance(transform.FrontAxis, new Vector3(1, 0, 0)), Is.LessThan(1e-4f));
    }

    [Test]
    public void OtherFaceTargetValuesKeepTheFacing() {
        foreach (int faceTarget in new[] { 0, 3, 10, 100 }) {
            Assert.That(MovementState.StandbyFacing(Npc, Vector3.Zero, faceTarget, new Vector3(1150, -600, 4450)), Is.Null, $"faceTarget {faceTarget}");
        }
    }

    [Test]
    public void NoTargetKeepsTheFacing() {
        Assert.That(MovementState.StandbyFacing(Npc, Vector3.Zero, 1, null), Is.Null);
    }
}
