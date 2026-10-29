using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

public class FieldInteract : FieldEntity<InteractObjectMetadata> {
    public readonly string EntityId;

    private long nextTick;
    private long reactTick;

    public IInteractObject Object { get; init; }
    public InteractType Type => Value.Type;

    private int reactLimit;
    public InteractState State { get; private set; }
    /// <summary>
    /// Used since it was last made reactable. Used objects stay in Normal (triggers mostly wait for 0, doors only
    /// open in Normal), but count as Hidden for the scripts that wait for 2 after use.
    /// </summary>
    public bool Reacted { get; private set; }
    public int SpawnId { get; init; }

    public FieldInteract(FieldManager field, int objectId, string entityId, InteractObjectMetadata value, IInteractObject interactObject) : base(field, objectId, value) {
        EntityId = entityId;
        State = InteractState.Normal;
        nextTick = Environment.TickCount64;
        reactLimit = Value.ReactCount > 0 ? Value.ReactCount : int.MaxValue;
        Object = interactObject;
    }

    public bool React() {
        if (State != InteractState.Reactable) {
            return false;
        }

        reactTick = Environment.TickCount64;
        if (SpawnId > 0) {
            // Spawned chests open once, then vanish after the hide time so their spawn point can refill.
            SetState(InteractState.Normal);
            nextTick = 0;
            Field.RemoveInteract(Object, TimeSpan.FromMilliseconds(Value.Time.Hide));
            return true;
        }

        reactLimit--;
        Reacted = true;
        if (reactLimit > 0) {
            SetState(InteractState.Normal);
            nextTick = reactTick + Value.Time.Reset;
        } else {
            if (Value.Time.Hide > 0) {
                SetState(InteractState.Normal);
                nextTick = reactTick + Value.Time.Hide;
            } else {
                // No hide time: stay used up in Normal. Triggers wait for state 0, and doors only drop their
                // collision in Normal (Abandoned Apartment 52000071: Hidden left the true door blocking its portal).
                SetState(InteractState.Normal);
                nextTick = 0;
            }
        }

        return true;
    }

    public void SetState(InteractState state) {
        // A set state wins over a pending timer: the start-up promotion made objects a script parks in 0 reactable (BT-71).
        nextTick = 0;
        if (state == InteractState.Reactable) {
            Reacted = false;
        }
        if (State == state) {
            return;
        }

        State = state;
        Field.Broadcast(InteractObjectPacket.Update(this));
    }

    public override void Update(long tickCount) {
        if (Object is InteractBillBoardObject billboard && DateTime.Now.ToEpochSeconds() > billboard.ExpirationTime) {
            SetState(InteractState.Hidden);
            Field.RemoveInteract(Object);
            return;
        }

        if (nextTick == 0 || tickCount < nextTick) {
            return;
        }

        // Checks if we should reset (resetTime > 0)
        if (State == InteractState.Normal && Value.Time.Reset > 0) {
            SetState(reactLimit > 0 ? InteractState.Reactable : InteractState.Hidden);
        }

        nextTick = 0;
    }
}
