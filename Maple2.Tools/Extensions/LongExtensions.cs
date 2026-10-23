namespace Maple2.Tools.Extensions;

public static class LongExtensions {
    /// <summary>
    /// Truncates a long value to a 32-bit int, handling overflow properly.
    /// This is useful for converting Environment.TickCount64 to match Environment.TickCount behavior.
    /// </summary>
    /// <param name="value">The long value to truncate</param>
    /// <returns>The truncated int value</returns>
    public static int Truncate32(this long value) {
        return (int) (0xFFFFFFFF & value);
    }

    /// <summary>
    /// Milliseconds from a 32-bit tick (one also sent to the client, such as an EndTick) to this 64-bit tick; negative while
    /// that tick is still ahead. Correct across the 32-bit wrap, which comes after 24.8 days of uptime.
    /// </summary>
    public static int TicksSince(this long tickCount, int tick) {
        return unchecked(tickCount.Truncate32() - tick);
    }
}

