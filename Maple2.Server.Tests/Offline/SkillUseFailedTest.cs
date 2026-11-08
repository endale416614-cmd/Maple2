using System;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-14: a failed cast as the captures send it. Golden: the capture "4-Ellinia, Godspring, Great Ellin Tree": skill 10900051, with that packet's cast uid and tick.</summary>
[TestFixture]
public class SkillUseFailedTest {
    [Test]
    public void TheCapturedPacket() {
        var property = new SkillMetadataProperty(default, default, default, default, default, ActorState.None, false, false, false, false, false, false, 0, 0, string.Empty);
        var record = new SkillRecord(new SkillMetadata(10900051, 1, null, property, null!, null!), 0x0000357540574763, null!) { ServerTick = 0x0BD4149F };

        ByteWriter packet = SkillUseFailedPacket.Fail(record);

        Assert.That(Convert.ToHexString(packet.Buffer[..packet.Length]).ToLowerInvariant(), Is.EqualTo("4100" + "05" + "6347574075350000" + "5352a600" + "06" + "9f14d40b" + "0000" + "00000000"));
    }
}