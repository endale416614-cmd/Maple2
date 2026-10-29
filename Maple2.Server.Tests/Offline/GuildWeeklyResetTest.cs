using Maple2.Model.Game;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>GD-04: the weekly reset clears the weekly contribution of a cached guild and keeps the total.</summary>
[TestFixture]
public class GuildWeeklyResetTest {
    [Test]
    public void ClearWeeklyContribution() {
        var guild = new Guild(1, "Weekly", 1, 1, "Leader");
        guild.Members[1] = new GuildMember { Info = null!, WeeklyContribution = 5, TotalContribution = 9 };
        guild.Members[2] = new GuildMember { Info = null!, WeeklyContribution = 7, TotalContribution = 11 };

        guild.ClearWeeklyContribution();

        Assert.That(guild.Members[1].WeeklyContribution, Is.EqualTo(0));
        Assert.That(guild.Members[2].WeeklyContribution, Is.EqualTo(0));
        Assert.That(guild.Members[1].TotalContribution, Is.EqualTo(9));
        Assert.That(guild.Members[2].TotalContribution, Is.EqualTo(11));
    }
}
