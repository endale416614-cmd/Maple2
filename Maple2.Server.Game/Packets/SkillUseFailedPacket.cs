using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Game.Packets;

public static class SkillUseFailedPacket {
    private enum Command : byte {
        Fail = 5,
        Unknown = 6,
    }

    public static ByteWriter Fail(SkillRecord record) {
        var pWriter = Packet.Of(SendOp.SkillUseFailed);
        pWriter.Write<Command>(Command.Fail);
        pWriter.WriteLong(record.CastUid);
        pWriter.WriteInt(record.SkillId); // 46 of 46 captured packets: the cast's skill id
        pWriter.WriteByte(6); // 41 of 46 captured packets; 1 in the other 5 (skills 1000001 and 1000002, Port 57949.msb), meaning not established
        pWriter.WriteInt(record.ServerTick);
        pWriter.WriteShort();
        pWriter.WriteInt();

        return pWriter;
    }
}
