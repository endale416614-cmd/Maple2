using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class RideConsumeEpTest {
    [Test]
    public void Cost_Null() {
        Assert.That(Maple2.Server.Game.PacketHandlers.RideConsumeEpHandler.Cost(null), Is.EqualTo(0));
    }

    [Test]
    public void Cost_Valid() {
        var metadata = new RideMetadata(
            Id: 1,
            Model: "test",
            Basic: new RideMetadataBasic(
                Type: 0,
                SkillSetId: 0,
                SummonTime: 0,
                RunXStamina: 15,
                EnableSwim: false,
                FallDamageDown: 0,
                Passengers: 0
            ),
            Speed: new RideMetadataSpeed(
                WalkSpeed: 0,
                RunSpeed: 0,
                RunXSpeed: 0,
                SwimSpeed: 0
            ),
            Stats: new Dictionary<BasicAttribute, long>()
        );

        Assert.That(Maple2.Server.Game.PacketHandlers.RideConsumeEpHandler.Cost(metadata), Is.EqualTo(15));
    }

    [Test]
    public void Cost_Zero() {
        var metadata = new RideMetadata(
            Id: 1,
            Model: "test",
            Basic: new RideMetadataBasic(
                Type: 0,
                SkillSetId: 0,
                SummonTime: 0,
                RunXStamina: 0,
                EnableSwim: false,
                FallDamageDown: 0,
                Passengers: 0
            ),
            Speed: new RideMetadataSpeed(
                WalkSpeed: 0,
                RunSpeed: 0,
                RunXSpeed: 0,
                SwimSpeed: 0
            ),
            Stats: new Dictionary<BasicAttribute, long>()
        );

        Assert.That(Maple2.Server.Game.PacketHandlers.RideConsumeEpHandler.Cost(metadata), Is.EqualTo(0));
    }
}
