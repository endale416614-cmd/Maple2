using System;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Offline;

/// <summary>RV-01: a map with an automatic revival tells the client the countdown and the server revives the player after it.</summary>
public class AutoReviveTest {
    [Test]
    public void NoAutoRevival_HasNoDelay() {
        Assert.Multiple(() => {
            Assert.That(FieldPlayer.AutoRevivalDelay(AutoReviveType.None, 9), Is.Null);
            Assert.That(FieldPlayer.AutoRevivalDelay(AutoReviveType.Trigger, 0), Is.Null);
        });
    }

    [Test]
    public void AutoRevival_DelayIsTheMapTimeInSeconds() {
        Assert.Multiple(() => {
            Assert.That(FieldPlayer.AutoRevivalDelay(AutoReviveType.Trigger, 9), Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(FieldPlayer.AutoRevivalDelay(AutoReviveType.Countdown, 20), Is.EqualTo(TimeSpan.FromSeconds(20)));
        });
    }

    [Test]
    public void AutoRevivePacket_IsTheOpcodeAndTheMilliseconds() {
        var packet = RevivalPacket.AutoRevive(9000);
        Assert.Multiple(() => {
            Assert.That(packet.Length, Is.EqualTo(6));
            Assert.That(BitConverter.ToUInt16(packet.Buffer, 0), Is.EqualTo(0x00A4));
            Assert.That(BitConverter.ToInt32(packet.Buffer, 2), Is.EqualTo(9000));
        });
    }
}
