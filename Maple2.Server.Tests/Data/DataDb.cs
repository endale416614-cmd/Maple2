using System;
using System.IO;

namespace Maple2.Server.Tests.Data;

/// <summary>Locates a real maple-data.db for tests that read game data (read-only).</summary>
internal static class DataDb {
    /// <summary>MAPLE_DATA_DB (a file path, wins even when missing so a test can be forced to skip), then SQLITE_DATA_DIR / SQLITE_DIR, then SaveData/ up the directory tree.</summary>
    public static string? Find() {
        string? explicitPath = Environment.GetEnvironmentVariable("MAPLE_DATA_DB");
        if (!string.IsNullOrEmpty(explicitPath)) {
            return File.Exists(explicitPath) ? explicitPath : null;
        }
        foreach (string? dir in new[] { Environment.GetEnvironmentVariable("SQLITE_DATA_DIR"), Environment.GetEnvironmentVariable("SQLITE_DIR") }) {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "maple-data.db"))) {
                return Path.Combine(dir, "maple-data.db");
            }
        }
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "SaveData", "maple-data.db");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }
}
