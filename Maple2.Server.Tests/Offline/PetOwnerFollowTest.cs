using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Offline;

/// <summary>An owned pet follows its owner by PetMetadata.Distance: nothing up to Trace, a walk up to Warp, a warp beyond.</summary>
public class PetOwnerFollowTest {
    private static readonly PetMetadataDistance Basic = new(1200, 200, 0); // 29 pets
    private static readonly PetMetadataDistance Combat = new(3000, 300, 5000); // 72 combat pets

    private static FieldPet.FollowAction Step(float x, PetMetadataDistance distance, float z = 0) =>
        FieldPet.FollowStep(Vector3.Zero, new Vector3(x, 0, z), distance);

    [Test]
    public void Basic_TraceAndWarpBounds() {
        Assert.That(Step(199, Basic), Is.EqualTo(FieldPet.FollowAction.None));
        Assert.That(Step(200, Basic), Is.EqualTo(FieldPet.FollowAction.None), "Trace itself is still close enough");
        Assert.That(Step(201, Basic), Is.EqualTo(FieldPet.FollowAction.Trace));
        Assert.That(Step(1200, Basic), Is.EqualTo(FieldPet.FollowAction.Trace), "Warp itself still walks");
        Assert.That(Step(1201, Basic), Is.EqualTo(FieldPet.FollowAction.Warp));
    }

    [Test]
    public void Combat_TraceAndWarpBounds() {
        Assert.That(Step(2999, Combat), Is.EqualTo(FieldPet.FollowAction.Trace));
        Assert.That(Step(3001, Combat), Is.EqualTo(FieldPet.FollowAction.Warp));
    }

    [Test]
    public void NoWarpDistance_NeverWarps() {
        Assert.That(Step(5000, new PetMetadataDistance(0, 200, 0)), Is.EqualTo(FieldPet.FollowAction.Trace));
    }

    [Test]
    public void VerticalOffsetCounts() {
        Assert.That(Step(0, Basic, z: 250), Is.EqualTo(FieldPet.FollowAction.Trace));
    }

    [Test]
    public void SamePosition_None() {
        var spot = new Vector3(10, 20, 30);
        Assert.That(FieldPet.FollowStep(spot, spot, Basic), Is.EqualTo(FieldPet.FollowAction.None));
    }
}
