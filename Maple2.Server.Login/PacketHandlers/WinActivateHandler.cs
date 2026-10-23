using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Login.Session;

namespace Maple2.Server.Login.PacketHandlers;

public class WinActivateHandler : PacketHandler<LoginSession> {
    public override RecvOp OpCode => RecvOp.WinActivate;

    public override void Handle(LoginSession session, IByteReader packet) {
        // A client platform notice (window activation); nothing to answer.
    }
}