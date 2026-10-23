using NUnit.Framework;
using Maple2.Model.Error;
using Maple2.Server.Game.PacketHandlers;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class DungeonHallTest {
    [Test]
    public void HallMoveError() {
        Assert.That(DungeonRoomHandler.HallMoveError(false, 2000118, 83000001, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall), Is.EqualTo(DungeonRoomError.none));
        Assert.That(DungeonRoomHandler.HallMoveError(false, 83000001, 83000001, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall), Is.EqualTo(DungeonRoomError.s_room_dungeon_AlreadyColosseumHall));
        Assert.That(DungeonRoomHandler.HallMoveError(true, 83000002, 83000001, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall), Is.EqualTo(DungeonRoomError.s_room_dungeon_CantEnterInDungeon));
        Assert.That(DungeonRoomHandler.HallMoveError(false, 2000118, 0, DungeonRoomError.s_room_dungeon_AlreadyColosseumHall), Is.EqualTo(DungeonRoomError.s_room_dungeon_notOpenTimeDungeon));
    }
}
