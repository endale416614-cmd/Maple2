using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class ResponseHeartbeatHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.ResponseHeartbeat;

    public override void Handle(GameSession session, IByteReader packet) {
        int serverTick = packet.ReadInt();
        int clientTick = packet.ReadInt();

        int latency = Environment.TickCount - serverTick;
        if (latency > Constant.MaxAllowedLatency) {
            // HB-01 (BT-128): a slow login, map change or map load delays the reply; a closed client ends its session at the socket.
            Logger.Information("Heartbeat reply from {Session} handled {Latency} ms after the request", session, latency);
            return;
        }

        if (serverTick == 0 || clientTick == 0) {
            return;
        }

        if (session is { ClientTick: 0, ServerTick: 0 }) {
            session.ClientTick = clientTick;
            session.ServerTick = serverTick;
            return;
        }

        int serverDelta = serverTick - session.ServerTick;
        int clientDelta = clientTick - session.ClientTick;

        session.ClientTick = clientTick;
        session.ServerTick = serverTick;
    }
}
