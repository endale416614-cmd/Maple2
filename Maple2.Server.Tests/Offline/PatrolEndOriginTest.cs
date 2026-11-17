using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-12 (BT-269): when a script's patrol ends (Twinkling Path 2000331, script seeker01: move_npc of the chasers) the last waypoint becomes
/// the NPC's home once the NPC has got there, so after a battle it walks back to the bridge and not to its spawn 2,700 units away. A walk that fails,
/// is cancelled or is never made leaves the home where it was; a looping patrol never changes it. Through the real NextWaypoint, with an air waypoint
/// (it needs no navmesh) and the walk task's status set the way NpcTask.Finish sets it.</summary>
public class PatrolEndOriginTest {
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Vector3 Spawn = new(-2375, -125, 6000);
    private static readonly Vector3 Bridge = new(-2600, 1045, 1200);

    [Test]
    public void AnEndedPatrolMakesItsLastWaypointTheHomeOnceTheWalkCompleted() {
        FieldNpc npc = Rig(isLoop: false, withFlySequence: true);

        NpcTask? task = NextWaypoint(npc);

        Assert.That(task, Is.Not.Null);
        Assert.That(npc.Patrol, Is.Null, "the patrol is over");
        Assert.That(npc.Origin, Is.EqualTo(Spawn), "not yet: the walk was only made");
        ApplyPatrolEnd(npc);
        Assert.That(npc.Origin, Is.EqualTo(Spawn), "not while it is still walking");

        SetStatus(task!, NpcTaskStatus.Complete);
        ApplyPatrolEnd(npc);
        Assert.That(npc.Origin, Is.EqualTo(Bridge), "arrived: the walk back and the idle walk are around the last waypoint");
    }

    [Test]
    public void ACancelledWalkKeepsTheOldHome() {
        FieldNpc npc = Rig(isLoop: false, withFlySequence: true);
        NpcTask task = NextWaypoint(npc)!;

        SetStatus(task, NpcTaskStatus.Cancelled); // a battle interrupted it, or the walk found no way
        ApplyPatrolEnd(npc);

        Assert.That(npc.Origin, Is.EqualTo(Spawn));
        SetStatus(task, NpcTaskStatus.Complete);
        ApplyPatrolEnd(npc);
        Assert.That(npc.Origin, Is.EqualTo(Spawn), "the cancelled walk is forgotten");
    }

    [Test]
    public void ANpcThatCannotWalkThereKeepsTheOldHome() {
        FieldNpc npc = Rig(isLoop: false, withFlySequence: false); // the model has no fly sequence: no walk is made

        Assert.That(NextWaypoint(npc), Is.Null);
        ApplyPatrolEnd(npc);

        Assert.That(npc.Patrol, Is.Null);
        Assert.That(npc.Origin, Is.EqualTo(Spawn));
    }

    [Test]
    public void ALoopingPatrolNeverChangesTheHome() {
        FieldNpc npc = Rig(isLoop: true, withFlySequence: true);
        NpcTask task = NextWaypoint(npc)!;

        SetStatus(task, NpcTaskStatus.Complete);
        ApplyPatrolEnd(npc);

        Assert.That(npc.Patrol, Is.Not.Null);
        Assert.That(npc.Origin, Is.EqualTo(Spawn));
    }

    /// <summary>A bare NPC at its spawn with a patrol of one air waypoint: what NextWaypoint reads, and a task queue.</summary>
    private static FieldNpc Rig(bool isLoop, bool withFlySequence) {
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long>(), [], [], [], []);
        var metadata = new NpcMetadata(24000404, "Chomp Blossom", "", null!, stat, null!, null!, null!, null!, null!, null!, null!, null, null!);
        var last = new MS2WayPoint("2", true, Bridge, Vector3.Zero, "", "", 0, true);
        var patrol = new MS2PatrolData("uuid", "patrol", true, 100, isLoop, [last]);

        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        GC.SuppressFinalize(npc);
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "Logger", Serilog.Core.Logger.None);
        SetField(npc, "TaskState", new TaskState(npc));
        var movement = (MovementState) RuntimeHelpers.GetUninitializedObject(typeof(MovementState));
        GC.SuppressFinalize(movement);
        SetField(movement, "actor", npc);
        SetField(npc, "MovementState", movement);
        SetField(npc, "FlySequence", withFlySequence ? new AnimationSequenceMetadata("Fly_A", 1, 1f, []) : null);
        typeof(FieldNpc).GetProperty(nameof(FieldNpc.Patrol), Any)!.SetValue(npc, patrol);
        npc.Origin = Spawn;
        return npc;
    }

    private static NpcTask? NextWaypoint(FieldNpc npc) => (NpcTask?) typeof(FieldNpc).GetMethod("NextWaypoint", Any)!.Invoke(npc, null);

    private static void ApplyPatrolEnd(FieldNpc npc) => typeof(FieldNpc).GetMethod("ApplyPatrolEnd", Any)!.Invoke(npc, null);

    private static void SetStatus(NpcTask task, NpcTaskStatus status) => SetField(task, "<Status>k__BackingField", status);

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
}
