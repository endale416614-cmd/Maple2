using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>A home's decoration exp restarts at each level and stops at the top level (the table gives the cost of each level).</summary>
public class HomeExpTest {
    private static readonly int[] Costs = [0, 100, 300, 1000, 2100, 5500, 7700, 9900, 13200, 16500];
    private static readonly Dictionary<int, MasteryUgcHousingTable.Entry> Table = new();

    static HomeExpTest() {
        for (int level = 1; level <= Costs.Length; level++) {
            Table[level] = new MasteryUgcHousingTable.Entry(level, Costs[level - 1], 0);
        }
    }

    [Test]
    public void GainExpRestartsAtEachLevel() {
        var home = new Home { DecorationLevel = 1 };

        Assert.That(home.GainExp(250, Table), Is.EqualTo(250));
        Assert.That((home.DecorationLevel, home.DecorationExp), Is.EqualTo((2L, 150L)));

        Assert.That(home.GainExp(200, Table), Is.EqualTo(200));
        Assert.That((home.DecorationLevel, home.DecorationExp), Is.EqualTo((3L, 50L)));
    }

    [Test]
    public void GainExpStopsAtTheTopLevel() {
        var home = new Home { DecorationLevel = 1 };

        long counted = 0;
        for (int i = 0; i < 100 && home.DecorationLevel < 10; i++) {
            counted += home.GainExp(1100, Table);
        }

        Assert.That((home.DecorationLevel, home.DecorationExp), Is.EqualTo((10L, 0L)));
        Assert.That(counted, Is.EqualTo(56300));
        Assert.That(home.GainExp(1100, Table), Is.EqualTo(0));
    }

    [Test]
    public void OldSaveKeepsLevelling() {
        var home = new Home { DecorationLevel = 4, DecorationExp = 1500 };

        Assert.That(home.GainExp(1100, Table), Is.EqualTo(1100));
        Assert.That((home.DecorationLevel, home.DecorationExp), Is.EqualTo((5L, 500L)));
    }

    [Test]
    public void OldSaveOverflowClears() {
        var home = new Home { DecorationLevel = 10, DecorationExp = 5000 };

        Assert.That(home.GainExp(1100, Table), Is.EqualTo(0));
        Assert.That(home.DecorationExp, Is.EqualTo(0));
    }

    [Test]
    public void TotalExp() {
        Assert.That(new Home { DecorationLevel = 0 }.TotalExp(Table), Is.EqualTo(0));
        Assert.That(new Home { DecorationLevel = 2, DecorationExp = 150 }.TotalExp(Table), Is.EqualTo(250));
        Assert.That(new Home { DecorationLevel = 10 }.TotalExp(Table), Is.EqualTo(56300));
    }
}
