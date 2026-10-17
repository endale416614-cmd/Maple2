using System.Collections.Immutable;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Serilog;

namespace Maple2.Server.Core.Network;

public class PacketRouter<T> where T : Session {
    private readonly ILogger logger = Log.Logger.ForContext<T>();
    private readonly ImmutableDictionary<RecvOp, PacketHandler<T>> handlers;

    public PacketRouter(IEnumerable<PacketHandler<T>> packetHandlers) {
        var builder = ImmutableDictionary.CreateBuilder<RecvOp, PacketHandler<T>>();
        foreach (PacketHandler<T> packetHandler in packetHandlers.OrderBy(handler => handler.OpCode)) {
            Register(builder, packetHandler);
        }
        handlers = builder.ToImmutable();
    }

    public void OnPacket(object? sender, IByteReader reader) {
        var op = reader.Read<RecvOp>();
        PacketHandler<T>? handler = handlers.GetValueOrDefault(op);
        if (sender is not T session) return;

        // One failing handler must not disconnect the session.
        try {
            // Let another system schedule when to call Handle
            if (handler?.TryHandleDeferred(session, reader) ?? false) {
                return;
            }

            handler?.Handle(session, reader);
        } catch (Exception ex) {
            logger.Error(ex, "Packet handler failed for {OpCode} {Handler}", $"0x{(ushort) op:X4}", handler?.GetType().Name);
        }
    }

    private void Register(ImmutableDictionary<RecvOp, PacketHandler<T>>.Builder builder, PacketHandler<T> packetHandler) {
        logger.Debug("Registered [{OpCode}] {Name}", $"0x{(ushort) packetHandler.OpCode:X4}", packetHandler.GetType().Name);
        builder.Add(packetHandler.OpCode, packetHandler);
    }
}
