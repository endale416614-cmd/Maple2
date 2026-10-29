using System.Globalization;

namespace Maple2.Server.Game.Trigger;

/// <summary>
/// The last 200 trigger state lines of all maps (R12-21), oldest first. A trigger that enters a state again within 5 s of that
/// state's line adds no line: the line counts it, ' (xN, last HH:mm:ss.fff)', so a trigger's current state is its line with the
/// latest time (BT-76). With OFFLINE_TRIGGER_RING_FILE set (by the launcher)
/// the ring is rewritten to that file every 2 s when it changed, so a session report shows where a quest or dungeon script
/// stopped even when OFFLINE_TRIGGER_TRACE is off. The servers are killed, not shut down, so there is no final flush.
/// </summary>
public static class TriggerRing {
    public const int Size = 200;
    private static readonly string[] entries = new string[Size];
    private static readonly string?[] triggers = new string?[Size];
    private static readonly string?[] states = new string?[Size];
    private static readonly int[] counts = new int[Size];
    private static readonly long[] ticks = new long[Size];
    private static readonly string?[] lastTimes = new string?[Size];
    // The original scripts poll in loops that change state every 100 ms (BT-76); a revisit or a retry after FoldMs gets its own line.
    // TODO: only a state re-entered within 5 s of its line's last entry is counted, so a slower polling loop still takes a line per change; raise FoldMs when a report shows one.
    private const long FoldMs = 5000;
    private static readonly object gate = new();
    private static long added;
    private static bool dirty;
    private static readonly string? file = Environment.GetEnvironmentVariable("OFFLINE_TRIGGER_RING_FILE");
    private static readonly System.Threading.Timer? timer =
        string.IsNullOrEmpty(file) ? null : new System.Threading.Timer(_ => Flush(file!), null, 2000, 2000);

    private static string Now() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public static void Add(string entry) {
        lock (gate) {
            Put(entry, null, null, 0);
        }
    }

    /// <summary>
    /// Adds a state line for the trigger, or counts it on the trigger's line for that state when it was entered within FoldMs.
    /// <paramref name="tick"/> is the field tick in ms (FieldManager.FieldTick).
    /// </summary>
    public static void Add(string trigger, string state, long tick) {
        lock (gate) {
            for (long i = added - 1; i >= Math.Max(0, added - Size); i--) {
                int slot = (int) (i % Size);
                if (triggers[slot] != trigger || states[slot] != state) {
                    continue;
                }
                if (tick - ticks[slot] > FoldMs) {
                    break;
                }
                counts[slot]++;
                ticks[slot] = tick;
                lastTimes[slot] = Now();
                dirty = true;
                return;
            }
            Put($"{trigger} {state}", trigger, state, tick);
        }
    }

    // Called under gate.
    private static void Put(string entry, string? trigger, string? state, long tick) {
        int slot = (int) (added % Size);
        entries[slot] = $"{Now()} {entry}";
        triggers[slot] = trigger;
        states[slot] = state;
        ticks[slot] = tick;
        counts[slot] = 1;
        added++;
        dirty = true;
    }

    public static List<string> Lines() {
        lock (gate) {
            long first = Math.Max(0, added - Size);
            var lines = new List<string>((int) (added - first));
            for (long i = first; i < added; i++) {
                int slot = (int) (i % Size);
                lines.Add(counts[slot] > 1 ? $"{entries[slot]} (x{counts[slot]}, last {lastTimes[slot]})" : entries[slot]);
            }
            return lines;
        }
    }

    /// <summary>Writes the ring to the file if it changed since the last write. Best effort: a report aid never stops a server.</summary>
    public static void Flush(string path) {
        lock (gate) {
            if (!dirty) {
                return;
            }
            dirty = false;
        }
        try {
            File.WriteAllLines(path, Lines());
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}