using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>A stat rebuild set health, spirit and stamina to their totals (Feinting Guard, Shield Wall and Warhorn refilled them) and dropped the allocated points.</summary>
[TestFixture]
public class StatRebuildTest {
    private static Dictionary<BasicAttribute, long> Metadata(long health) {
        return new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Health, health },
            { BasicAttribute.Spirit, 100 },
            { BasicAttribute.Stamina, 120 },
        };
    }

    private static Stats Damaged() {
        var stats = new Stats(Metadata(1000), JobCode.Newbie);
        stats[BasicAttribute.Health].Current = 500;
        stats[BasicAttribute.Spirit].Current = 40;
        stats[BasicAttribute.Stamina].Current = 60;
        return stats;
    }

    [Test]
    public void PoolsSurviveRebuild() {
        Stats stats = Damaged();
        long[] saved = StatsManager.SavePools(stats);

        stats.Reset(Metadata(1000), JobCode.Newbie);
        stats[BasicAttribute.Health].AddTotal(90);
        stats.Total();
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1090));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(1090));

        StatsManager.RestorePools(stats, saved);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(500));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(40));
        Assert.That(stats[BasicAttribute.Stamina].Current, Is.EqualTo(60));
    }

    [Test]
    public void PoolsAreClampedToTheNewTotals() {
        Stats stats = Damaged();
        long[] saved = StatsManager.SavePools(stats);

        stats.Reset(Metadata(400), JobCode.Newbie);
        stats.Total();

        StatsManager.RestorePools(stats, saved);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(400));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(40));
        Assert.That(stats[BasicAttribute.Stamina].Current, Is.EqualTo(60));
    }

    [Test]
    public void StatAttributeBonus() {
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.Health, 5), Is.EqualTo((BasicAttribute.Health, 50L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.Strength, 3), Is.EqualTo((BasicAttribute.Strength, 3L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.CriticalRate, 2), Is.EqualTo((BasicAttribute.CriticalRate, 6L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.PhysicalAtk, 4).Amount, Is.EqualTo(0));
    }
}
