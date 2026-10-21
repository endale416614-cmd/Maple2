using System.Text.Json;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Reverse Fair Fight (docs/engineering/RFF_DESIGN.md): a contextual damage floor for a solo player in Adapted mode. A
/// direct hit on a boss or a group-dungeon monster is raised to a floor derived from the target's max HP, a target clear
/// time and the attack point's own rate; damage above the floor is untouched. Off under Legacy (OFFLINE_FAIR_FIGHT=false
/// turns it off in Adapted too), with more than one player in the field, for misses and blocks, and while the target is
/// invincible or shielded. Damage over time and scripted damage never reach it (the hook sits after CalculateDamage in
/// Actor.ApplyDamage only). Tunables: offline-rff.json (OFFLINE_RFF_FILE, default next to the solution), all optional.
/// </summary>
public static class OfflineFairFight {
    public sealed record ClearTime(double ClearSeconds);
    public sealed record ClassSetting(int PartySize, double ClearSeconds);
    public sealed record Config(double ReferenceRatePerSecond = 2.5, double FloorShare = 1.0, double MaxHitShare = 0.02,
        double TimerShare = 0.8, Dictionary<string, ClearTime>? GroupTypes = null, Dictionary<string, ClassSetting>? Classes = null,
        Dictionary<string, ClearTime>? Dungeons = null, bool LogFlooredHits = false);

    /// <summary>The target's encounter as plain values (read from the field and the NPC), so the rule is testable.</summary>
    public readonly record struct Encounter(bool InDungeon, string GroupType, int DungeonId, long DurationTick, bool IsBoss, int NpcClass);

    /// <summary>RFF_DESIGN 3.5 defaults: raid 600 s, chaosRaid 900, guildRaid 900, worldBossDungeon 600, event 600; open-field
    /// bosses by class: 4 -> (2, 300), 6 -> (4, 600). Classes 3 and 7 are meant for one player and get no floor unless the
    /// file lists them. A file's values replace these per key.</summary>
    public static readonly Config Defaults = new(
        GroupTypes: new Dictionary<string, ClearTime> {
            ["raid"] = new(600), ["chaosRaid"] = new(900), ["guildRaid"] = new(900), ["worldBossDungeon"] = new(600), ["event"] = new(600),
        },
        Classes: new Dictionary<string, ClassSetting> {
            ["4"] = new(2, 300), ["6"] = new(4, 600),
        });

    public static readonly bool Enabled = OfflineProfile.Flag("OFFLINE_FAIR_FIGHT", true);

    private static readonly Lazy<Config> LazyConfig = new(Load);

    public static Config Settings => LazyConfig.Value;

    /// <summary>Seconds a solo player should need, or null when the encounter gets no floor. In a dungeon:
    /// Dungeons[id], else GroupTypes[GroupType], else null (normal dungeons are off unless the file lists "normal");
    /// capped at DurationTick / 1000 * TimerShare when DurationTick > 0. In the open field: Classes[NpcClass] for a boss,
    /// else null.</summary>
    public static double? ClearSeconds(Encounter encounter, Config config) {
        double? seconds;
        if (encounter.InDungeon) {
            seconds = config.Dungeons?.GetValueOrDefault(encounter.DungeonId.ToString())?.ClearSeconds
                      ?? config.GroupTypes?.GetValueOrDefault(encounter.GroupType)?.ClearSeconds;
            if (seconds is > 0 && encounter.DurationTick > 0) {
                seconds = Math.Min(seconds.Value, encounter.DurationTick / 1000.0 * config.TimerShare);
            }
        } else {
            seconds = encounter.IsBoss ? config.Classes?.GetValueOrDefault(encounter.NpcClass.ToString())?.ClearSeconds : null;
        }
        return seconds is > 0 ? seconds : null;
    }

    /// <summary>The floor of one attack point: maxHp / clearSeconds / ReferenceRatePerSecond * rate * FloorShare, at most
    /// maxHp * MaxHitShare; 0 when any input is 0 or less.</summary>
    public static double FloorHit(double maxHp, double clearSeconds, double rate, Config config) {
        if (maxHp <= 0 || clearSeconds <= 0 || rate <= 0 || config.ReferenceRatePerSecond <= 0) {
            return 0;
        }
        double floor = maxHp / clearSeconds / config.ReferenceRatePerSecond * rate * config.FloorShare;
        return Math.Min(floor, maxHp * config.MaxHitShare);
    }

    /// <summary>Normal and Critical hits above 0 rise to the floor (rounded up); every other result, and every hit at or
    /// above the floor, stays as calculated. Never lowers a hit.</summary>
    public static long Apply(DamageType type, long calculated, double floorHit) {
        if (type is not (DamageType.Normal or DamageType.Critical) || calculated <= 0 || floorHit <= calculated) {
            return calculated;
        }
        return (long) Math.Ceiling(floorHit);
    }

