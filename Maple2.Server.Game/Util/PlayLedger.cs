using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Maple2.Model.Game.Dungeon;

namespace Maple2.Server.Game.Util;

/// <summary>
/// TM-01 (BT-237, BT-240): a local play ledger for balance measurements, ids and numbers only (no names, no chat, no
/// paths). It is its own file, play-ledger.log in the save folder the launcher passes as SQLITE_DIR, and play-ledger.old.log
/// once the file passes MaxBytes (so at most about 2 x MaxBytes). No problem report reads that folder's files but the save
/// database, and nothing here goes through the server logs. Without SQLITE_DIR (tests, a bare server) nothing is written.
/// A failed write is dropped: a ledger never stops the game. One line per event: "UTC event key=value ...".
/// </summary>
public static class PlayLedger {
    public const string FileName = "play-ledger.log";
    public const string OldFileName = "play-ledger.old.log";
    // The highest region count of a map is written each time it passes another multiple of this.
    public const int RegionStep = 100;

    public static string? FilePath { get; set; } = Environment.GetEnvironmentVariable("SQLITE_DIR") is { Length: > 0 } folder
        ? Path.Combine(folder, FileName) : null;
    public static long MaxBytes { get; set; } = 1_000_000;
    public static long SnapshotMs { get; set; } = 5 * 60 * 1000;

    private sealed class Tally {
        public long FirstHitMs, Total, Raised, Deaths;
    }

    // gate guards the in-memory totals only; fileGate guards the append and the roll, so a slow disk never holds up a field or packet thread.
    private static readonly object gate = new();
    private static readonly object fileGate = new();
    private static readonly ConditionalWeakTable<object, Tally> tallies = new();
    private static readonly ConcurrentDictionary<int, int> regionSteps = new();
    private static readonly SortedDictionary<string, long> expBySource = new(StringComparer.Ordinal);
    private static long lastSnapshotMs = Environment.TickCount64;

    private static string Pct(long part, long whole) => whole > 0 ? (part * 100.0 / whole).ToString("0.0", CultureInfo.InvariantCulture) : "-";

    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Write(string name, string fields) {
        if (FilePath is not { } file) {
            return;
        }
        try {
            string line = $"{DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)} {name} {fields}\n";
            lock (fileGate) {
                if (Path.GetDirectoryName(file) is { Length: > 0 } folder) {
                    Directory.CreateDirectory(folder);
                }
                var info = new FileInfo(file);
                if (info.Exists && info.Length >= MaxBytes) {
                    try {
                        File.Move(file, Path.Combine(info.DirectoryName ?? "", OldFileName), overwrite: true);
                    } catch (Exception) {
                        // The old file is locked: keep appending to this one, up to twice the cap, then drop lines until the move works.
                        if (info.Length >= 2 * MaxBytes) {
                            return;
                        }
                    }
                }
                File.AppendAllText(file, line);
            }
        } catch (Exception) {
            // dropped on purpose; logging the failure would put ledger text into the server log
        }
    }

    // A player's hit on a monster, before and after the fair-fight floor. Only what a line reads is counted: a boss's own hits
    // (boss line) and every hit inside a dungeon run (dungeon line); a common monster outside a run gets no tally.
    public static void PlayerHit(object npc, bool boss, DungeonRoomRecord? run, long calculated, long result) {
        if (FilePath == null || (!boss && run == null)) {
            return;
        }
        long now = Environment.TickCount64;
        if (boss) {
            Count(npc);
        }
        if (run != null) {
            Count(run);
        }

        void Count(object key) {
            Tally tally = tallies.GetValue(key, _ => new Tally());
            Interlocked.CompareExchange(ref tally.FirstHitMs, now, 0);
            Interlocked.Add(ref tally.Total, result);
            Interlocked.Add(ref tally.Raised, Math.Max(0, result - calculated));
        }
    }

    // A player's death in a dungeon run, whatever killed them.
    public static void CountDeath(DungeonRoomRecord? run) {
        if (FilePath != null && run != null) {
            Interlocked.Increment(ref tallies.GetValue(run, _ => new Tally()).Deaths);
        }
    }

    public static void Death(int npcId, int skillId, long hit, long maxHealth) {
        Write("death", $"npc={Id(npcId)} skill={Id(skillId)} hit={Id(hit)} maxhp={Id(maxHealth)} pct={Pct(hit, maxHealth)}");
    }

    // clockLeft is -1 for a run with no room timer, revivalsLeft -1 for a room with no revival limit.
    public static void DungeonEnd(int roomId, DungeonRoomRecord run, long endTick) {
        if (FilePath == null) {
            return;
        }
        tallies.TryGetValue(run, out Tally? tally);
        long clockLeft = run.TimerOn ? Math.Max(0, run.TimerDuration - (endTick - run.TimerStartTick)) / 1000 : -1;
        int limit = run.Metadata?.Limit?.MaxRevivalCount ?? 0;
        int revivalsLeft = limit > 0 ? Math.Max(0, limit - run.Revivals.Values.DefaultIfEmpty(0).Max()) : -1;
        Write("dungeon", $"room={Id(roomId)} result={run.State} seconds={Id(Math.Max(0, endTick - run.StartTick) / 1000)} clockleft={Id(clockLeft)} " +
            $"deaths={Id(tally?.Deaths ?? 0)} revivalsleft={Id(revivalsLeft)} raised={Pct(tally?.Raised ?? 0, tally?.Total ?? 0)}");
    }

    public static void BossKill(int mapId, int npcId, object npc) {
        if (FilePath == null) {
            return;
        }
        tallies.TryGetValue(npc, out Tally? tally);
        long seconds = tally is { FirstHitMs: > 0 } ? (Environment.TickCount64 - tally.FirstHitMs) / 1000 : -1;
        Write("boss", $"map={Id(mapId)} npc={Id(npcId)} seconds={Id(seconds)} raised={Pct(tally?.Raised ?? 0, tally?.Total ?? 0)}");
    }

    public static void LevelUp(int from, int to, string source) {
        Write("levelup", $"from={Id(from)} to={Id(to)} source={source}");
        Snapshot();
    }

    // EXP the character gained, by source; the totals of this server run are written every 5 minutes and at each level-up.
    public static void Exp(long amount, string source) {
        if (FilePath == null || amount <= 0) {
            return;
        }
        bool due;
        lock (gate) {
            expBySource[source] = expBySource.GetValueOrDefault(source) + amount;
            due = Environment.TickCount64 - lastSnapshotMs >= SnapshotMs;
        }
        if (due) {
            Snapshot();
        }
    }

    private static void Snapshot() {
        string fields;
        lock (gate) {
            lastSnapshotMs = Environment.TickCount64;
            if (expBySource.Count == 0) {
                return;
            }
            fields = string.Join(' ', expBySource.Select(entry => $"{entry.Key}={Id(entry.Value)}"));
        }
        Write("exp", fields);
    }

    // The most regions a map held at once: one line each time it passes another multiple of RegionStep.
    public static void Regions(int mapId, int count) {
        if (count < RegionStep || FilePath == null) {
            return;
        }
        int step = count / RegionStep;
        if (regionSteps.GetValueOrDefault(mapId) >= step) {
            return;
        }
        lock (gate) {
            if (regionSteps.GetValueOrDefault(mapId) >= step) {
                return;
            }
            regionSteps[mapId] = step;
        }
        Write("regions", $"map={Id(mapId)} max={Id(count)}");
    }
}
