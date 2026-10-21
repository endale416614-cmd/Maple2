using System;
using NUnit.Framework;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
public class GlobalPortalScheduleTest {
    private static GlobalPortalMetadata Portal(DateTime start, DateTime end, TimeSpan cycle) {
        return new GlobalPortalMetadata(80001, 100, start, end, cycle, TimeSpan.Zero, TimeSpan.FromSeconds(59), "", "", []);
    }

    [Test]
    public void TestFirstRun_1() {
        // start 2016-01-01 00:00, end 2099-01-01, cycle 3 h: FirstRun is 2026-10-20 15:00.
        var portal = Portal(new DateTime(2016, 1, 1, 0, 0, 0), new DateTime(2099, 1, 1, 0, 0, 0), TimeSpan.FromHours(3));
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 13, 0, 0)), Is.EqualTo(new DateTime(2026, 10, 20, 15, 0, 0)));
    }

    [Test]
    public void TestFirstRun_2() {
        // same portal, now = 2026-10-20 12:00:00 exactly: FirstRun is 2026-10-20 12:00 (a run exactly now counts).
        var portal = Portal(new DateTime(2016, 1, 1, 0, 0, 0), new DateTime(2099, 1, 1, 0, 0, 0), TimeSpan.FromHours(3));
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 12, 0, 0)), Is.EqualTo(new DateTime(2026, 10, 20, 12, 0, 0)));
    }

    [Test]
    public void TestFirstRun_3() {
        // start 2027-01-01 00:00 (ahead), end 2099-01-01, cycle 3 h: FirstRun is 2027-01-01 00:00.
        var portal = Portal(new DateTime(2027, 1, 1, 0, 0, 0), new DateTime(2099, 1, 1, 0, 0, 0), TimeSpan.FromHours(3));
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 13, 0, 0)), Is.EqualTo(new DateTime(2027, 1, 1, 0, 0, 0)));
    }

    [Test]
    public void TestFirstRun_4() {
        // end 2023-01-01 (past): null.
        var portal = Portal(new DateTime(2016, 1, 1, 0, 0, 0), new DateTime(2023, 1, 1, 0, 0, 0), TimeSpan.FromHours(3));
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 13, 0, 0)), Is.Null);
    }

    [Test]
    public void TestFirstRun_5() {
        // start 2026-10-20 00:00, end 2026-10-20 20:00, cycle 24 h: null (the next run, 2026-10-21 00:00, is after the end).
        var portal = Portal(new DateTime(2026, 10, 20, 0, 0, 0), new DateTime(2026, 10, 20, 20, 0, 0), TimeSpan.FromHours(24));
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 13, 0, 0)), Is.Null);
    }

    [Test]
    public void TestFirstRun_6() {
        // cycle TimeSpan.Zero: null.
        var portal = Portal(new DateTime(2016, 1, 1, 0, 0, 0), new DateTime(2099, 1, 1, 0, 0, 0), TimeSpan.Zero);
        Assert.That(portal.FirstRun(new DateTime(2026, 10, 20, 13, 0, 0)), Is.Null);
    }
}
