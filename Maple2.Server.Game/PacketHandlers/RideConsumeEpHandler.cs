using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class RideConsumeEpHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RideConsumeEp;

    /// <summary>The EP one mount-sprint step costs: the ride's runXConsumeEp, 0 when not riding.</summary>
    public static int Cost(RideMetadata? ride) => (int) (ride?.Basic.RunXStamina ?? 0);

    public override void Handle(GameSession session, IByteReader packet) {
        // The mount sprint's EP (runXConsumeEp); the client stops sprinting when EP runs out.
        session.Player.ConsumeStamina(Cost(session.Ride.Ride?.Metadata));
    }
}
