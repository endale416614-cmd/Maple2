using System.Text.Json;
using Maple2.Model;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition grinding rates, OFF unless OFFLINE_GRIND=true (launcher.ini: GrindRates=true).
/// Read from offline-grind.json (path: OFFLINE_GRIND_FILE, default next to the solution): monster exp scaled to a
/// target number of kills per level, a chance multiplier for monster loot rolls, and level-based upgrade-material drops.
/// </summary>
public static class OfflineGrind {
    public sealed record UpgradeDrop(int ItemId, string Name, int MinLevel, int MaxLevel, float Chance, int Amount, float AmountPerLevel);
    public sealed record Config(int TargetKillsPerLevel, float MaxExpMultiplier, float DropRateMultiplier, float BossMultiplier, UpgradeDrop[] UpgradeDrops,
        float MesoMultiplier = 1);

    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_GRIND"), "true", StringComparison.OrdinalIgnoreCase);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Settings => Enabled ? LazyConfig.Value : null;

    /// <summary>Multiplier for the mesos monsters drop (1 when off or not set).</summary>
    public static float MesoRate => Settings?.MesoMultiplier is > 0 and var rate ? rate : 1f;

    /// <summary>Scales the meso drops among a monster's loot by <see cref="MesoRate"/>; other items are untouched.</summary>
    public static List<Item> ScaleMeso(List<Item> drops) {
        float rate = MesoRate;
        if (rate != 1f) {
            foreach (Item item in drops.Where(item => item.IsMeso())) {
                item.Amount = ScaledMeso(item.Amount, rate);
            }
        }
        return drops;
    }

    public static int ScaledMeso(int amount, float rate) => (int) Math.Clamp(Math.Round(amount * (double) rate), 1, int.MaxValue);

    /// <summary>Loot-roll chance multiplier for monster kills (1 when off).</summary>
    public static float DropRate => Math.Max(1f, Settings?.DropRateMultiplier ?? 1f);

    /// <summary>
    /// Exp multiplier for killing a monster of this level: the original exp table needs NextExp/ExpBase kills of
    /// same-level monsters per level; scale that down to TargetKillsPerLevel. Never below 1.
    /// </summary>
    public static float ExpMultiplier(ExpTable table, int monsterLevel) {
        Config? config = Settings;
        if (config == null || config.TargetKillsPerLevel <= 0) {
            return 1f;
        }

        if (!table.ExpBase.TryGetValue(2, monsterLevel, out long monsterExp) || monsterExp <= 0
            || !table.NextExp.TryGetValue(monsterLevel, out long nextExp) || nextExp <= 0) {
            return 1f;
        }

        return ExpMultiplier(nextExp, monsterExp, config.TargetKillsPerLevel, config.MaxExpMultiplier);
    }

    public static float ExpMultiplier(long nextExp, long monsterExp, int targetKills, float maxMultiplier) {
        float killsPerLevel = (float) nextExp / monsterExp;
        float multiplier = killsPerLevel / targetKills;
        return Math.Clamp(multiplier, 1f, Math.Max(1f, maxMultiplier));
    }

    /// <summary>The upgrade materials one kill drops (item id, amount).</summary>
    public static IEnumerable<(int ItemId, int Amount)> RollUpgradeDrops(int monsterLevel, bool boss, Random random) {
        Config? config = Settings;
        if (config == null) {
            yield break;
        }

        foreach (UpgradeDrop drop in config.UpgradeDrops) {
            if (monsterLevel < drop.MinLevel || (drop.MaxLevel > 0 && monsterLevel > drop.MaxLevel)) {
                continue;
            }
            if (random.NextSingle() >= drop.Chance) {
                continue;
            }

            int amount = Math.Max(1, (int) (drop.Amount + drop.AmountPerLevel * monsterLevel));
            if (boss) {
                amount = (int) Math.Max(1, amount * config.BossMultiplier);
            }
            yield return (drop.ItemId, amount);
        }
    }

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_GRIND_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json");
        if (!File.Exists(path)) {
            Log.Logger.Warning("Grind rates enabled but {Path} is missing; original rates apply", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (config != null) {
                Log.Logger.Information("Offline grind rates: {Kills} kills/level target, loot x{Drop}, meso x{Meso}, {Count} upgrade drops",
                    config.TargetKillsPerLevel, config.DropRateMultiplier, config.MesoMultiplier, config.UpgradeDrops?.Length ?? 0);
            }
            return config == null ? null : config with { UpgradeDrops = config.UpgradeDrops ?? [] };
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid grind rates file {Path}; original rates apply", path);
            return null;
        }
    }
}
