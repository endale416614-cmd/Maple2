using System.Collections.Concurrent;
using Maple2.Database.Storage;
using Maple2.Model.Error;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.PacketHandlers;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class DungeonRoomHandler : PacketHandler<GameSession> {
    public override RecvOp OpCode => RecvOp.RoomDungeon;

    // source: Challenge Map hall buttons and a hall room's Lobby button send one byte, no payload; KMS2_DEV (MS2Recomp UIDungeonControlCenter) sends 4, 13, 14 and Global numbers every command from 9 one higher, so Global is 4, 14, 15 (DL-R; confirm with one click).
    private enum Command : byte {
        Reset = 1,
        Create = 2,
        EnterLobby = 3,
        ChaosHall = 4,
        EnterField = 10,
        ColosseumHall = 14,
        LapentaHall = 15,
    }

    private static readonly ConcurrentDictionary<byte, bool> UnhandledCommands = new();

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Reset:
                HandleReset(session);
                break;
            case Command.Create:
                HandleCreate(session, packet);
                break;
            case Command.EnterLobby:
                HandleEnter(session, packet);
                break;
            case Command.EnterField:
                HandleEnterField(session);
                break;
            case Command.ChaosHall:
                EnterHall(session, session.ServerTableMetadata.ConstantsTable.ChaosDungeonHallFieldID, DungeonRoomError.s_room_dungeon_AlreadyChaosHall);
                break;
            case Command.ColosseumHall:
                EnterHall(session, session.ServerTableMetadata.ConstantsTable.ColosseumDungeonHallFieldID, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall);
                break;
            case Command.LapentaHall:
                EnterHall(session, session.ServerTableMetadata.ConstantsTable.LapentaDungeonHallFieldID, DungeonRoomError.s_room_dungeon_AlreadyLapentaHall);
                break;
            default:
                if (UnhandledCommands.TryAdd((byte) command, true)) {
                    Logger.Information("RoomDungeon command {Command} is not handled offline", (byte) command);
                }
                break;
        }
    }

    private void HandleReset(GameSession session) {
        session.Dungeon.Reset();
    }

    private void HandleCreate(GameSession session, IByteReader packet) {
        int dungeonId = packet.ReadInt();
        bool withParty = packet.ReadBool();
        int unknown = packet.ReadInt();
        byte unknown2 = packet.ReadByte();

        session.Dungeon.CreateDungeonRoom(dungeonId, withParty);
    }

    private void HandleEnter(GameSession session, IByteReader packet) {
        int unknown = packet.ReadInt();
        session.Dungeon.EnterLobby();
    }

    private void HandleEnterField(GameSession session) {
        // enter first field of dungeon
        session.Dungeon.EnterInitField();
    }

    // The hall field (constants.xml) holds the entry portals of its rooms; Queen's Parlor 83000001 also holds Alberto Bean.
    private static void EnterHall(GameSession session, int mapId, DungeonRoomError alreadyThere) {
        if (session.Field is null) return;
        DungeonRoomError error = HallMoveError(session.Field is DungeonFieldManager, session.Field.MapId, mapId, alreadyThere);
        if (error != DungeonRoomError.none) {
            session.Send(DungeonRoomPacket.Error(error));
            return;
        }
        session.Send(session.PrepareField(mapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    /// <summary>Why the player cannot go to the hall now: no hall field, inside a dungeon room, or already there.</summary>
    public static DungeonRoomError HallMoveError(bool inDungeonRoom, int currentMapId, int hallMapId, DungeonRoomError alreadyThere) {
        if (hallMapId == 0) return DungeonRoomError.s_room_dungeon_notOpenTimeDungeon;
        if (inDungeonRoom) return DungeonRoomError.s_room_dungeon_CantEnterInDungeon;
        return currentMapId == hallMapId ? alreadyThere : DungeonRoomError.none;
    }
}
