using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

/// <summary>
/// Client packets that need no answer offline (BT-11, DL-07): read nothing, do nothing.
/// </summary>
public class ResponseClientTickSyncHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.ResponseClientTickSync;

    public override void Handle(GameSession session, IByteReader packet) {
        // The reply to the tick sync at EnterServer; nothing reads it (time sync and heartbeat keep the clocks).
    }
}

public class ServerEnterHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.ServerEnter;

    public override void Handle(GameSession session, IByteReader packet) {
        // The client's language; only the Login server uses it.
    }
}

public class GlobalFactorHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.GlobalFactor;

    public override void Handle(GameSession session, IByteReader packet) {
        // Asks for a factor table no server implements; rates are applied on the server.
    }
}

public class DynamicBanwordHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.DynamicBanword;

    public override void Handle(GameSession session, IByteReader packet) {
        // Asks for server-pushed banned words; offline there are none (BanWordStorage checks names).
    }
}

public class TutorialHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.Tutorial;

    public override void Handle(GameSession session, IByteReader packet) {
        // Sent once early in the tutorial; its payload is unknown and the tutorial plays through without it.
    }
}

public class NexonArenaHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.NexonArena;

    public override void Handle(GameSession session, IByteReader packet) {
        // Asks an online Nexon service (sent after some field actions); nothing to answer offline.
    }
}

public class WinActivateHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.WinActivate;

    public override void Handle(GameSession session, IByteReader packet) {
        // A client platform notice (window activation); nothing to answer.
    }
}