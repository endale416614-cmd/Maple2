using System.Text.Json;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// DefensiveProperty damage-taken rates of additional effects (DL-03): Iron Shield 10100151 takes 25-80 % less damage, Iron
/// Defense 10100061 10 %. The ingest drops them, so tools/data/defensive_effects.py writes them from the client XML into
/// defensive-effects.json (DEFENSIVE_EFFECTS_FILE, default next to the solution); PvP-only rows are left out. Both modes.
/// shortcut: the ingest drops DefensiveProperty; move the rates into AdditionalEffectMetadataStatus with the next data rebuild.
/// </summary>
public static class DefensiveEffects {
    public sealed record Config(Dictionary<int, Dictionary<short, float[]>> Effects);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    /// <summary>The damage-taken rate of effect <paramref name="id"/> at <paramref name="level"/> for physical or magic hits
    /// (DefensiveProperty papDamageR / mapDamageR; 0.25 = 25 % less), or 0.</summary>
    public static float Rate(int id, short level, bool magic) {
        float[]? rates = LazyConfig.Value?.Effects.GetValueOrDefault(id)?.GetValueOrDefault(level);
        return rates is { Length: 2 } ? rates[magic ? 1 : 0] : 0;
    }

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("DEFENSIVE_EFFECTS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "defensive-effects.json");
        if (!File.Exists(path)) {
            Log.Logger.Warning("Damage-reduction file {Path} is missing; damage-reduction buffs do nothing", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (config != null) {
                Log.Logger.Information("Loaded damage-reduction rates of {Count} effects", config.Effects.Count);
            }
            return config;
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid damage-reduction file {Path}", path);
            return null;
        }
    }
}
