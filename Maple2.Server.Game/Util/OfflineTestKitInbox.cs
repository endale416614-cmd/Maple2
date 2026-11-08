using Maple2.Server.Game.Session;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Test-kit command inbox (V-03g): the dungeon harness drops chat command files (*.cmd, one command per line) into a
/// folder and the server runs them for the player in the world, so the harness types nothing after the world loads.
/// Off unless TestKit=true and OFFLINE_TESTKIT_INBOX names the folder (the launcher passes logs\testkit-inbox).
/// </summary>
public static class OfflineTestKitInbox {
    private static Timer? timer;
    private static int busy;

    /// <summary>
    /// Runs the lines of the folder's *.cmd files in name order and deletes each file once all its lines ran. Stops at
    /// the first line <paramref name="run"/> refuses (no player in the world yet) or at a file another process holds:
    /// that file and the later ones stay for the next call. Returns the number of lines run.
    /// </summary>
    public static int Process(string folder, Func<string, bool> run) {
        if (!Directory.Exists(folder)) {
            return 0;
        }

        int count = 0;
        foreach (string path in Directory.GetFiles(folder, "*.cmd").OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
            FileStream file;
            try {
                // Shares only the delete: until File.Delete no other process can open the file to read or write, which would fail the delete and run its lines twice.
                file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete);
            } catch (IOException) {
                // Another process still holds it (BT-137). Not an error: it and the later files wait for the next call, in name order.
                return count;
            }

            using StreamReader reader = new(file);
            while (reader.ReadLine() is { } line) {
                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }
                if (!run(line)) {
                    return count;
                }
                count++;
            }
            File.Delete(path);
        }
        return count;
    }

    public static void Start(Func<IEnumerable<GameSession>> sessions) {
        string? folder = Environment.GetEnvironmentVariable("OFFLINE_TESTKIT_INBOX");
        if (!OfflineTestKit.Enabled || string.IsNullOrEmpty(folder) || timer != null) {
            return;
        }

        try {
            Directory.CreateDirectory(folder);
            timer = new Timer(_ => {
                // A command can outlast the period (a map change): never run a file twice.
                if (Interlocked.Exchange(ref busy, 1) == 1) {
                    return;
                }
                try {
                    Process(folder, line => {
                        GameSession? session = sessions().FirstOrDefault(session => session.Field != null);
                        if (session == null) {
                            return false;
                        }
                        int exitCode = session.CommandHandler.Invoke(line.TrimStart('/'));
                        Log.Information("[TestKit] {Character}: {Line} -> {ExitCode}", session.Player.Value.Character.Name, line, exitCode);
                        return true;
                    });
                } catch (Exception ex) {
                    Log.Error(ex, "[TestKit] inbox {Folder}", folder);
                } finally {
                    Volatile.Write(ref busy, 0);
                }
            }, null, 0, 500);
        } catch (Exception ex) {
            // A test aid must never stop the game server.
            Log.Error(ex, "[TestKit] inbox {Folder} not started", folder);
        }
    }
}
