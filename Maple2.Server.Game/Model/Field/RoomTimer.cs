using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Model;

public class RoomTimer : IUpdatable {
    private readonly FieldManager field;
    public int StartTick { get; private set; }
    public readonly RoomTimerType Type;
    public int Duration;
    private bool started;
    private bool returned;

    private readonly ILogger logger = Log.Logger.ForContext<RoomTimer>();

    public RoomTimer(FieldManager field, RoomTimerType type, int duration) {
        this.field = field;
        Type = type;
        Duration = duration;
        // Counts from creation until the first player arrives, so an unentered room still expires.
        StartTick = Environment.TickCount64.Truncate32();
    }

    public void Modify(int tick) {
        int originalDuration = Duration;
        Duration = Math.Max(0, Duration + tick);
        int delta = Duration - originalDuration;
        field.Broadcast(RoomTimerPacket.Modify(this, delta));
    }

    public void Update(long tickCount) {
        if (!started) {
            StartTick = tickCount.Truncate32();
            field.Broadcast(RoomTimerPacket.Start(this));
            started = true;
        }

        if (returned || !Expired(tickCount)) {
            return;
        }
        returned = true;

        foreach ((int objectId, FieldPlayer player) in field.Players) {
            int returnMapId = ReturnMapId(player.Value.Character.ReturnMaps, field.MapId);
            player.Session.Send(player.Session.PrepareField(returnMapId, returnMapId)
                ? FieldEnterPacket.Request(player)
                : FieldEnterPacket.Error(MigrationError.s_move_err_default));
        }

        // The dispose loop removes the empty, expired room; disposing here would join this field's own thread.
        logger.Debug("Room timer expired, players sent back from field {FieldId}", field.MapId);
    }

    public bool Expired(long tickCount) => tickCount.TicksSince(StartTick) > Duration;

    /// <summary>Where a timed room sends its players: the return map below the room's own entry (14 pocket realms push themselves).</summary>
    public static int ReturnMapId(LimitedStack<int> returnMaps, int roomMapId) {
        if (returnMaps.Count > 1 && returnMaps.Peek() == roomMapId) {
            returnMaps.Pop();
        }
        return returnMaps.Peek();
    }

    /// <summary>Where a room's leave portal sends a player: the map below the room, else Lith Harbor when the room is all that is left on the stack.</summary>
    public static int LeaveMapId(LimitedStack<int> returnMaps, int roomMapId) {
        int below = ReturnMapId(returnMaps, roomMapId);
        return below != roomMapId ? below : Constant.DefaultReturnMapId;
    }

    /// <summary>Where a character saved inside a pocket realm logs in: the map below the room, else Lith Harbor (a room does not outlive a logout).</summary>
    public static int LoginMapId(int savedMapId, bool pocketRealm, LimitedStack<int> returnMaps) {
        return pocketRealm ? LeaveMapId(returnMaps, savedMapId) : savedMapId;
    }
}
