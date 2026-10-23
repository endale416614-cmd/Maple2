using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

public class FieldLiftable : FieldEntity<Liftable> {
    public readonly string EntityId;

    public int Count;
    public LiftableState State;
    public long FinishTick;

    public long RespawnTick { get; private set; }

    public FieldLiftable(FieldManager field, int objectId, string entityId, Liftable value) : base(field, objectId, value) {
        EntityId = entityId;
        Count = Value.ItemStackCount;
    }

    /// <summary>When a put-down liftable goes away: on a target box after its FinishTime (delivered), anywhere else after its
    /// ItemLifetime, where 0 means it stays until picked up again (52000004's pizza must be put down to break a wall).
    /// Never 0, which marks the map's own liftables.</summary>
    public static long FinishTickFor(long now, Liftable liftable, bool onTargetBox) {
        int lifetime = onTargetBox ? liftable.FinishTime : liftable.ItemLifetime;
        return lifetime > 0 || onTargetBox ? now + lifetime : long.MaxValue;
    }

    public LiftableCube? Pickup() {
        if (Count <= 0 || State != LiftableState.Default) {
            return null;
        }

        Count--;
        // Only respawn if we have a regen time
        if (RespawnTick == 0 && Value.RegenCheckTime > 0) {
            RespawnTick = Field.FieldTick + Value.RegenCheckTime;
        }

        if (Count > 0) {
            Field.Broadcast(LiftablePacket.Update(this));
        } else if (FinishTick > 0) {
            // This is a temp liftable, so we need to remove it
            Field.RemoveLiftable(EntityId);
        } else {
            State = LiftableState.Removed;
            Field.Broadcast(LiftablePacket.Update(this));

            Field.Broadcast(CubePacket.RemoveCube(ObjectId, Position));
        }

        return new LiftableCube(Value);
    }

    public override void Update(long tickCount) {
        switch (State) {
            case LiftableState.Removed:
                if (RespawnTick > tickCount) {
                    State = LiftableState.Respawning;
                }
                break;
            case LiftableState.Respawning:
                if (tickCount >= RespawnTick) {
                    State = LiftableState.Default;
                    Count = Value.ItemStackCount;
                    RespawnTick = 0;
                    Field.Broadcast(LiftablePacket.Update(this));
                }
                break;
            case LiftableState.Default:
                if (FinishTick != 0 && tickCount > FinishTick) {
                    Field.RemoveLiftable(EntityId);
                    return;
                }
                break;
            case LiftableState.Disabled:
                return;
        }
    }
}
