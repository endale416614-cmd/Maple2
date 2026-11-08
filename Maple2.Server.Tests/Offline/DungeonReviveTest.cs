using System;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-13b: a dungeon room's revival limit counts each player's safe revivals, and DungeonReviveCount tells the client the count.</summary>
public class DungeonReviveTest {
    [Test]
    public void SafeRevival_UpToTheRoomLimit() {
        Assert.Multiple(() => {
            Assert.That(RevivalHandler.CanSafeRevive(0, 0), Is.True, "a room with limit 0 has no limit");
            Assert.That(RevivalHandler.CanSafeRevive(0, 40), Is.True, "a room with limit 0 has no limit");
            Assert.That(RevivalHandler.CanSafeRevive(5, 4), Is.True, "the fifth safe revival of a raid");
            Assert.That(RevivalHandler.CanSafeRevive(5, 5), Is.False, "a sixth in Temple of Immortals 20004002, limit 5");
            Assert.That(RevivalHandler.CanSafeRevive(3, 2), Is.True, "the third safe revival of a chaos raid");
            Assert.That(RevivalHandler.CanSafeRevive(3, 3), Is.False, "a fourth in a chaos raid, limit 3");
        });
    }

    [Test]
    public void DungeonReviveCountPacket_IsTheOpcodeAndTheCount() {
        var packet = RevivalPacket.DungeonReviveCount(2);
        Assert.Multiple(() => {
            Assert.That(packet.Length, Is.EqualTo(6));
            Assert.That(BitConverter.ToUInt16(packet.Buffer, 0), Is.EqualTo(0x00FD));
            Assert.That(BitConverter.ToInt32(packet.Buffer, 2), Is.EqualTo(2), "captured as 02 00 00 00 after a run's second safe revival");
        });
    }
}
