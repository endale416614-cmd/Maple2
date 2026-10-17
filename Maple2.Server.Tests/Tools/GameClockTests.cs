using System;
using Maple2.Model.Enum;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.Tools;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
[NonParallelizable] // GameClock.Override is process-wide.
public class GameClockTests {
    private static readonly DateTimeOffset Fixed = new(2027, 3, 14, 12, 30, 0, TimeSpan.Zero);

    [Test]
    public void RealTime_MatchesSystemClock() {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset now = GameClock.UtcNow;
        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.That(now, Is.InRange(before, after));
        Assert.That(now.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(GameClock.Now.Kind, Is.EqualTo(DateTimeKind.Local));
    }

    [Test]
    public void Override_SetsUtcNowAndNow() {
        using (GameClock.Override(() => Fixed)) {
            Assert.That(GameClock.UtcNow, Is.EqualTo(Fixed));
            Assert.That(GameClock.Now, Is.EqualTo(Fixed.LocalDateTime));
            Assert.That(GameClock.Now.Kind, Is.EqualTo(DateTimeKind.Local));
        }
    }

    [Test]
    public void Override_WithOffset_ReturnsSameInstantInUtc() {
        var plusNine = new DateTimeOffset(2027, 3, 14, 21, 30, 0, TimeSpan.FromHours(9));
        using (GameClock.Override(() => plusNine)) {
            Assert.That(GameClock.UtcNow, Is.EqualTo(Fixed));
            Assert.That(GameClock.UtcNow.Offset, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public void Dispose_RestoresPreviousClock_InNestedOrder() {
        IDisposable outer = GameClock.Override(() => Fixed);
        IDisposable inner = GameClock.Override(() => Fixed.AddDays(1));
        Assert.That(GameClock.UtcNow, Is.EqualTo(Fixed.AddDays(1)));

        inner.Dispose();
        inner.Dispose(); // second dispose must not restore twice
        Assert.That(GameClock.UtcNow, Is.EqualTo(Fixed));

        outer.Dispose();
        Assert.That(GameClock.UtcNow - DateTimeOffset.UtcNow, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void Override_Null_Throws() {
        Assert.Throws<ArgumentNullException>(() => GameClock.Override(null!));
    }
}

/// <summary>
/// Pins today's GameEvent.IsActive semantics: start/end compared as local time, days of week and part times compared in UTC.
/// Open Decision 11 (event time zone) is undecided; when it is, these expectations change with it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class GameEventActiveTest {
    // A Wednesday window, far from any zone boundary so the result does not depend on the test machine's zone.
    private static readonly DateTime Start = new(2027, 3, 10, 0, 0, 0);
    private static readonly DateTime End = new(2027, 3, 24, 0, 0, 0);

    private static GameEvent Event(DayOfWeek[]? days = null, TimeSpan startPart = default, TimeSpan endPart = default) {
        return new GameEvent(new GameEventMetadata(1, GameEventType.StringBoard, Start, End, startPart, endPart,
            days ?? [], new StringBoard("test", 0), "", "", "", ""));
    }

    private static bool ActiveAt(GameEvent gameEvent, DateTimeOffset utc) {
        using (GameClock.Override(() => utc)) {
            return gameEvent.IsActive();
        }
    }

    [Test]
    public void BeforeInsideAfter() {
        GameEvent gameEvent = Event();
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 8, 12, 0, 0, TimeSpan.Zero)), Is.False);
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 15, 12, 0, 0, TimeSpan.Zero)), Is.True);
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 26, 12, 0, 0, TimeSpan.Zero)), Is.False);
    }

    [Test]
    public void DayOfWeek_UsesUtcDay() {
        GameEvent gameEvent = Event(days: [DayOfWeek.Monday]);
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 15, 12, 0, 0, TimeSpan.Zero)), Is.True);  // Monday
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 16, 12, 0, 0, TimeSpan.Zero)), Is.False); // Tuesday
    }

    [Test]
    public void PartTime_UsesUtcTimeOfDay() {
        GameEvent gameEvent = Event(startPart: TimeSpan.FromHours(10), endPart: TimeSpan.FromHours(14));
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 15, 9, 0, 0, TimeSpan.Zero)), Is.False);
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 15, 12, 0, 0, TimeSpan.Zero)), Is.True);
        Assert.That(ActiveAt(gameEvent, new DateTimeOffset(2027, 3, 15, 15, 0, 0, TimeSpan.Zero)), Is.False);
    }
}
