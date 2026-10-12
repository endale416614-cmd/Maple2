using System.Text.Json;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Save confirmation for the offline launcher. When OFFLINE_SAVE_STATUS_FILE is set, the file holds the latest
/// state of the player's save: "active" once the character is in the game world, then "committed" or "failed"
/// for the save made when the player leaves. Written atomically (temp file + move) so a reader never sees a partial file.
/// </summary>
public static class OfflineSaveStatus {
    public const string Active = "active";
    public const string Committed = "committed";
    public const string Failed = "failed";

    private static readonly ILogger Logger = Log.ForContext(typeof(OfflineSaveStatus));
    private static readonly object WriteLock = new();

    // Does nothing and returns true if the environment variable is not set.
    public static bool Write(long characterId, long accountId, string result, string? error) {
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
