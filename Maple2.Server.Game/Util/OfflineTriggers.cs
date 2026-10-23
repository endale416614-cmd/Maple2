using System.Text.Json;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Map script patches for original-data gaps (both modes): offline-triggers.json (OFFLINE_TRIGGERS_FILE, default next to
/// the solution), each a text replacement in one script, applied when the script is parsed (TriggerCache).
/// </summary>
public static class OfflineTriggers {
    public sealed record Patch(string XBlock, string Trigger, string Find, string Replace, string? Note = null);
    public sealed record Config(Patch[] Patches);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_TRIGGERS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-triggers.json");
        if (!File.Exists(path)) {
            Log.Logger.Information("No {Path}; map scripts are not patched", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            // Apply runs on every script load: an incomplete entry is skipped.
            return config == null ? null : config with {
                Patches = (config.Patches ?? []).Where(patch => patch is { XBlock: not null, Trigger: not null, Find.Length: > 0, Replace: not null }).ToArray(),
            };
        } catch (Exception ex) {
            // Never rethrown: the Lazy would keep the exception and every script load would fail.
            Log.Logger.Error(ex, "Invalid map script patches file {Path}; map scripts are not patched", path);
            return null;
        }
    }

    /// <summary>The script with this file's patches for it applied; a patch whose Find is missing logs a warning and changes nothing.</summary>
    public static string Apply(string xBlock, string trigger, string xml, Config? config = null) {
        config ??= LazyConfig.Value;
        foreach (Patch patch in config?.Patches ?? []) {
            if (!patch.XBlock.Equals(xBlock, StringComparison.OrdinalIgnoreCase) || !patch.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!xml.Contains(patch.Find, StringComparison.Ordinal)) {
                Log.Logger.Warning("Trigger patch for {XBlock}/{Trigger} does not match: {Find}", xBlock, trigger, patch.Find);
                continue;
            }
            xml = xml.Replace(patch.Find, patch.Replace, StringComparison.Ordinal);
        }
        return xml;
    }
}
