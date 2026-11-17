using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Caching;
using Grpc.Core;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.World.Service;
using Maple2.Tools;
using NUnit.Framework;
using WorldClient = Maple2.Server.World.Service.World.WorldClient;

namespace Maple2.Server.Tests.Offline;

/// <summary>RL-01 (BT-236): at a dungeon room's revival limit a safe-revive request is answered with a notice that names Leave the Dungeon, and a
/// dead player's leave request is not refused. Both run through the real handlers on a session made of bare objects.</summary>
[TestFixture]
public class RevivalLimitTest {
    private const int Limit = 3;
    private const long CharacterId = 7;
    private const int ReturnMap = 2000062;

    [Test]
    public void TheRequestAtTheLimitGetsAnAnswer() {
        var scene = new Scene(revivals: Limit);
        scene.Session.Field = scene.Dungeon;

        scene.Handle(new RevivalHandler(), [0]); // Command.SafeRevive

        Assert.That(scene.Sent(SendOp.Notice), Has.Count.EqualTo(1), "a notice answers the request");
        byte[] notice = scene.Sent(SendOp.Notice).Single();
        Assert.Multiple(() => {
            Assert.That(notice[2], Is.EqualTo(4), "Notice command");
            Assert.That(BitConverter.ToInt16(notice, 3), Is.EqualTo((short) (NoticePacket.Flags.Alert | NoticePacket.Flags.Message)));
            Assert.That(Encoding.Unicode.GetString(notice, 10, notice.Length - 10), Does.Contain("Leave the Dungeon"));
        });
    }

    [Test]
    public void TheNoticeIsTheHandlersOwn() {
        ByteWriter packet = RevivalHandler.RevivalLimitNotice();
        Assert.That(Encoding.Unicode.GetString(packet.Buffer, 10, packet.Length - 10), Does.Contain("no safe revivals left"));
    }

    // Leave the Dungeon is RequestMoveField 1 (LeaveDungeon). A dead player's request reaches the world server with the return map, as a living
    // player's does: nothing on the way reads the player's death. The bare session ends where the migration save and the socket would start,
    // so the test stops at the request and the migration packet.
    [Test]
    public void ADeadPlayerCanLeaveTheDungeon() {
        var scene = new Scene(revivals: Limit);
        scene.Session.Field = scene.Dungeon;
        SetField(scene.Player, "isDead", true);
        Assert.That(scene.Player.IsDead, Is.True);

        try {
            scene.Handle(new MoveFieldHandler(), [1]); // Command.LeaveDungeon
        } catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException) {
            // the session's own teardown after the migration, which needs its socket
        }

        Assert.That(scene.World.Requests.Select(request => request.MapId), Is.EqualTo(new[] { ReturnMap }), "the request names the return map");
        Assert.That(scene.Sent(SendOp.GameToGame), Has.Count.EqualTo(1), "the client is told where to go");
    }

    private sealed class FakeWorld : WorldClient {
        public readonly List<MigrateOutRequest> Requests = [];

        public override MigrateOutResponse MigrateOut(MigrateOutRequest request, CallOptions options) {
            Requests.Add(request);
            return new MigrateOutResponse { IpAddress = "127.0.0.1", Port = 20001, Token = 1 };
        }
    }

    private sealed class Scene {
        public readonly GameSession Session = Bare<GameSession>();
        public readonly FieldPlayer Player = Bare<FieldPlayer>();
        public readonly DungeonFieldManager Dungeon = Bare<DungeonFieldManager>();
        public readonly FakeWorld World = new();
        private readonly ConcurrentDictionary<SendOp, byte[]> sent = [];

        public Scene(int revivals) {
            var character = Bare<Character>();
            character.ReturnMaps = new LimitedStack<int>(3, ReturnMap);
            character.MapId = 20004002;
            var home = Bare<Home>();
            home.Indoor = Bare<PlotInfo>();
            var model = Bare<Maple2.Model.Game.Player>();
            SetField(model, "Character", character);
            SetField(model, "<Home>k__BackingField", home);
            SetField(Player, "<Value>k__BackingField", model);

            var room = Bare<DungeonRoomRecord>();
            SetField(room, "Metadata", Bare<DungeonRoomMetadata>() with { Limit = Bare<DungeonRoomLimitMetadata>() with { MaxRevivalCount = Limit } });
            SetField(room, "Revivals", new ConcurrentDictionary<long, int> { [CharacterId] = revivals });
            SetField(Dungeon, "<DungeonRoomRecord>k__BackingField", room);
            SetField(Dungeon, "Metadata", Bare<MapMetadata>() with { Property = Bare<MapMetadataProperty>() with { NoRevivalHere = false } });
            SetField(Dungeon, "<Lobby>k__BackingField", Dungeon);
            SetField(Dungeon, "RoomFields", new ConcurrentDictionary<int, DungeonFieldManager>());
            SetField(Dungeon, "<MapId>k__BackingField", 20004002);

            var maps = Bare<MapMetadataStorage>();
            var cache = new LRUCache<int, MapMetadata>(10, 1);
            cache.AddReplace(ReturnMap, Bare<MapMetadata>());
            SetField(maps, "Cache", cache);
            var tables = Bare<ServerTableMetadataStorage>();
            SetField(tables, "instanceFieldTable", new Lazy<InstanceFieldTable>(() => new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));

            // The session as Session.Send and Disconnect read it.
            SetField(Session, "sendQueue", new BlockingCollection<(byte[] packet, int length)>(new ConcurrentQueue<(byte[], int)>()));
            SetField(Session, "lastSentPackets", sent);
            SetField(Session, "preMigrationSaved", true);
            SetField(Session, "<CharacterId>k__BackingField", CharacterId);
            SetField(Session, "<World>k__BackingField", World);
            SetField(Session, "<MapMetadata>k__BackingField", maps);
            SetField(Session, "<ServerTableMetadata>k__BackingField", tables);
            SetField(Session, "<Player>k__BackingField", Player);
            SetField(Player, "Session", Session);
            Session.Guild = Bare<Maple2.Server.Game.Manager.GuildManager>();
        }

        public void Handle(Maple2.Server.Core.PacketHandlers.PacketHandler<GameSession> handler, byte[] packet) =>
            handler.Handle(Session, new ByteReader(packet));

        public List<byte[]> Sent(SendOp op) => sent.TryGetValue(op, out byte[]? packet) ? [packet] : [];
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
