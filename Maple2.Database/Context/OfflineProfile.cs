namespace Maple2.Database.Context;

/// <summary>
/// OFFLINE_PROFILE=Legacy|Adapted picks the default of every offline adaptation: Legacy keeps the original game rules,
/// Adapted (also when unset or unknown, so existing installs keep their behaviour) turns the solo-friendly ones on.
/// A per-option environment variable (OFFLINE_GRIND=true, ...) overrides the profile either way.
/// </summary>
public static class OfflineProfile {
    public static readonly bool Adapted = Resolve(Environment.GetEnvironmentVariable("OFFLINE_PROFILE"));

    /// <summary>Only "Legacy" (any case) selects the original rules.</summary>
    public static bool Resolve(string? modeValue) => !string.Equals(modeValue?.Trim(), "Legacy", StringComparison.OrdinalIgnoreCase);

    /// <summary>The option's own value when it parses as a bool, else the profile default (off under Legacy).</summary>
    public static bool Resolve(string? modeValue, string? flagValue, bool adaptedDefault) =>
        bool.TryParse(flagValue, out bool value) ? value : Resolve(modeValue) && adaptedDefault;

    public static bool Flag(string envVar, bool adaptedDefault) =>
        Resolve(Environment.GetEnvironmentVariable("OFFLINE_PROFILE"), Environment.GetEnvironmentVariable(envVar), adaptedDefault);

    /// <summary>An option that is the same in both modes: its own value when it parses as a bool, else the default.</summary>
    public static bool ResolveOption(string? flagValue, bool defaultValue) => bool.TryParse(flagValue, out bool value) ? value : defaultValue;

    public static bool Option(string envVar, bool defaultValue) => ResolveOption(Environment.GetEnvironmentVariable(envVar), defaultValue);
}
