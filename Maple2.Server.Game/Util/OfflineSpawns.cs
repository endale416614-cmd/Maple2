using System.Text.Json;
using Maple2.Model.Metadata;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Spawn points the map data lacks although quests need their NPCs (both modes; an educated reconstruction of the missing
/// spawn data): offline-spawns.json (OFFLINE_SPAWNS_FILE, default next to the solution), each at a region spawn of its map.
/// </summary>
public static class OfflineSpawns {
    public sealed record Spawn(int MapId, int NpcId, int Count, int RegionId, int RegenSeconds, string? Note = null);
    public sealed record Config(Spawn[] Spawns);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Settings => LazyConfig.Value;

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_SPAWNS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-spawns.json");
        if (!File.Exists(path)) {
            Log.Logger.Information("No {Path}; no added spawn points", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (config != null) {
                Log.Logger.Information("Offline spawn points: {Count} spawns loaded", config.Spawns?.Length ?? 0);
            }
            return config == null ? null : config with { Spawns = config.Spawns ?? [] };
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid spawn points file {Path}; no extra spawns", path);
            return null;
        }
    }

    public static IEnumerable<Spawn> ForMap(int mapId) {
        return Settings?.Spawns.Where(spawn => spawn.MapId == mapId) ?? [];
    }

    public static SpawnPointNPC ToSpawnPoint(Spawn spawn, Ms2RegionSpawn region) =>
        new($"offline-spawn-{spawn.MapId}-{spawn.NpcId}-{spawn.RegionId}", 0, region.Position, region.Rotation, true, true, 150,
            [new SpawnPointNPCListEntry(spawn.NpcId, Math.Max(1, spawn.Count))], Math.Max(1, spawn.RegenSeconds), null);
}