    /// <summary>The whole rule on plain values: returns calculated unless enabled, playersInField == 1, the target is
    /// neither invincible nor shielded, and ClearSeconds is not null.</summary>
    public static long Floor(bool enabled, int playersInField, bool targetInvincible, bool targetShielded, Encounter encounter,
        double maxHp, double rate, DamageType type, long calculated, Config config) {
        if (!enabled || playersInField != 1 || targetInvincible || targetShielded) {
            return calculated;
        }
        double? clearSeconds = ClearSeconds(encounter, config);
        return clearSeconds == null ? calculated : Apply(type, calculated, FloorHit(maxHp, clearSeconds.Value, rate, config));
    }

    /// <summary>For Actor.ApplyDamage (RFF-02): caster must be a FieldPlayer and target a FieldNpc with Friendly == 0.
    /// Reads the encounter first and returns when it gets no floor (most hits), then the live player count (lobby plus
    /// rooms in a dungeon), invincible/shield buffs, max HP and the rate, then the overload above with Enabled and
    /// Settings. With LogFlooredHits, one Information line per hit on a floored encounter, raised or not. Never throws:
    /// an error keeps the calculated damage.</summary>
    public static long Floor(IActor caster, IActor target, DamagePropertyRecord property, DamageType type, long calculated) {
        if (!Enabled || caster is not FieldPlayer || target is not FieldNpc npc || npc.Value.Metadata.Basic.Friendly > 0) {
            return calculated;
        }

        try {
            Config config = Settings;
            var dungeon = target.Field as DungeonFieldManager;
            Encounter encounter = dungeon != null
                ? new Encounter(true, dungeon.DungeonMetadata.GroupType.ToString(), dungeon.DungeonMetadata.Id,
                    dungeon.DungeonMetadata.DurationTick, npc.Value.IsBoss, npc.Value.Metadata.Basic.Class)
                : new Encounter(false, "", 0, 0, npc.Value.IsBoss, npc.Value.Metadata.Basic.Class);
            if (ClearSeconds(encounter, config) == null) {
                return calculated;
            }

            int players;
            if (dungeon != null) {
                DungeonFieldManager lobby = dungeon.Lobby ?? dungeon;
                players = lobby.Players.Count + lobby.RoomFields.Values.Sum(room => room.Players.Count);
            } else {
                players = target.Field.Players.Count;
            }

            bool invincible = false, shielded = false;
            foreach (Buff buff in target.Buffs.EnumerateBuffs()) {
                invincible |= buff.Metadata.Status.Invincible;
                shielded |= buff.Metadata.Shield != null;
            }

            double maxHp = target.Stats.Values[BasicAttribute.Health].Total;
            long result = Floor(true, players, invincible, shielded, encounter, maxHp, property.Rate, type, calculated, config);
            if (config.LogFlooredHits) {
                Log.Logger.Information("Fair fight: npc {NpcId} skill {SkillId} {Encounter} players {Players} rate {Rate} " +
                    "calculated {Calculated} result {Result} raised {Raised} hp {Hp} of {MaxHp}", npc.Value.Metadata.Id, property.SkillId,
                    encounter.InDungeon ? $"dungeon {encounter.DungeonId} {encounter.GroupType}" : $"field class {encounter.NpcClass}",
                    players, property.Rate, calculated, result, result != calculated,
                    target.Stats.Values[BasicAttribute.Health].Current, (long) maxHp); // hp before this hit (scenario 7 holds)
            }
            return result;
        } catch (Exception ex) {
            // The buff list can change on the field thread while this runs on the session thread; RFF never lowers a hit.
            Log.Logger.Warning(ex, "Fair fight skipped for npc {NpcId}", npc.Value.Metadata.Id);
            return calculated;
        }
    }

    /// <summary>offline-rff.json text to Config: camelCase keys as in RFF_DESIGN 3.5, missing keys keep the defaults,
    /// dictionary entries are merged over the default dictionaries. Throws JsonException on invalid JSON.</summary>
    public static Config Parse(string json) {
        Config parsed = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new JsonException("offline-rff.json is empty");
        return parsed with {
            GroupTypes = Merge(Defaults.GroupTypes, parsed.GroupTypes),
            Classes = Merge(Defaults.Classes, parsed.Classes),
            Dungeons = Merge(Defaults.Dungeons, parsed.Dungeons),
        };
    }

    private static Dictionary<string, T> Merge<T>(Dictionary<string, T>? defaults, Dictionary<string, T>? overrides) {
        var merged = new Dictionary<string, T>(defaults ?? new Dictionary<string, T>());
        foreach ((string key, T value) in overrides ?? new Dictionary<string, T>()) {
            merged[key] = value;
        }
        return merged;
    }

    private static Config Load() {
        string path = Environment.GetEnvironmentVariable("OFFLINE_RFF_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-rff.json");
        if (!File.Exists(path)) {
            return Defaults; // the defaults are the design
        }
        try {
            Config config = Parse(File.ReadAllText(path));
            Log.Logger.Information("Fair fight: {Path} loaded (reference rate {Rate}/s, floor share {Share}, max hit {Max} of HP)",
                path, config.ReferenceRatePerSecond, config.FloorShare, config.MaxHitShare);
            return config;
        } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
            Log.Logger.Error(ex, "Invalid fair fight file {Path}; the defaults apply", path);
            return Defaults;
        }
    }
}
