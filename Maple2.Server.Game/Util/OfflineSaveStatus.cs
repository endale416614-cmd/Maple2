using System.Text.Json;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Save confirmation for the offline launcher. When OFFLINE_SAVE_STATUS_FILE is set, the file holds the latest
/// state of the player's save: "active" once the character is in the game world, then "committed" or "failed"
/// for the save made when the player leaves the game or moves to another map server session (migration).
/// Written atomically (temp file + move) so a reader never sees a partial file.
/// </summary>
public static class OfflineSaveStatus {
    public const string Active = "active";
    public const string Committed = "committed";
    public const string Failed = "failed";

    private static readonly ILogger Logger = Log.ForContext(typeof(OfflineSaveStatus));
    private static readonly object WriteLock = new();
    // Per character: the session that last reported the character active.
    private static readonly Dictionary<long, long> ActiveSession = new();
    private static long lastSession;

    /// <summary>A new id for a game session that reports its status.</summary>
    public static long NewSession() => Interlocked.Increment(ref lastSession);

    // Does nothing and returns true if the environment variable is not set.
    // With a session id, a result from an older session than the one that last reported "active" is skipped: a session
    // that migrated away finishes after the next session has entered the world, and must not hide that session's pending save.
    public static bool Write(long characterId, long accountId, string result, string? error, long session = 0) {
        string? path = Environment.GetEnvironmentVariable("OFFLINE_SAVE_STATUS_FILE");
        if (string.IsNullOrWhiteSpace(path)) {
            return true;
        }

        try {
            string json = JsonSerializer.Serialize(new Dictionary<string, object?> {
                ["characterId"] = characterId,
                ["accountId"] = accountId,
                ["result"] = result,
                ["error"] = error,
                ["utc"] = DateTime.UtcNow.ToString("O"),
            });

            lock (WriteLock) {
                if (session > 0) {
                    if (result == Active) {
                        ActiveSession[characterId] = session;
                    } else if (ActiveSession.TryGetValue(characterId, out long active) && active > session) {
                        return true;
                    }
                }

                string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(folder)) {
                    Directory.CreateDirectory(folder);
                }
                string temp = Path.Combine(folder ?? string.Empty, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                try {
                    File.WriteAllText(temp, json);
                    File.Move(temp, path, overwrite: true);
                } finally {
                    if (File.Exists(temp)) {
                        File.Delete(temp);
                    }
                }
            }
            return true;
        } catch (Exception ex) {
            Logger.Error(ex, "Failed to write save status file {Path}", path);
            return false;
        }
    }
}
