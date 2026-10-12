namespace Maple2.Database.Context;

/// <summary>
/// OFFLINE_MODE=true marks the single-player offline edition: one local player on this PC,
/// servers on loopback only, managed by the offline launcher.
/// </summary>
public static class OfflineMode {
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_MODE"), "true", StringComparison.OrdinalIgnoreCase);
}
