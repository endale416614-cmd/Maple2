using System.Globalization;

namespace Maple2.Server.Game.Trigger;

/// <summary>
/// The last 200 trigger state changes of all maps (R12-21), oldest first. With OFFLINE_TRIGGER_RING_FILE set (by the launcher)
/// the ring is rewritten to that file every 2 s when it changed, so a session report shows where a quest or dungeon script
/// stopped even when OFFLINE_TRIGGER_TRACE is off. The servers are killed, not shut down, so there is no final flush.
/// </summary>
public static class TriggerRing {
    public const int Size = 200;
    private static readonly string[] entries = new string[Size];
    private static readonly object gate = new();
    private static long added;
    private static bool dirty;
    private static readonly string? file = Environment.GetEnvironmentVariable("OFFLINE_TRIGGER_RING_FILE");
    private static readonly System.Threading.Timer? timer =
        string.IsNullOrEmpty(file) ? null : new System.Threading.Timer(_ => Flush(file!), null, 2000, 2000);

    public static void Add(string entry) {
        lock (gate) {
            entries[added % Size] = $"{DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {entry}";
            added++;
            dirty = true;
        }
    }

    public static List<string> Lines() {
        lock (gate) {
            long first = Math.Max(0, added - Size);
            var lines = new List<string>((int) (added - first));
            for (long i = first; i < added; i++) {
                lines.Add(entries[i % Size]);
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