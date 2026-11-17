using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Recast.Toolset.Builder;
using DotRecast.Recast.Toolset.Geom;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.DotRecast;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-01 (BT-252): a chase path to a target on another part of the navmesh ends where the path ends, at the edge of the monster's own
/// island nearest the target, not at the target (the monster walked a straight line through air and walls to it); a monster already at that edge gets no
/// path. A path to a target it can reach, and every path to a point (Move node, patrol, return; authored points, may lie across a gap), still ends exactly at the target.
/// Two floors with a gap and one connected floor, built in the test (units of the navmesh space: 10 x 10 floors), through AgentNavigation.FindPath and PathTo.</summary>
public class PartialPathTest {
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly RcVec3f OnFirst = new(5, 0, 5);
    private static readonly RcVec3f OnFirstToo = new(8, 0, 2);
    private static readonly RcVec3f OnSecond = new(19, 0, 5);

    // Floor 0..10, then a gap of 4 with no floor, then floor 14..24
    private static AgentNavigation Split() => Build(
        [0, 0, 0, 10, 0, 0, 10, 0, 10, 0, 0, 10, 14, 0, 0, 24, 0, 0, 24, 0, 10, 14, 0, 10],
        [0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6]);

    private static AgentNavigation Connected() => Build(
        [0, 0, 0, 24, 0, 0, 24, 0, 10, 0, 0, 10],
        [0, 2, 1, 0, 3, 2]);

    private static AgentNavigation Build(float[] vertices, int[] faces) {
        NavMeshBuildResult result = new SoloNavMeshBuilder().Build(new DemoInputGeomProvider(vertices, faces), DotRecastHelper.NavMeshBuildSettings);
        Assert.That(result.Success, Is.True);
        var crowd = new DtCrowd(new DtCrowdConfig(maxAgentRadius: 0.3f), result.NavMesh, _ => new DtQueryDefaultFilter(
            SampleAreaModifications.SAMPLE_POLYFLAGS_ALL, SampleAreaModifications.SAMPLE_POLYFLAGS_DISABLED, [1f, 10f, 1f, 1f, 2f, 1.5f]));

        var navigation = Bare<Navigation>();
        SetField(navigation, "<Crowd>k__BackingField", crowd);
        var field = Bare<FieldManager>();
        SetField(field, "<Navigation>k__BackingField", navigation);
        var npc = Bare<FieldNpc>();
        SetField(npc, "<Field>k__BackingField", field);
        return new AgentNavigation(npc, new DtCrowdAgent(0), crowd);
    }

    private static float Flat(RcVec3f a, RcVec3f b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    [Test]
    public void ATargetOnAnotherIslandEndsTheWalkAtTheEdgeOfTheOwnIsland() {
        List<RcVec3f>? path = Split().FindPath(OnFirst, OnSecond, true);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        RcVec3f end = path![^1];
        Assert.That(end.X, Is.LessThanOrEqualTo(10.2f), "the walk stays on the first floor");
        Assert.That(end.X, Is.GreaterThan(9f), "and goes to its edge nearest the target");
        Assert.That(Flat(end, OnSecond), Is.GreaterThan(8f), "not to the target across the gap");
        foreach (RcVec3f point in path) {
            Assert.That(point.X, Is.LessThanOrEqualTo(10.2f));
        }
    }

    [Test]
    public void ATargetOnTheSameIslandStillEndsAtTheTarget() {
        List<RcVec3f>? path = Split().FindPath(OnFirst, OnFirstToo, true);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(Flat(path![^1], OnFirstToo), Is.LessThan(0.01f));
    }

    [Test]
    public void AConnectedMeshEndsAtTheTarget() {
        List<RcVec3f>? path = Connected().FindPath(OnFirst, OnSecond, true);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(Flat(path![^1], OnSecond), Is.LessThan(0.01f));
    }

    [Test]
    public void APatrolStylePathStillEndsAtTheTargetAcrossTheGap() {
        List<RcVec3f>? path = Split().FindPath(OnFirst, OnSecond);

        Assert.That(path, Is.Not.Null.And.Not.Empty);
        Assert.That(Flat(path![^1], OnSecond), Is.LessThan(0.01f), "authored patrol points may lie across a gap: the NPC walks the line to them as before");
    }

    [Test]
    public void AMonsterAtTheEdgeOfItsIslandGetsNoPathEveryTime() {
        AgentNavigation navigation = Split();
        RcVec3f edge = navigation.FindPath(OnFirst, OnSecond, true)![^1];

        for (int i = 0; i < 3; i++) {
            Assert.That(navigation.FindPath(edge, OnSecond, true), Is.Null, "like a target off the mesh: no walk starts and ends at once");
        }
    }

    [Test]
    public void PathToPassesTheChaseFlagThrough() {
        AgentNavigation navigation = Split();
        navigation.agent.npos = OnFirst;
        Vector3 goal = DotRecastHelper.FromNavMeshSpace(OnSecond);

        Assert.That(navigation.PathTo(goal), Is.True);
        Assert.That(Flat(navigation.currentPath![^1], OnSecond), Is.LessThan(0.01f), "default: the target");

        Assert.That(navigation.PathTo(goal, true), Is.True);
        Assert.That(navigation.currentPath![^1].X, Is.LessThanOrEqualTo(10.2f), "chase: the edge of its island");

        navigation.agent.npos = navigation.currentPath![^1];
        Assert.That(navigation.PathTo(goal, true), Is.False, "chase from the edge: no path");
        Assert.That(navigation.PathTo(goal), Is.True, "patrol from the edge: not refused, as before");
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
