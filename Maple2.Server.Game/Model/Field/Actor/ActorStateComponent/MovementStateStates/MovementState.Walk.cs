using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Enum;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    private enum WalkType {
        None,
        Direction,
        MoveTo,
        ToTarget,
        FromTarget,
        Jump,
    }

    // Shortest airtime, so a jump to (almost) the same spot still shows a hop.
    private const float MinJumpSeconds = 0.3f;
    private Vector3 jumpStart;
    private Vector3 jumpEnd;
    private float jumpPeak;
    private float jumpDuration;
    private float jumpElapsed;

    private Vector3 walkDirection;
    private Vector3 walkTargetPosition;
    private float walkTargetDistance;
    private WalkType walkType = WalkType.None;
    private (Vector3 start, Vector3 end) walkSegment;
    private bool walkSegmentSet;
    private bool walkLookWhenDone = false;
    private AnimationSequenceMetadata? walkSequence = null;
    private float walkSpeed;
    private NpcTask? walkTask = null;
    private bool isFlying;

    // MovementSpeed is a percent of the data's walk and run speed, and a slow or haste also scales an explicit speed (BT-62; the captures show walk x (1 + rate)).
    public static float WalkSpeedRate(float speed, Stat moveSpeed) => (speed == 0 ? 1 : speed) * moveSpeed.Current / 100f;

    private void UpdateMoveSpeed(float speed) {
        speedOverride = speed;
        Speed = WalkSpeedRate(speed, actor.Stats.Values[BasicAttribute.MovementSpeed]) * baseSpeed;
    }

    private void StartWalking(string sequence, NpcTask task) {
        sequence = sequence == "" ? "Run_A" : sequence;
        walkSegmentSet = false;
        walkSpeed = Speed;

        emoteActionTask?.Cancel();

        bool isWalking = sequence.StartsWith("Walk_");

        baseSpeed = isWalking ? actor.Value.Metadata.Action.WalkSpeed : actor.Value.Metadata.Action.RunSpeed;

        if (actor.Animation.PlayingSequence?.Name == sequence || actor.Animation.TryPlaySequence(sequence, aniSpeed * Speed, AnimationType.Misc)) {
            stateSequence = actor.Animation.PlayingSequence;
            walkSequence = stateSequence;
            walkTask = task;

            SetState(ActorState.Walk);
        } else {
            task.Cancel();

            Idle();
        }
    }

    public bool IsMovingToTarget() {
        return State == ActorState.Walk && walkType switch {
            WalkType.MoveTo => true,
            WalkType.FromTarget => true,
            WalkType.ToTarget => true,
            _ => false,
        };
    }

    private void StateWalkDirectionUpdate(long tickCount, long tickDelta, float delta) {
        Vector3 newPosition = actor.Position + delta * Speed * walkDirection;

        actor.Navigation!.UpdatePosition(newPosition);

        Velocity = Speed * walkDirection;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);
        UpdateDebugMarker(actor.Navigation.GetAgentPosition(), debugAgent, tickCount);
    }

    private void JumpTo(NpcTask task, Vector3 position, float speed, float heightMultiplier, string sequence) {
        if (!CanTransitionToState(ActorState.Walk)) {
            task.Cancel();

            return;
        }

        emoteActionTask?.Cancel();

        // Speed is horizontal units per second; the arc peaks HeightMultiplier * distance / 2 above the
        // start-end line (0.5 = the 45 degree throw arc).
        Vector2 offset = new Vector2(position.X - actor.Position.X, position.Y - actor.Position.Y);
        float distance = offset.Length();

        jumpStart = actor.Position;
        jumpEnd = position;
        jumpDuration = Math.Max(distance / Math.Max(speed, 1f), MinJumpSeconds);
        jumpPeak = heightMultiplier * distance * 0.5f;
        jumpElapsed = 0;

        if (distance > 0) {
            actor.Transform.LookTo(Vector3.Normalize(new Vector3(offset, 0)));
        }

        walkType = WalkType.Jump;
        walkTask = task;

        actor.AppendDebugMessage($"> Jumping to position\n");

        if (actor.Animation.TryPlaySequence(sequence, aniSpeed, AnimationType.Misc)
            || actor.Animation.TryPlaySequence("Jump_A", aniSpeed, AnimationType.Misc)) {
            stateSequence = actor.Animation.PlayingSequence;
            walkSequence = stateSequence;
        }

        SetState(ActorState.Walk);
    }

    private void StateJumpUpdate(long tickCount, long tickDelta) {
        float delta = (float) tickDelta / 1000;
        jumpElapsed += delta;

        float progress = Math.Min(jumpElapsed / jumpDuration, 1f);
        Vector3 position = Vector3.Lerp(jumpStart, jumpEnd, progress);
        position.Z += 4 * jumpPeak * progress * (1 - progress);

        Velocity = delta > 0 ? (position - actor.Position) / delta : Vector3.Zero;
        actor.Position = position;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);

        if (progress >= 1) {
            Land();
            walkTask?.Completed();
        }
    }

    private void Land() {
        Velocity = Vector3.Zero;
        // Puts the navmesh agent under the NPC again, on the landing spot or back where it took off (BT-67). shortcut: with
        // neither on the navmesh (no navmesh, or it took off outside it after an off-mesh teleport or a flight) it stays
        // where it landed, until the teleport node gets its own navmesh rule.
        if (actor.Navigation?.Land(jumpStart, jumpEnd) != true) {
            actor.Position = jumpEnd;
        }
    }

    /// <summary>Slides the NPC to <paramref name="position"/> (on the navmesh) over <paramref name="seconds"/>: a skill's pull.</summary>
    private void PullTo(NpcTask task, Vector3 position, float seconds) {
        emoteActionTask?.Cancel();
        jumpStart = actor.Position;
        jumpEnd = position;
        jumpPeak = 0;
        jumpDuration = Math.Max(seconds, 0.05f); // Gravity Rune pulls in 0.01 s: keep a few ticks
        jumpElapsed = 0;
        walkType = WalkType.Jump;
        walkTask = task;
        SetState(ActorState.Walk); // the walk update runs the slide; the current animation keeps playing
    }

    private void StateWalkUpdate(long tickCount, long tickDelta) {
        if (walkType == WalkType.Jump) {
            StateJumpUpdate(tickCount, tickDelta);
            return;
        }

        if (actor.Navigation is null) {
            return;
        }

        UpdateMoveSpeed(speedOverride);

        float delta = (float) tickDelta / 1000;
        if (walkType == WalkType.Direction) {
            StateWalkDirectionUpdate(tickCount, tickDelta, delta);
            return;
        }

        // --- FLYING ADVANCE LOGIC ---
        // If isFlying, use direct advance
        if (walkSequence != null && isFlying) {
            Vector3 target = walkTargetPosition;
            (Vector3 newPos, bool reachedFlying) = actor.Navigation.FlyAdvance(actor.Position, target, Speed, delta);

            Velocity = (newPos - actor.Position) / delta;
            actor.Position = newPos;

            if (walkLookWhenDone && (target - actor.Position).LengthSquared() > 0) {
                actor.Transform.LookTo(Vector3.Normalize(target - actor.Position));
            }

            if (reachedFlying) {
                Velocity = Vector3.Zero;
                walkTask?.Completed();
            }
            return;
        }

        Vector3 offset = walkSegmentSet ? walkSegment.end - actor.Position : new Vector3(0, 0, 0);
        float distanceSquared = offset.LengthSquared();
        float tickDistance = delta * Speed;

        if (!walkSegmentSet || distanceSquared < tickDistance * tickDistance) {
            if (walkSegmentSet) {
                actor.Position = walkSegment.end;

                tickDistance -= (float) Math.Sqrt(distanceSquared);
            }

            walkSegment = actor.Navigation.Advance(TimeSpan.FromSeconds(0.15), Speed, out walkSegmentSet);

            offset = walkSegmentSet ? walkSegment.end - actor.Position : new Vector3(0, 0, 0);
            distanceSquared = offset.LengthSquared();

            if (!walkSegmentSet || distanceSquared == 0) {
                Vector3 walkTargetOffset = walkTargetPosition - actor.Position;
                float walkTargetDistance = walkTargetOffset.LengthSquared();

                if (walkLookWhenDone && walkTargetDistance > 0) {
                    actor.Transform.LookTo(Vector3.Normalize(walkTargetOffset));
                }

                walkTask?.Cancel();

                return;
            }

            float distance = (float) Math.Sqrt(distanceSquared);

            walkDirection = (1 / distance) * offset;
            tickDistance = Math.Min(tickDistance, distance);

            actor.Transform.LookTo(walkDirection);
        }

        Velocity = Speed * walkDirection;
        actor.Position += tickDistance * walkDirection;

        Vector3 targetOffset = walkTargetPosition - actor.Position;
        float targetDistance = targetOffset.LengthSquared();
        float travelDistance = Speed * delta;

        UpdateDebugMarker(actor.Position, debugNpc, tickCount);
        UpdateDebugMarker(walkTargetPosition, debugTarget, tickCount);
        UpdateDebugMarker(actor.Navigation.GetAgentPosition(), debugAgent, tickCount);

        bool reached;

        if (walkType == WalkType.MoveTo) {
            reached = targetDistance < travelDistance * travelDistance;
        } else if (walkType == WalkType.ToTarget) {
            reached = targetDistance < walkTargetDistance * walkTargetDistance;
        } else {
            reached = targetDistance >= walkTargetDistance * walkTargetDistance;
        }

        if (reached) {
            Velocity = new Vector3(0, 0, 0);

            if (walkLookWhenDone) {
                actor.Transform.LookTo(Vector3.Normalize(targetOffset));
            }

            walkTask?.Completed();
        }
    }

    public void StateWalkEvent(string keyName) {
        switch (keyName) {
            case "end":
                if (State == ActorState.Walk) {
                    if (actor.Animation.TryPlaySequence(stateSequence!.Name, aniSpeed * Speed, AnimationType.Misc)) {
                        stateSequence = actor.Animation.PlayingSequence;
                    }

                    return;
                }
                walkTask?.Cancel();

                break;
            default:
                break;
        }
    }
}
