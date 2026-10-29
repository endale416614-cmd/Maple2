using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>GD-02: the guild level follows the guild EXP, and the daily donation and check-in counts are per day.</summary>
[TestFixture]
public class GuildDonationTest {
    private static GuildTable.Property Level(short level, int experience) {
        return new GuildTable.Property(level, experience, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0f, 0f, 0, 0, 0, 0);
    }

    private static GuildTable Table() {
        var properties = new Dictionary<short, GuildTable.Property> {
            [1] = Level(1, 10000),
            [2] = Level(2, 30000),
            [3] = Level(3, 70000),
        };
        return new GuildTable(
            new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>>(),
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            properties);
    }

    [TestCase(0, 1)]
    [TestCase(9999, 1)]
    [TestCase(10000, 2)]
    [TestCase(69999, 3)]
    [TestCase(70000, 3)]
    [TestCase(1000000, 3)]
    public void PropertyFor(int experience, int level) {
        Assert.That(Table().PropertyFor(experience).Level, Is.EqualTo((short) level));
    }

    [Test]
    public void DonationsToday() {
        var member = new GuildMember { Info = null!, DailyDonationCount = 4, DonationTime = 99 };
        Assert.That(member.DonationsToday(100), Is.EqualTo(0));

        member.DonationTime = 100;
        Assert.That(member.DonationsToday(100), Is.EqualTo(4));
    }

    [Test]
    public void CheckedInToday() {
        var member = new GuildMember { Info = null!, CheckinTime = 99 };
        Assert.That(member.CheckedInToday(100), Is.False);

        member.CheckinTime = 150;
        Assert.That(member.CheckedInToday(100), Is.True);
    }
}
