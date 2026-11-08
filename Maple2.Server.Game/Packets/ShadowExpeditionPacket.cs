using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;

namespace Maple2.Server.Game.Packets;

/// <summary>The Shadow Expedition gauge (SE-01) as the GMS captures of 52100031 and 52100041 send it: Open, then an Update with the new
/// total after each kill that scores, then Close.</summary>
public static class ShadowExpeditionPacket {
    private enum Command : byte {
        Open = 0,
        Update = 1,
        Close = 2,
    }

    public static ByteWriter Open(string title, int maxPoints) {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.Write<Command>(Command.Open);
        pWriter.WriteBool(true); // 1 in both captures
        pWriter.WriteUnicodeString(title);
        pWriter.WriteInt(0); // 0 in both captures
        pWriter.WriteInt(maxPoints);

        return pWriter;
    }

    public static ByteWriter Update(int points) {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.Write<Command>(Command.Update);
        pWriter.WriteInt(points);

        return pWriter;
    }

    public static ByteWriter Close() {
        var pWriter = Packet.Of(SendOp.ShadowExpedition);
        pWriter.Write<Command>(Command.Close);

        return pWriter;
    }
}
