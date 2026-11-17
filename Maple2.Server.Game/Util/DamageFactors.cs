using System.Text.Json;
using Maple2.Model.Metadata;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// BL-01: the damage factors of DamageCalculator, a player's hits (4, FT-26a) and a monster's hits on players (2.8, FT-26b), read at start
/// from damage-factors.json (DAMAGE_FACTORS_FILE, default next to the solution). A missing or invalid file gives the constants' values and one log line.
/// </summary>
public static class DamageFactors {
    public sealed record Config(double PlayerFactor = Constant.AttackDamageFactor, double NpcFactor = Constant.NpcAttackDamageFactor);

    /// <summary>The factors in use; set by tests only.</summary>
    public static Config Current { get; set; } = Load();

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    public static bool Valid(Config? config) => config is { PlayerFactor: > 0 and < 1000, NpcFactor: > 0 and < 1000 };

    private static Config Load() {
        string path = Environment.GetEnvironmentVariable("DAMAGE_FACTORS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "damage-factors.json");
        if (!File.Exists(path)) {
            Log.Logger.Warning("Damage factors file {Path} is missing; the built-in factors apply", path);
            return new Config();
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (!Valid(config)) {
                Log.Logger.Error("Invalid damage factors file {Path}: both factors must be above 0; the built-in factors apply", path);
                return new Config();
            }
            Log.Logger.Information("Damage factors: player x{Player}, monsters x{Npc}", config!.PlayerFactor, config.NpcFactor);
            return config;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            Log.Logger.Error(ex, "Damage factors file {Path} could not be read; the built-in factors apply", path);
            return new Config();
        }
    }
}
