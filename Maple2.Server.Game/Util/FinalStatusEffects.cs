using System.Text.Json;
using Maple2.Model.Enum;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// FinalStatusProperty rates and values of additional effects (PI-09, BT-264): Shadow Power 70000027 raises max health by 1150 %.
/// The ingest drops them, so tools/data/final_status_effects.py writes them from the client XML into final-status-effects.json
/// (FINAL_STATUS_EFFECTS_FILE, default next to the solution), with the effects that apply only in PvP zones (applyPvPZoneType 2,
/// BT-266). A rate is added to the stat after the buffs' rates and values, gear and conversions are in: total += total * rate.
/// TODO(PI-09): the ingest drops FinalStatusProperty and applyPvPZoneType; move them into AdditionalEffectMetadata with the next data rebuild.
/// </summary>
public static class FinalStatusEffects {
    public sealed record Stat(Dictionary<string, float>? Rates = null, Dictionary<string, float>? Values = null);

    public sealed record Config(Dictionary<int, Dictionary<short, Stat>> Effects, Dictionary<int, short[]>? PvpOnly = null);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    /// <summary>The final rates and values of effect <paramref name="id"/> at <paramref name="level"/>, per attribute, or null.</summary>
    public static Stat? Get(int id, short level) => LazyConfig.Value?.Effects.GetValueOrDefault(id)?.GetValueOrDefault(level);

    /// <summary>The final rates and values of effect <paramref name="id"/> at <paramref name="level"/> as attributes (an unknown name is skipped).</summary>
    public static IEnumerable<(BasicAttribute Attribute, float Rate, float Value)> Attributes(int id, short level) {
        Stat? stat = Get(id, level);
        if (stat == null) {
            yield break;
        }
        IEnumerable<string> names = (stat.Rates?.Keys ?? Enumerable.Empty<string>()).Union(stat.Values?.Keys ?? Enumerable.Empty<string>());
        foreach (string name in names) {
            if (Enum.TryParse(name, out BasicAttribute attribute)) {
                yield return (attribute, stat.Rates?.GetValueOrDefault(name) ?? 0, stat.Values?.GetValueOrDefault(name) ?? 0);
            }
        }
    }

    /// <summary>Whether effect <paramref name="id"/> at <paramref name="level"/> applies only in PvP zones (applyPvPZoneType 2).</summary>
    public static bool IsPvpOnly(int id, short level) => LazyConfig.Value?.PvpOnly?.GetValueOrDefault(id)?.Contains(level) ?? false;

    /// <summary>Whether a map of this type is a player-kill zone for PvP-only effects: PvP maps and the Shadow World maps (Calamitysnare, Savage Crossroads).</summary>
    public static bool IsPvpZone(MapType type) => type is MapType.Pvp or MapType.Shadow;

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("FINAL_STATUS_EFFECTS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "final-status-effects.json");
        if (!File.Exists(path)) {
            Log.Logger.Warning("Final-status file {Path} is missing; final stat rates do nothing", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (config is { Effects: null }) {
                Log.Logger.Error("Invalid final-status file {Path}: it has no Effects", path);
                return null;
            }
            if (config != null) {
                Log.Logger.Information("Loaded final stat rates of {Count} effects", config.Effects.Count);
            }
            return config;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Log.Logger.Error(ex, "Final-status file {Path} could not be read; final stat rates do nothing", path);
            return null;
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid final-status file {Path}", path);
            return null;
        }
    }
}
