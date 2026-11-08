using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Packets;

public static class RevivalPacket {
    public static ByteWriter UpdatePenalty(int objectId, int endTick, int count) {
        var pWriter = Packet.Of(SendOp.RevivalConfirm);
        pWriter.WriteInt(objectId);
        pWriter.WriteInt(endTick);
        pWriter.WriteInt(count);

        return pWriter;
    }

    public static ByteWriter RevivalCount(int count) {
        var pWriter = Packet.Of(SendOp.RevivalCount);
        pWriter.WriteInt(count);

        return pWriter;
    }

    public static ByteWriter Revive(int objectId) {
        var pWriter = Packet.Of(SendOp.Revival);
        pWriter.WriteInt(objectId);
        pWriter.WriteByte();

        return pWriter;
    }

    // source: KMS2_DEV client, scenestategame.cpp OnAutoRevive reads one int, the countdown in milliseconds
    public static ByteWriter AutoRevive(int milliseconds) {
        var pWriter = Packet.Of(SendOp.AutoRevive);
        pWriter.WriteInt(milliseconds);

        return pWriter;
    }

    // source: GMS captures, one int: 0 as a dungeon run's first map is entered, then the count after each safe revival; the
    // KMS2_DEV client (scenestategame.cpp OnDungeonReviveCount) keeps it for the revival dialog's uses left
    public static ByteWriter DungeonReviveCount(int count) {
        var pWriter = Packet.Of(SendOp.DungeonReviveCount);
        pWriter.WriteInt(count);

        return pWriter;
    }

    public static ByteWriter Tombstone(Tombstone tombstone) {
        var pWriter = Packet.Of(SendOp.Tombstone);
        pWriter.WriteClass<Tombstone>(tombstone);

        return pWriter;
    }
}
