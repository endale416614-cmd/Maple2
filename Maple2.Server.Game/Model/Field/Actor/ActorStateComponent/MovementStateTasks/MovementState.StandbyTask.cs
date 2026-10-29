using Maple2.Server.Game.Model.Enum;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    public class NpcStandbyTask : NpcTask {
        private readonly MovementState movement;
        public IActor? Target { get; init; }
        public int FaceTarget { get; init; }
        public Vector3 FacePos { get; init; }
        private string Sequence { get; init; }
        private bool IsIdle { get; init; }
        public override bool CancelOnInterrupt => true;

        public NpcStandbyTask(TaskState taskState, MovementState movement, string sequence, NpcTaskPriority priority, bool isIdle) : base(taskState, priority) {
            this.movement = movement;
            Sequence = sequence;
            IsIdle = isIdle;
        }

        protected override void TaskResumed() {
            movement.Standby(this, IsIdle, Sequence);
        }

        /// <summary>Turns the NPC at once to the standby's facePos or target; the AI calls it on every update while it waits on the standby, as the original keeps a waiting NPC facing a moving target.</summary>
        public void Face() {
            if (Status != NpcTaskStatus.Running) {
                return; // queued behind a pull or another cast: TaskResumed turns it
            }
            // TODO: faces the target the standby started with, not a later one; follow BattleState.Target if an NPC is seen facing its old target after a target change mid-wait.
            if (StandbyFacing(movement.actor.Position, FacePos, FaceTarget, Target?.Position) is Vector3 direction) {
                movement.actor.Transform.LookTo(direction);
            }
        }
    }

    /// <summary>A standby node's facing: toward facePos (a map position) when set, else toward the target when faceTarget is 1 (as skill nodes read it), else none; null keeps the facing.</summary>
    public static Vector3? StandbyFacing(Vector3 position, Vector3 facePos, int faceTarget, Vector3? target) => facePos != Vector3.Zero ? facePos - position : faceTarget == 1 ? target - position : null;

    private void Standby(NpcStandbyTask task, bool isIdle, string sequence) {
        Idle(sequence);
        dontIdleOnStateEnd = isIdle;
        task.Face();
    }
}
