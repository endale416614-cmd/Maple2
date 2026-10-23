using System;
using System.IO;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// NPC pathing on the Cusp of Life navmesh (02000066), with positions from own report 2026-10-13 01-35-14: hundreds of
/// "Failed to find path" errors, flee targets 21 million units away and NaN flee points.
/// </summary>
public class NpcNavigationTest {
    private static readonly RcVec3f Extents = new(2, 4, 2); // FieldManager.FindNearestPoly search box
    private static readonly RcVec3f Koborc = new(-21, 13.500004f, 45); // spawn 1303, monster 24000205
    private static readonly RcVec3f PlayerOffMesh = new(-29.637596f, 11.315744f, 60.64036f); // the target it chased 476 times

    private DtNavMeshQuery query = null!;
    private IDtQueryFilter filter = null!;

    [SetUp]
    public void SetUp() {
        if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navmeshes", "02000066_bf.navmesh"))) {
            Assert.Ignore("Navmeshes submodule not checked out");
        }

        var navigation = new Navigation("02000066_bf");
        query = navigation.Crowd.GetNavMeshQuery();
        filter = navigation.Crowd.GetFilter(0);
    }

    private long PolyAt(RcVec3f point) {
        query.FindNearestPoly(point, Extents, filter, out long polyRef, out _, out _);
        return polyRef;
    }

    [Test]
    public void OffMeshPoint_DetourReportsSuccessWithNoPolygon() {
        // Why AgentNavigation.TryFindPoly checks the ref: the status alone said "found" and PathTo pathed to the bare point.
        DtStatus status = query.FindNearestPoly(PlayerOffMesh, Extents, filter, out long polyRef, out RcVec3f nearest, out _);

        Assert.That(status.Succeeded(), Is.True);
        Assert.That(polyRef, Is.EqualTo(0));
        Assert.That(nearest, Is.EqualTo(PlayerOffMesh));
        Assert.That(PolyAt(Koborc), Is.Not.EqualTo(0), "the monster itself stands on the navmesh");
    }

    [TestCase(15f)] // runaway till 1500
    [TestCase(38f)] // till 3800, the longest in the AI data
    public void FindPointAway_RunsAwayFromTheTargetOnTheNavMesh(float distance) {
        var player = new RcVec3f(Koborc.X + 1, Koborc.Y, Koborc.Z + 1);

        bool found = AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, player, distance, out RcVec3f point);

        Assert.That(found, Is.True);
        Assert.That(point.IsFinite(), Is.True);
        Assert.That(PolyAt(point), Is.Not.EqualTo(0));
        Assert.That(RcVec3f.Distance(point, player), Is.GreaterThan(RcVec3f.Distance(Koborc, player)));
        Assert.That(RcVec3f.Distance(point, player), Is.LessThanOrEqualTo(distance + 0.01f));
    }

    [Test]
    public void FindPointAway_TargetStraightAboveOrOnTop_NoNaN() {
        // Old code normalized (target - position): zero vector = NaN point; straight above = a point deep under the floor.
        foreach (RcVec3f player in new[] { Koborc, new RcVec3f(Koborc.X, Koborc.Y + 3, Koborc.Z) }) {
            bool found = AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, player, 15f, out RcVec3f point);

            Assert.That(found, Is.True);
            Assert.That(point.IsFinite(), Is.True);
            Assert.That(PolyAt(point), Is.Not.EqualTo(0));
        }
    }

    [Test]
    public void FindPointAway_HugeDistance_StaysOnTheNavMesh() {
        // The runaway node used float.MaxValue; (int) of it is int.MinValue, which put the flee point 21474836 units away.
        float distance = float.MaxValue / 100; // world units to navmesh units
        var player = new RcVec3f(Koborc.X + 1, Koborc.Y, Koborc.Z);

        AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, player, distance, out RcVec3f point);

        Assert.That(point.IsFinite(), Is.True);
        Assert.That(PolyAt(point), Is.Not.EqualTo(0));
        Assert.That(RcVec3f.Distance(point, Koborc), Is.LessThan(200));
    }

    [Test]
    public void FindPointAway_BadInput_False() {
        Assert.That(AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, PlayerOffMesh, float.NaN, out _), Is.False);
        Assert.That(AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, PlayerOffMesh, 0, out _), Is.False);
        var nan = new RcVec3f(float.NaN, float.NaN, float.NaN);
        Assert.That(AgentNavigation.FindPointAway(query, filter, PolyAt(Koborc), Koborc, nan, 15f, out _), Is.False);
    }

    [Test]
    public void FindPullPoint() {
        // Test that pull point returns true, ends on mesh and closer to center when pulling toward a point 1 navmesh unit away
        var target = new RcVec3f(Koborc.X + 1, Koborc.Y, Koborc.Z + 1);
        Assume.That(PolyAt(target), Is.Not.EqualTo(0));
        bool found = AgentNavigation.FindPullPoint(query, filter, PolyAt(Koborc), Koborc, target, out RcVec3f point);

        Assert.That(found, Is.True);
        Assert.That(point.IsFinite(), Is.True);
        Assert.That(PolyAt(point), Is.Not.EqualTo(0));
        Assert.That(RcVec3f.Distance(point, target), Is.LessThan(RcVec3f.Distance(Koborc, target)));

        // Toward the off-mesh point the slide still ends on the mesh
        found = AgentNavigation.FindPullPoint(query, filter, PolyAt(Koborc), Koborc, PlayerOffMesh, out point);
        Assert.That(found, Is.True);
        Assert.That(point.IsFinite(), Is.True);
        Assert.That(PolyAt(point), Is.Not.EqualTo(0));

        // Test that pull point returns false when pulling toward self (already there)
        found = AgentNavigation.FindPullPoint(query, filter, PolyAt(Koborc), Koborc, Koborc, out point);
        Assert.That(found, Is.False);

        // Test that pull point returns false when center is NaN
        var nanCenter = new RcVec3f(float.NaN, float.NaN, float.NaN);
        found = AgentNavigation.FindPullPoint(query, filter, PolyAt(Koborc), Koborc, nanCenter, out point);
        Assert.That(found, Is.False);
    }

    [TestCase(5, 1, true)]
    [TestCase(5, 80, false)]
    [TestCase(5, 99, false)]
    public void CanBePushed(int hitImmuneBreak, int hitImmune, bool expected) {
        Assert.That(Maple2.Server.Game.Model.FieldNpc.CanBePushed(hitImmuneBreak, hitImmune), Is.EqualTo(expected));
    }
}
