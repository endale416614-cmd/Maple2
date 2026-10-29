using System;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Offline;

/// <summary>A dismount the client asks for is echoed with the client's own follow-up fields.</summary>
public class RideStopTest {
    [Test]
    public void StopEcho_RepeatsTheClientsFields() {
        var packet = RidePacket.Stop(7, new RideOffActionEcho(RideOffType.Taxi, false, BitConverter.GetBytes(2000062)));

        Assert.Multiple(() => {
            Assert.That(packet.Length, Is.EqualTo(13));
            Assert.That(BitConverter.ToUInt16(packet.Buffer, 0), Is.EqualTo((ushort) SendOp.ResponseRide));
            Assert.That(packet.Buffer[2], Is.EqualTo(1));
            Assert.That(BitConverter.ToInt32(packet.Buffer, 3), Is.EqualTo(7));
            Assert.That(packet.Buffer[7], Is.EqualTo((byte) RideOffType.Taxi));
            Assert.That(packet.Buffer[8], Is.EqualTo(0));
            Assert.That(BitConverter.ToInt32(packet.Buffer, 9), Is.EqualTo(2000062));
        });
    }

    [Test]
    public void ReadOffActionFields_ReturnsTheRestOfThePacket() {
        var writer = new ByteWriter();
        writer.Write<RideOffType>(RideOffType.CashCall);
        writer.WriteBool(false);
        writer.WriteInt(2000062);
        writer.WriteByte(3);
        writer.WriteLong(123456789);
        var reader = new ByteReader(writer.Buffer[..writer.Length]);
        reader.Read<RideOffType>();
        reader.ReadBool();

        byte[] fields = RideHandler.ReadOffActionFields(reader);

        Assert.That(fields, Has.Length.EqualTo(13));
        Assert.That(fields, Is.EqualTo(writer.Buffer[2..writer.Length]));
    }

    [Test]
    public void ReadOffActionFields_OverTheCap_IsEmpty() {
        Assert.Multiple(() => {
            Assert.That(RideHandler.ReadOffActionFields(new ByteReader(new byte[RideHandler.MaxOffActionBytes + 1])), Is.Empty);
            Assert.That(RideHandler.ReadOffActionFields(new ByteReader(new byte[RideHandler.MaxOffActionBytes])), Has.Length.EqualTo(RideHandler.MaxOffActionBytes));
        });
    }
}
