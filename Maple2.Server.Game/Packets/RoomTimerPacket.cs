using Maple2.Model.Enum;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Packets;

public static class RoomTimerPacket {
    private enum Command : byte {
        Start = 0,
        Modify = 1,
        Reset = 2,
        Close = 4,
        Stop = 5,
    }

    public static ByteWriter Start(RoomTimer timer) => Start(timer.Type, timer.StartTick, timer.Duration);

    // As captured: Start to a player entering a timed room (Temple of Time, 900000), Reset when dungeon_reset_time starts the
    // timer again (Terminus of Time, 720000).
    public static ByteWriter Start(RoomTimerType type, int startTick, int duration, bool reset = false) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.Write<Command>(reset ? Command.Reset : Command.Start);
        pWriter.Write<RoomTimerType>(type);
        pWriter.WriteInt(startTick);
        pWriter.WriteInt(duration);
        pWriter.WriteInt();

        return pWriter;
    }

    public static ByteWriter Modify(RoomTimer timer, int delta) => Modify(timer.StartTick, timer.Duration, delta);

    // As captured (Ishura's extendRoomTime at her death, +60000): the timer's start tick, its new length, 2, the change.
    // TODO: FT-13a: 2 is what an NPC's extendRoomTime sends; whether ModifyRoomTime's IsShowEffect sends another value is not captured, upgrade when a capture shows one.
    public static ByteWriter Modify(int startTick, int duration, int delta) {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.Write<Command>(Command.Modify);
        pWriter.WriteInt(startTick);
        pWriter.WriteInt(duration);
        pWriter.WriteInt(2);
        pWriter.WriteInt(delta);

        return pWriter;
    }

    // As captured: dungeon_close_timer (Nairin's Combat Simulator) and dungeon_stop_timer (Terminus of Time) send the command alone.
    public static ByteWriter Close() {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.Write<Command>(Command.Close);

        return pWriter;
    }

    public static ByteWriter Stop() {
        var pWriter = Packet.Of(SendOp.RoomTimer);
        pWriter.Write<Command>(Command.Stop);

        return pWriter;
    }
}
