using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>A heartbeat reply that the session thread handles late keeps the player in the game (HB-01, BT-128).</summary>
public class HeartbeatTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void LateReply_KeepsTheSession() {
        // A session without a socket, holding what Disconnect touches: a disconnect shows in its flag, not as a NullReferenceException.
        var session = (GameSession) RuntimeHelpers.GetUninitializedObject(typeof(GameSession));
        GC.SuppressFinalize(session);
        SetField(session, "Logger", Log.Logger);
        SetField(session, "UnhandledOpcodes", new ConcurrentDictionary<ushort, int>());
        SetField(session, "sendQueue", new BlockingCollection<(byte[], int)>());
        SetField(session, "gameDisposeState", 2); // no leave save or field cleanup

        // A login or a map load held the session thread: the reply is handled 2.5 s after the request (players were dropped at 2.15 to 3.36 s).
        var writer = new ByteWriter();
        writer.WriteInt(Environment.TickCount - Constant.MaxAllowedLatency - 500); // the request's tick, echoed
        writer.WriteInt(123456); // the client's tick
        new ResponseHeartbeatHandler().Handle(session, new ByteReader(writer.Buffer[..writer.Length]));

        Assert.That(GetField(session, "disconnecting"), Is.EqualTo(0), "the late reply disconnected the player");
    }

    private static FieldInfo FindField(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) => FindField(target, name).SetValue(target, value);

    private static object? GetField(object target, string name) => FindField(target, name).GetValue(target);
}
