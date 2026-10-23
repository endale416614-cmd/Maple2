namespace Maple2.Model.Game;

/// <summary>
/// In-game calendar at 1x (maintainer 2026-10-20): 60 ticks a real second, 2,500 ticks an hour, 60,000 a day
/// (about 16.7 minutes), 15-day seasons, 60-day years (about 16.7 hours). Tick 0 is 00:00 on Day 1 of Spring, Year 1.
/// </summary>
public static class OfflineCalendar {
    public const int TicksPerRealSecond = 60;
    public const int TicksPerHour = 2_500;
    public const int HoursPerDay = 24;
    public const int TicksPerDay = TicksPerHour * HoursPerDay;
    public const int DaysPerSeason = 15;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
    public const long TicksPerSeason = (long) TicksPerDay * DaysPerSeason;
    public const long TicksPerYear = TicksPerSeason * SeasonsPerYear;

    public enum Season { Spring, Summer, Fall, Winter }

    /// <summary>Year from 1, day of the season 1-15, hour 0-23, minute 0-59.</summary>
    public readonly record struct Date(int Year, Season Season, int Day, int Hour, int Minute) {
        public override string ToString() => $"{Hour:00}:{Minute:00}, Day {Day} of {Season}, Year {Year}";
    }

    public static Date ToDate(long ticks) {
        if (ticks < 0) {
            ticks = 0;
        }

        int year = (int)(ticks / TicksPerYear) + 1;
        long remainingTicks = ticks % TicksPerYear;
        Season season = (Season)((int)(remainingTicks / TicksPerSeason));
        remainingTicks = remainingTicks % TicksPerSeason;
        int day = (int)(remainingTicks / TicksPerDay) + 1;
        remainingTicks = remainingTicks % TicksPerDay;
        int hour = (int)(remainingTicks / TicksPerHour);
        remainingTicks = remainingTicks % TicksPerHour;
        int minute = (int)(remainingTicks * 60 / TicksPerHour);

        return new Date(year, season, day, hour, minute);
    }

    /// <summary>Ticks that pass in a span of real time at 1x.</summary>
    public static long TicksFromRealMilliseconds(long milliseconds) => milliseconds * TicksPerRealSecond / 1000;

    /// <summary>World ticks now, from the last save: the saved ticks plus the real time since then, at most maxGapMs (a World that
    /// stopped saving does not run on).</summary>
    public static long Now(long savedTicks, long savedAtMs, long nowMs, long maxGapMs = 120_000) {
        return Math.Max(0, savedTicks) + TicksFromRealMilliseconds(Math.Clamp(nowMs - savedAtMs, 0, maxGapMs));
    }

    /// <summary>Hour of the day with its fraction, 0 to below 24 (for light by hour).</summary>
    public static double HourOfDay(long ticks) => (double)(ticks % TicksPerDay) / TicksPerHour;
}