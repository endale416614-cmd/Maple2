using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

public class FieldBreakable : FieldEntity<BreakableActor> {
    public readonly string EntityId;

    private long nextTick;

    public BreakableState State { get; private set; }
    public long BaseTick { get; private set; }

    private bool visible;

    public bool Visible {
        get => visible;
        set {
            if (!visible && value) {
                BaseTick = Environment.TickCount64;
            }
            visible = value;
            addressed = true;
        }
    }

    // Scripted platforms (MS2BreakableNIF): only map scripts change them, no automatic reset.
    public bool TriggerOnly { get; private set; }

    private bool addressed; // a script (or a hit) has set its state or visibility

    /// <summary>Whether a player entering the field gets this breakable: a scripted platform only once a script has set it (until then the client keeps its own start, as before 1.2.1).</summary>
    public bool InFieldList => !TriggerOnly || addressed;

    public FieldBreakable(FieldManager field, int objectId, string entityId, BreakableActor breakable) : base(field, objectId, breakable) {
        EntityId = entityId;
        visible = breakable.Visible;
        State = breakable.Visible ? BreakableState.Show : BreakableState.Hide;
    }

    /// <summary>Makes this a scripted platform that starts enabled (Show) or not as its xblock says, apart from visibility.</summary>
    public void SetScripted(bool enabled) {
        TriggerOnly = true;
        State = enabled ? BreakableState.Show : BreakableState.Hide;
    }

    /// <summary>When the breakable changes by itself next: never for scripted ones and shown ones, else after its hide or reset time.</summary>
    public static long NextTick(BreakableState state, bool triggerOnly, long now, int hideTime, int resetTime) => triggerOnly ? 0 : state switch {
        BreakableState.Break => now + hideTime,
        BreakableState.Hide => now + resetTime,
        _ => 0,
    };

    public bool UpdateState(BreakableState state) {
        addressed = true; // also when it already is in that state: the script's choice then holds for players entering later
        if (State == state) {
            return false;
        }

        State = state;
        if (TriggerOnly && state == BreakableState.Show) {
            BaseTick = Environment.TickCount64; // the client runs a moving platform's cycle from this tick: enabling starts it
        }
        Field.Broadcast(BreakablePacket.Update(this));

        nextTick = NextTick(State, TriggerOnly, Environment.TickCount64, Value.HideTime, Value.ResetTime);

        return true;
    }

    public override void Update(long tickCount) {
        if (nextTick == 0 || tickCount < nextTick) {
            return;
        }

        if (!Visible) {
            return;
        }

        switch (State) {
            case BreakableState.Show:
                break;
            case BreakableState.Break:
                UpdateState(BreakableState.Hide);
                break;
            case BreakableState.Hide:
                UpdateState(BreakableState.Show);
                break;
            case BreakableState.Unknown5:
                break;
            case BreakableState.Unknown6:
                break;
        }
    }
}
