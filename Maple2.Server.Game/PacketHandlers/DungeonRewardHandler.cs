using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRewardHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.DungeonReward;

    public override void Handle(GameSession session, IByteReader packet) {
        // Sent 2-5 times while a dungeon's results window is up; the rewards are already granted. Its meaning is unknown:
        // keep the bytes for a capture (BT-11).
        Logger.Information("DungeonReward {Bytes}", Convert.ToHexString(packet.ReadBytes(packet.Available)));
    }
}