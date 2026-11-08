using System.Text.Json;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// DefensiveProperty damage-taken rates of additional effects (DL-03): Iron Shield 10100151 takes 25-80 % less damage, Iron
/// Defense 10100061 10 %. The ingest drops them, so tools/data/defensive_effects.py writes them from the client XML into
/// defensive-effects.json (DEFENSIVE_EFFECTS_FILE, default next to the solution); PvP-only rows are left out. Both modes.
/// Invincible holds DefensiveProperty invincible per effect and level (NB-21); the ingest keeps it only as a flag.
/// TODO(DL-03): the ingest drops DefensiveProperty; move the rates and the invincible values into AdditionalEffectMetadataStatus with the next data rebuild.
/// </summary>
public static class DefensiveEffects {
    public sealed record Config(Dictionary<int, Dictionary<short, float[]>> Effects, Dictionary<int, Dictionary<short, int>>? Invincible = null);

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

    /// <summary>DefensiveProperty invincible of effect <paramref name="id"/> at <paramref name="level"/> (1, 2, 3 or 150), or 0.</summary>
    public static int Invincible(int id, short level) => LazyConfig.Value?.Invincible?.GetValueOrDefault(id)?.GetValueOrDefault(level) ?? 0;

    /// <summary>The hit a target holding these buffs takes at <paramref name="tick"/> in place of damage, or null (NB-21). As the GMS
    /// captures show: while an enabled buff has invincible 1 a hit by another actor is type 4 for 0, with 2 type 8 for 0; 3 and 150 are
    /// not enforced, no capture shows them enabled; 1 wins over 2, inferred: no capture has both. A buff past its end no longer counts,
    /// since one with interval 0 is removed about a second after its end (Buff.cs).</summary>
    public static DamageType? InvincibleHit(IEnumerable<Buff> buffs, long tick) {
        int[] values = buffs.Where(buff => buff.Enabled && (buff.EndTick <= buff.StartTick || tick <= buff.EndTick))
            .Select(buff => Invincible(buff.Id, buff.Level)).ToArray();
        return values.Contains(1) ? DamageType.Invincible : values.Contains(2) ? DamageType.Invincible2 : null;
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
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // The Lazy would keep a read error and throw it on every hit on a buffed target (BT-218), so an unreadable file counts as missing.
            Log.Logger.Error(ex, "Damage-reduction file {Path} could not be read; buffs reduce no damage and make no target invincible", path);
            return null;
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid damage-reduction file {Path}", path);
            return null;
        }
    }
}
