using System;
using Maple2.Model.Game;
using NUnit.Framework;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
public class OfflineCalendarTest {
    [Test]
    public void TestConstants() {
        Assert.That(OfflineCalendar.TicksPerDay, Is.EqualTo(60_000));
        Assert.That(OfflineCalendar.DaysPerYear, Is.EqualTo(60));
        Assert.That(OfflineCalendar.TicksPerSeason, Is.EqualTo(900_000));
        Assert.That(OfflineCalendar.TicksPerYear, Is.EqualTo(3_600_000));
    }

    [Test]
    public void TestToDate() {
        var date = OfflineCalendar.ToDate(0);
        Assert.That(date, Is.EqualTo(new OfflineCalendar.Date(1, OfflineCalendar.Season.Spring, 1, 0, 0)));
        Assert.That(date.ToString(), Is.EqualTo("00:00, Day 1 of Spring, Year 1"));

        date = OfflineCalendar.ToDate(OfflineCalendar.TicksPerDay * 15);
        Assert.That(date, Is.EqualTo(new OfflineCalendar.Date(1, OfflineCalendar.Season.Summer, 1, 0, 0)));

        date = OfflineCalendar.ToDate(OfflineCalendar.TicksPerYear);
        Assert.That(date, Is.EqualTo(new OfflineCalendar.Date(2, OfflineCalendar.Season.Spring, 1, 0, 0)));

        date = OfflineCalendar.ToDate(OfflineCalendar.TicksPerYear - 1);
        Assert.That(date, Is.EqualTo(new OfflineCalendar.Date(1, OfflineCalendar.Season.Winter, 15, 23, 59)));

        date = OfflineCalendar.ToDate(OfflineCalendar.TicksPerHour * 14 + OfflineCalendar.TicksPerHour / 2);
        Assert.That(date.ToString(), Is.EqualTo("14:30, Day 1 of Spring, Year 1"));
    }

    [Test]
    public void TestTicksFromRealMilliseconds() {
        Assert.That(OfflineCalendar.TicksFromRealMilliseconds(1_000_000), Is.EqualTo(60_000));
    }

    [Test]
    public void TestHourOfDay() {
        Assert.That(OfflineCalendar.HourOfDay(OfflineCalendar.TicksPerDay * 3 + OfflineCalendar.TicksPerHour * 6 + OfflineCalendar.TicksPerHour / 4),
            Is.EqualTo(6.25));
    }

    [Test]
    public void Now_AddsTheRealTimeSinceTheSave() {
        Assert.That(OfflineCalendar.Now(1000, 5000, 6000), Is.EqualTo(1060)); // 1 s = 60 ticks
        Assert.That(OfflineCalendar.Now(1000, 5000, 4000), Is.EqualTo(1000)); // a clock set back adds nothing
        Assert.That(OfflineCalendar.Now(0, 0, 10_000_000), Is.EqualTo(7200)); // the gap counts at most 120 s
        Assert.That(OfflineCalendar.Now(-5, 0, 0), Is.EqualTo(0)); // negative ticks are clamped to 0
    }
}