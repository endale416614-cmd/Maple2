using System.Text.Json;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Summon lists of NPCs (NB-09): a summon node's npcId N is the Nth id (1-based) of the summoning NPC's list. The client keeps
/// the lists in its NPC XML (aiInfo/@summonNpcIDs) and the ingest drops them, so tools/data/summon_npcs.py writes them into
/// summon-npcs.json (SUMMON_NPCS_FILE, default next to the solution). Both modes.
/// Also carries stringIDs for npc_is_dead_by_string_id (NB-15).
/// Also carries skill probabilities for useNpcProb selects (DL-05).
/// Also carries races for npc_race conditions (DL-30).
/// Also carries the skills of usable NPCs, such as the Pirate Cannons (DL-52).
/// </summary>
public static class NpcSummons {
    public sealed record Config(Dictionary<int, int[]> Summons, Dictionary<int, string>? StringIds = null, Dictionary<int, int[]>? SkillProbs = null, Dictionary<int, string>? Races = null,
        Dictionary<int, int[]>? InteractSkills = null);

    private static readonly Lazy<Config?> LazyConfig = new(Load);

    public static Config? Parse(string json) => JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    public static IReadOnlyList<int>? For(int npcId) {
        var config = LazyConfig.Value;
        return config?.Summons?.GetValueOrDefault(npcId);
    }

    /// <summary>The client NPC XML's basic/@stringID (Gigantica's six phases share Gigantika_01), or null.</summary>
    public static string? StringId(int npcId) => LazyConfig.Value?.StringIds?.GetValueOrDefault(npcId);

    /// <summary>The client NPC XML's basic/@raceString (animal, undead, ...), or null.</summary>
    public static string? Race(int npcId) => LazyConfig.Value?.Races?.GetValueOrDefault(npcId);

    /// <summary>A usable NPC's interact/@interactFunction "UseSkill,id,level" and @interactCoolTime as [skill id, level,
    /// cooldown ms], or null.</summary>
    public static int[]? InteractSkill(int npcId) => LazyConfig.Value?.InteractSkills?.GetValueOrDefault(npcId);

    /// <summary>The NPC's skill probabilities in skill order (client NPC XML skill/@probs, two or more skills), or null.</summary>
    public static int[]? SkillProbs(int npcId) => LazyConfig.Value?.SkillProbs?.GetValueOrDefault(npcId);

    private static Config? Load() {
        string path = Environment.GetEnvironmentVariable("SUMMON_NPCS_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        if (!File.Exists(path)) {
            Log.Logger.Warning("Summon lists file {Path} is missing; summons do nothing", path);
            return null;
        }
        try {
            Config? config = Parse(File.ReadAllText(path));
            if (config != null) {
                Log.Logger.Information("Loaded summon lists for {Count} NPCs", config.Summons.Count);
            }
            return config;
        } catch (JsonException ex) {
            Log.Logger.Error(ex, "Invalid summon lists file {Path}", path);
            return null;
        }
    }
}