using System;
using System.Threading;

namespace Maple2.Tools;

/// <summary>
/// Single source of wall-clock time for game logic (events, resets, time sync), so tests can set the time.
/// At real time it returns exactly what DateTimeOffset.UtcNow / DateTime.Now return; it changes no time zone.
/// </summary>
public static class GameClock {
    private static readonly Func<DateTimeOffset> SystemClock = () => DateTimeOffset.UtcNow;
    private static Func<DateTimeOffset> source = SystemClock;

    /// <summary>Current time with offset zero. Replaces DateTimeOffset.UtcNow and DateTime.UtcNow.</summary>
    public static DateTimeOffset UtcNow => Volatile.Read(ref source)().ToUniversalTime();

    /// <summary>Current local time (Kind = Local). Replaces DateTime.Now.</summary>
    public static DateTime Now => UtcNow.LocalDateTime;

    /// <summary>Replaces the clock until the returned handle is disposed. For tests only.</summary>
    public static IDisposable Override(Func<DateTimeOffset> clock) {
        ArgumentNullException.ThrowIfNull(clock);
        Func<DateTimeOffset> previous = Interlocked.Exchange(ref source, clock);
        return new Restore(previous);
    }

    private sealed class Restore(Func<DateTimeOffset> previous) : IDisposable {
        private int disposed;

        public void Dispose() {
            if (Interlocked.Exchange(ref disposed, 1) == 0) {
                Volatile.Write(ref source, previous);
            }
        }
    }
}
