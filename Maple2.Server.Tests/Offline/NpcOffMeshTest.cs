using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;
using Maple2.Server.Game.Manager.Field;
using Maple2.Tools.DotRecast;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// NPC moves never keep a position off the navmesh (BT-67), on a flat 10 x 10 navmesh-unit floor (1000 x 1000 game units)
/// built in the test, so no navmesh file is needed.
/// </summary>
public class NpcOffMeshTest {
    private static readonly RcVec3f Centre = new(5, 0, 5);
    private static readonly RcVec3f PastEdge = new(11, 0, 5); // 100 units past the edge: inside the search box
    private static readonly RcVec3f FarPastEdge = new(14, 0, 5); // 400 units past it: no polygon in the box

    private DtNavMeshQuery query = null!;
    private IDtQueryFilter filter = null!;

    [OneTimeSetUp]
    public void BuildFloor() {
        float[] vertices = [0, 0, 0, 10, 0, 0, 10, 0, 10, 0, 0, 10];
        int[] faces = [0, 2, 1, 0, 3, 2];
        NavMeshBuildResult result = new SoloNavMeshBuilder().Build(new DemoInputGeomProvider(vertices, faces), DotRecastHelper.NavMeshBuildSettings);
        Assert.That(result.Success, Is.True);
        query = new DtNavMeshQuery(result.NavMesh);
        filter = new DtQueryDefaultFilter(SampleAreaModifications.SAMPLE_POLYFLAGS_ALL, SampleAreaModifications.SAMPLE_POLYFLAGS_DISABLED, [1f, 10f, 1f, 1f, 2f, 1.5f]); // Navigation's crowd filter
    }

    [Test]
    public void FindOnMesh_SnapsAStepNearTheEdgeOntoTheMesh() {
        Assert.That(AgentNavigation.FindOnMesh(query, filter, Centre, out RcVec3f centre), Is.True);
        Assert.That(RcVec3f.Distance(centre, Centre), Is.LessThan(0.1f));

        Assert.That(AgentNavigation.FindOnMesh(query, filter, PastEdge, out RcVec3f edge), Is.True);
        Assert.That(edge.X, Is.LessThanOrEqualTo(10f));
        Assert.That(AgentNavigation.FindOnMesh(query, filter, edge, out _), Is.True);
    }

    [Test]
    public void FindOnMesh_RejectsAPointWithNoPolygonInReach() {
        Assert.That(AgentNavigation.FindOnMesh(query, filter, FarPastEdge, out _), Is.False);
        Assert.That(AgentNavigation.FindOnMesh(query, filter, new RcVec3f(5, 5, 5), out _), Is.False, "500 units above the floor");
        Assert.That(AgentNavigation.FindOnMesh(query, filter, new RcVec3f(float.NaN, 0, 5), out _), Is.False);
    }

    [Test]
    public void FindLanding_OnTheMeshAtTheEndElseBackAtTheStart() {
        Assert.That(AgentNavigation.FindLanding(query, filter, Centre, new RcVec3f(8, 0, 5), out RcVec3f landing), Is.True);
        Assert.That(RcVec3f.Distance(landing, new RcVec3f(8, 0, 5)), Is.LessThan(0.1f));

        Assert.That(AgentNavigation.FindLanding(query, filter, Centre, FarPastEdge, out landing), Is.True);
        Assert.That(RcVec3f.Distance(landing, Centre), Is.LessThan(0.1f));

        Assert.That(AgentNavigation.FindLanding(query, filter, FarPastEdge, new RcVec3f(5, 5, 5), out _), Is.False);
    }
}
