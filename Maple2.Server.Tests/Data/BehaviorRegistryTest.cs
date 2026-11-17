using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Data report test that writes the behaviour registry (NB-08): one CSV row per NPC with an AI (boss, monster or friendly NPC)
/// naming the AI steps the server does not run yet, a status (blocked / degraded / ok), its attack rows by the wave 3 rule they
/// fire by (NB-28), and a summary. The waves of the
/// behaviour restoration lower its counts. It also writes the fidelity registry (FT-04), whose counts are gated like the
/// data sweep's errors (SW-01): one above its threshold fails the test; the behaviour registry's findings never do.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class BehaviorRegistryTest {
    // AI steps that do nothing in AiState (2026-10-22). A name leaves its set when its behaviour is implemented.
    // Blocking: boss phases stop. Summons and the master/slave values left with NB-10 and NB-12; the op form of the slave
    // count condition left with NB-16.
    private static readonly HashSet<string> Blocking = [];
    // Degrading: the encounter runs without them. ShowVibrate and HideVibrateAll left with FT-18.
    private static readonly HashSet<string> Degrading = [
        "RideNode", "CreateRandomRoomNode", "CreateInteractObjectNode",
    ];

    // FT-04: the fidelity counts on the 1.2.2 data, the same on the base and the pack DBs; a count above its threshold fails the test.
    // A change that lowers a count lowers its threshold. FT-07: the counts are of what a player can meet, so a threshold also drops when the
    // count's rule narrows (an NPC placed or summoned, a node in an AI an NPC runs, a script with a map row, a no-op not on the on-purpose list);
    // the 1.2.2 values were 3012 stub calls, 28 empty node uses, 310 NPCs without an AI file and 5 tables never read.
    private static readonly Dictionary<string, int> Thresholds = new() {
        ["Trigger stub calls"] = 2811, ["Empty AI node uses"] = 3, ["NPCs without an AI file"] = 54, ["RecvOps without a handler"] = 7,
        ["SendOps never sent"] = 29, ["Tables never read"] = 3, ["Condition types never reported"] = 99,
    };

    /// <summary>Every entry in the trees of <paramref name="ai"/> (reserved, battle, battle end, presets), nested ones too.</summary>
    public static IEnumerable<AiMetadata.Entry> AllEntries(AiMetadata ai) {
        static IEnumerable<AiMetadata.Entry> Walk(AiMetadata.Entry entry) {
            yield return entry;
            if (entry is AiMetadata.Node node) {
                foreach (AiMetadata.Entry child in node.Entries.SelectMany(Walk)) {
                    yield return child;
                }
            }
            // A conditions node keeps steps under its conditions too (AiState.ProcessNode runs them).
            foreach (AiMetadata.Condition condition in (entry as AiMetadata.ConditionsNode)?.Conditions ?? []) {
                foreach (AiMetadata.Entry child in Walk(condition)) {
                    yield return child;
                }
            }
        }

        return ai.Reserved.Concat(ai.Battle).Concat(ai.BattleEnd).Concat(ai.AiPresets).SelectMany(Walk);
    }

    /// <summary>Counts of the unsupported AI steps in every tree of <paramref name="ai"/> (reserved, battle, battle end, presets).</summary>
    public static Dictionary<string, int> Unsupported(AiMetadata ai) {
        var counts = new Dictionary<string, int>();
        foreach (string name in AllEntries(ai).Select(entry => entry.GetType().Name).Where(name => Blocking.Contains(name) || Degrading.Contains(name))) {
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }
        return counts;
    }

    /// <summary>
    /// FT-07: the NPCs a player can meet: those <paramref name="placed"/> by the maps, the quests' fallback or offline-spawns.json, and the NPCs
    /// the summon nodes of their AIs name, and those NPCs' summons in turn. <paramref name="aiOf"/> gives the AI an NPC runs (null: none);
    /// a summon node's npcId is an index into the summoner's list, <paramref name="summonsOf"/> (SummonRules.Resolve, as AiState reads it).
    /// </summary>
    public static HashSet<int> WithSummons(IEnumerable<int> placed, Func<int, AiMetadata?> aiOf, Func<int, IReadOnlyList<int>?> summonsOf) {
        var meetable = new HashSet<int>(placed);
        var pending = new Queue<int>(meetable);
        while (pending.TryDequeue(out int id)) {
            if (aiOf(id) is not { } ai) {
                continue;
            }
            foreach (int summoned in AllEntries(ai).OfType<AiMetadata.SummonNode>().Select(node => SummonRules.Resolve(summonsOf(id), node.NpcId)).OfType<int>()) {
                if (meetable.Add(summoned)) {
                    pending.Enqueue(summoned);
                }
            }
        }
        return meetable;
    }

    /// <summary>The AI each NPC runs, as AiState finds it: the default AI (null), a file of the data, or the NB-05 substitute for a missing path.</summary>
    public static Func<int, AiMetadata?> AiLookup(IReadOnlyDictionary<string, AiMetadata> ais, IReadOnlyDictionary<int, string> aiPathByNpc) {
        string[] names = ais.Keys.ToArray();
        return id => {
            if (!aiPathByNpc.TryGetValue(id, out string? path) || path == "AI_DefaultNew.xml") {
                return null;
            }
            if (ais.TryGetValue(path, out AiMetadata? ai)) {
                return ai;
            }
            return AiMetadataStorage.Substitute(path, names) is { } substitute ? ais[substitute] : null;
        };
    }

    // FT-07: trigger functions that do nothing on purpose; they stay out of "Trigger stub calls" and are listed in the registry as such.
    // A name is here only with its reason, and only while the TriggerContext method still does nothing (OnPurposeNoOps_AreStubsWithAReason).
    private const string OnlineOnly = "an online-only system (PvP, guild vs guild) that stays closed in both modes (COOP_DESIGN.md F-3)";
    public static readonly Dictionary<(string Kind, string Name), string> OnPurposeNoOps = new() {
        [("action", "set_pvp_zone")] = OnlineOnly,
        [("condition", "pvp_zone_ended")] = OnlineOnly,
        [("action", "guild_vs_game_end_game")] = OnlineOnly,
        [("action", "guild_vs_game_give_contribution")] = OnlineOnly,
        [("action", "guild_vs_game_give_reward")] = OnlineOnly,
        [("action", "guild_vs_game_log_result")] = OnlineOnly,
        [("action", "guild_vs_game_log_won_by_default")] = OnlineOnly,
        [("action", "guild_vs_game_result")] = OnlineOnly,
        [("action", "guild_vs_game_score_by_user")] = OnlineOnly,
        [("action", "set_user_value_from_guild_vs_game_score")] = OnlineOnly,
        [("condition", "guild_vs_game_scored_team")] = OnlineOnly,
        [("condition", "guild_vs_game_winner_team")] = OnlineOnly,
    };

    // NB-28: the NPC attack rows the wave 3 rules reach, of those that deal damage or carry skills. Keyless: no key of the motion's
    // sequence names the point, so it fires at the sequence's end (NB-19, NB-19b). MissingSequence: the NPC's model lacks the
    // sequence, so it fires at once (NB-19). CubeSplash: a cube path's splash, placed around each target the cast takes (NB-22c,
    // NB-23); CubeSplashSensor: one whose region waits for a sensor (NB-27).
    private static readonly string[] AttackKinds = ["Keyless", "MissingSequence", "CubeSplash", "CubeSplashSensor"];

    /// <summary>The attack rows of <paramref name="skill"/> by AttackKinds, on a model whose sequences are <paramref name="animation"/>
    /// (null: it has none), with the region skills <paramref name="region"/> finds and the cube paths <paramref name="paths"/> holds.</summary>
    public static Dictionary<string, int> AttackRows(SkillMetadata skill, AnimationMetadata? animation, Func<int, short, SkillMetadata?> region, IReadOnlyDictionary<long, IReadOnlyList<MagicPath>> paths) {
        var counts = new Dictionary<string, int>();
        void Count(string kind) => counts[kind] = counts.GetValueOrDefault(kind) + 1;
        foreach (SkillMetadataMotion motion in skill.Data.Motions) {
            AnimationSequenceMetadata? sequence = animation?.Sequences.GetValueOrDefault(motion.MotionProperty.SequenceName);
            HashSet<byte> keyless = MovementState.KeylessAttackPoints(motion, sequence).ToHashSet();
            for (int point = 0; point < motion.Attacks.Length; point++) {
                SkillMetadataAttack attack = motion.Attacks[point];
                if (attack.Damage.Count == 0 && attack.Skills.Length == 0) {
                    continue;
                }
                if (keyless.Contains((byte) point)) {
                    Count(sequence == null ? "MissingSequence" : "Keyless");
                }
                // SkillState.PlaceCubePath's rule, whatever the attack's IncludeCaster: each splash effect whose region skill is found
                // places, unless its region waits for a sensor.
                // A path with no move gives CubePoints nothing to place at (magicpath.xml 50000324, Madonette's p11).
                if (attack.CubeMagicPathId == 0 || attack.MagicPathId != 0 || paths.GetValueOrDefault(attack.CubeMagicPathId) is not { Count: > 0 }) {
                    continue;
                }
                bool[] places = attack.Skills.Where(effect => effect.Splash != null && effect.Skills.Length > 0)
                    .Select(effect => region(effect.Skills[0].Id, effect.Skills[0].Level) is { } placed ? SkillRecord.PlacesCubeRegion(placed.Data.Sensor, effect.Splash!) : (bool?) null)
                    .OfType<bool>().ToArray();
                if (places.Contains(true)) {
                    Count("CubeSplash");
                }
                if (places.Contains(false)) {
                    Count("CubeSplashSensor");
                }
            }
        }
        return counts;
    }

    // The IL opcodes by value, to read a method's body.
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode) field.GetValue(null)!).ToDictionary(op => op.Value);

    /// <summary>The methods and fields the IL of <paramref name="method"/> names, in order.</summary>
    private static IEnumerable<MemberInfo> Members(MethodBase method) {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int i = 0; i < il.Length;) {
            OpCode op = OpCodesByValue[il[i] == 0xFE ? unchecked((short) (0xFE00 | il[i + 1])) : il[i]];
            i += op.Size;
            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField) {
                yield return method.Module.ResolveMember(BitConverter.ToInt32(il, i), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
            }
            i += op.OperandType switch {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }

    /// <summary>
    /// Whether a TriggerContext method does nothing: it calls ErrorLog (which logs "not implemented offline"), or it names no method or
    /// field but DebugLog, WarnLog, TraceEnabled and Array.Empty (an empty body, or an answer from its arguments alone).
    /// </summary>
    public static bool DoesNothing(MethodBase method) {
        List<MemberInfo> members = Members(method).ToList();
        bool Log(MemberInfo member, params string[] names) => member.DeclaringType == typeof(TriggerContext) && names.Contains(member.Name);
        return members.Any(member => Log(member, "ErrorLog"))
               || members.All(member => Log(member, "DebugLog", "WarnLog", "TraceEnabled") || member.DeclaringType == typeof(Array) && member.Name == "Empty");
    }

    /// <summary>The TriggerContext method a trigger function runs: its factory builds an action or a condition, whose Execute or Evaluate calls it.</summary>
    public static MethodInfo? ContextMethod(Delegate factory) {
        Type? built = Members(factory.Method).OfType<ConstructorInfo>().Select(ctor => ctor.DeclaringType!)
            .FirstOrDefault(type => typeof(IAction).IsAssignableFrom(type) || typeof(ICondition).IsAssignableFrom(type));
        MethodInfo? run = built?.GetMethod(nameof(IAction.Execute)) ?? built?.GetMethod(nameof(ICondition.Evaluate));
        return run == null ? null : Members(run).OfType<MethodInfo>().FirstOrDefault(method => method.DeclaringType == typeof(TriggerContext));
    }

    private static void Empty() { }
    private static bool FromArguments(int score) => score > 0;
    private static string Calls(string text) => text.Trim();

    [Test]
    public void Walk_CountsNestedEntries() {
        var tree = new AiMetadata.Node("battle", [
            new AiMetadata.RideNode("ride", [], default, false, []),
            new AiMetadata.Node("select", [new AiMetadata.RideNode("ride", [new AiMetadata.CreateRandomRoomNode("room", [], 1, 0)], default, false, [])]),
            new AiMetadata.ConditionsNode("conditions", [], [new AiMetadata.Condition("true", [new AiMetadata.CreateRandomRoomNode("room", [], 1, 0)])], 0, 0, false),
            new AiMetadata.ShowVibrateNode("vibrate", [], 1),
        ]);

        Dictionary<string, int> counts = Unsupported(new AiMetadata("test", [], [tree], [], []));
        Assert.That(counts, Is.EquivalentTo(new Dictionary<string, int> { ["RideNode"] = 2, ["CreateRandomRoomNode"] = 2 }));
    }

    // NB-28: p0 hits at its key and places its splash, p1 has no key, p2 has none and places one splash while its other region waits
    // for a sensor, p3 deals nothing and carries no skill; the attacks exclude their caster, and still place around their targets
    // (NB-23). The second motion's sequence is not on the model, and without the model's sequences every row is missing.
    [Test]
    public void AttackRows_CountEachRowByTheRuleItFiresBy() {
        var range = new SkillMetadataRange(SkillRegion.Box, 0, 0, 0, 0, 0, default, default, IncludeCasterType.Exclude, ApplyTargetType.Friendly, SkillTargetType.None);
        SkillEffectMetadata Splash(int region) => new(1, [new SkillEffectMetadata.Skill(region, 1)], null, new SkillEffectMetadataSplash(900, 0, 0, false, false, false, false, false, false, null));
        SkillMetadataAttack Attack(string point, int damage, params SkillEffectMetadata[] skills) => new(point, 0, 1, 0, skills.Length > 0 ? 600280511 : 0, 0, 0, [], null,
            range, null!, new SkillMetadataDamage(damage, 1, 1, 0, false, 0, 0, false, null), skills, []);
        SkillMetadataMotion Motion(string sequence, params SkillMetadataAttack[] attacks) =>
            new(new SkillMetadataMotionProperty(sequence, 1, 0, 0, true, 0), attacks, new Dictionary<string, byte>());
        SkillMetadata Skill(int id, SkillMetadataSensor? sensor, params SkillMetadataMotion[] motions) =>
            new(id, 1, "", null!, null!, new SkillMetadataLevel(null!, null, null, null!, null!, sensor, null!, [], motions));
        SkillMetadata floor = Skill(2, null), mine = Skill(3, new SkillMetadataSensor(null!, 0, false));
        SkillMetadata? Region(int id, short level) => id == 2 ? floor : id == 3 ? mine : null;
        SkillMetadata cast = Skill(1, null,
            Motion("Attack_01_A", Attack("p0", 1, Splash(2)), Attack("p1", 1), Attack("p2", 1, Splash(2), Splash(3)), Attack("p3", 0)),
            Motion("Attack_02_A", Attack("p0", 1)));
        var model = new AnimationMetadata("model", new Dictionary<string, AnimationSequenceMetadata> {
            ["Attack_01_A"] = new("Attack_01_A", 0, 1f, [new AnimationKey("p0", 0.4f), new AnimationKey("end", 1f)]),
        });
        var paths = new Dictionary<long, IReadOnlyList<MagicPath>> { [600280511] = [null!] };

        Assert.That(AttackRows(cast, model, Region, paths), Is.EquivalentTo(new Dictionary<string, int> {
            ["Keyless"] = 2, ["MissingSequence"] = 1, ["CubeSplash"] = 2, ["CubeSplashSensor"] = 1,
        }));
        Assert.That(AttackRows(cast, null, Region, paths), Is.EquivalentTo(new Dictionary<string, int> {
            ["MissingSequence"] = 4, ["CubeSplash"] = 2, ["CubeSplashSensor"] = 1,
        }));
        // A path the table lacks, or one with no move, places nothing.
        Assert.That(AttackRows(cast, model, Region, new Dictionary<long, IReadOnlyList<MagicPath>> { [600280511] = [] }), Is.EquivalentTo(new Dictionary<string, int> { ["Keyless"] = 2, ["MissingSequence"] = 1 }));
    }

    [Test]
    public void WithSummons_FollowsTheChainOfSummonNodes() {
        // A summon node's npcId is the 1-based index into its summoner's list.
        AiMetadata Summoning(params int[] indexes) => new("ai", [], [new AiMetadata.Node("battle", [
            .. indexes.Select(index => new AiMetadata.SummonNode("summon", [], index, 1, 1, 0, 0, default, default, default, default, default, 0, default, [], 0, false)),
        ])], [], []);
        var ais = new Dictionary<int, AiMetadata> { [1] = Summoning(2), [2] = Summoning(1, 3), [4] = Summoning(1) };
        var lists = new Dictionary<int, int[]> { [1] = [7, 2], [2] = [3, 1], [4] = [5] };

        HashSet<int> meetable = WithSummons([1, 9], id => ais.GetValueOrDefault(id), id => lists.GetValueOrDefault(id));

        Assert.That(meetable, Is.EquivalentTo(new[] { 1, 2, 3, 9 }), "placed 1 summons its entry 2 (NPC 2), which summons entries 1 and 3 (NPC 3, and none); 4 and 5 are never placed");
    }

    [Test]
    public void AiLookup_FindsFileSubstituteOrDefault() {
        var file = new AiMetadata("Npc/AI_Boss.xml", [], [], [], []);
        var ais = new Dictionary<string, AiMetadata>(StringComparer.OrdinalIgnoreCase) { [file.Name] = file };
        Func<int, AiMetadata?> aiOf = AiLookup(ais, new Dictionary<int, string> {
            [1] = "Npc/AI_Boss.xml", [2] = "Other/AI_Boss.xml", [3] = "AI_DefaultNew.xml", [4] = "Npc/AI_Missing.xml",
        });

        Assert.That(aiOf(1), Is.SameAs(file));
        Assert.That(aiOf(2), Is.SameAs(file), "a missing path takes the one file of that name");
        Assert.That(aiOf(3), Is.Null);
        Assert.That(aiOf(4), Is.Null);
        Assert.That(aiOf(5), Is.Null, "an NPC with no AI path");
    }

    [Test]
    public void OnPurposeNoOps_AreStubsWithAReason() {
        Assert.That(OnPurposeNoOps.Where(pair => pair.Value.Length < 20).Select(pair => pair.Key), Is.Empty, "each name has its reason");
        foreach (((string kind, string name), _) in OnPurposeNoOps) {
            Delegate? factory = kind == "action" ? TriggerFunctionMapping.ActionMap.GetValueOrDefault(name) : TriggerFunctionMapping.ConditionMap.GetValueOrDefault(name);
            Assert.That(factory, Is.Not.Null, $"{kind} {name} is a trigger function");
            Assert.That(ContextMethod(factory!) is { } method && DoesNothing(method), Is.True, $"{kind} {name} still does nothing; take it off the list when it works");
        }
    }

    [Test]
    public void DoesNothing_OnlyLogsOrAnswersFromItsArguments() {
        MethodInfo Local(string name) => typeof(BehaviorRegistryTest).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.That(DoesNothing(Local(nameof(Empty))), Is.True);
        Assert.That(DoesNothing(Local(nameof(FromArguments))), Is.True);
        Assert.That(DoesNothing(Local(nameof(Calls))), Is.False);
        // SetPvpZone only logs that it is not implemented offline (PvP is not offered, F-3); DungeonFail ends the run.
        Assert.That(DoesNothing(typeof(TriggerContext).GetMethod(nameof(TriggerContext.SetPvpZone))!), Is.True);
        Assert.That(DoesNothing(typeof(TriggerContext).GetMethod(nameof(TriggerContext.DungeonFail))!), Is.False);
    }

    [Test]
    public void ContextMethod_FindsTheCallOfEveryTriggerFunction() {
        Assert.That(ContextMethod(TriggerFunctionMapping.ActionMap["dungeon_variable"])?.Name, Is.EqualTo(nameof(TriggerContext.SetDungeonVariable)));
        Assert.That(ContextMethod(TriggerFunctionMapping.ConditionMap["dungeon_variable"])?.Name, Is.EqualTo(nameof(TriggerContext.DungeonVariable)));
        Assert.That(TriggerFunctionMapping.ActionMap.Where(pair => ContextMethod(pair.Value) == null).Select(pair => pair.Key), Is.Empty);
        Assert.That(TriggerFunctionMapping.ConditionMap.Where(pair => ContextMethod(pair.Value) == null).Select(pair => pair.Key),
            Is.EquivalentTo(new[] { "any_one", "all_of", "true", "always" }), "only the group conditions run no TriggerContext method");
    }

    [Test]
    public void WriteRegistry() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        Dictionary<string, AiMetadata> ais = context.AiMetadata.ToList().ToDictionary(ai => ai.Name, StringComparer.OrdinalIgnoreCase);
        string[] names = ais.Keys.ToArray();
        var npcStorage = new NpcMetadataStorage(context);
        var skillStorage = new SkillMetadataStorage(context);
        SkillMetadata? Region(int id, short level) => skillStorage.TryGet(id, level, out SkillMetadata? region) ? region : null;
        IReadOnlyDictionary<long, IReadOnlyList<MagicPath>> paths = new TableMetadataStorage(context).MagicPathTable.Entries;
        var unsupportedByAi = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> UnsupportedOf(string name) {
            if (!unsupportedByAi.TryGetValue(name, out Dictionary<string, int>? counts)) {
                counts = Unsupported(ais[name]);
                unsupportedByAi[name] = counts;
            }
            return counts;
        }

        var rows = new List<(int Id, string Name, string Kind, string AiPath, string RunsAi, string Status, Dictionary<string, int> Unsupported, Dictionary<string, int> Attacks)>();
        foreach (NpcMetadata npc in context.NpcMetadata.ToList().Where(npc => !string.IsNullOrEmpty(npc.AiPath)).OrderBy(npc => npc.Id)) {
            string kind = npc.Basic.Friendly != 0 ? "friendly" : npc.Basic.Class >= 3 ? "boss" : "monster";
            // AiState: AI_DefaultNew.xml is the default AI; a missing path takes the NB-05 substitute, else the default AI.
            string? runs = npc.AiPath == "AI_DefaultNew.xml" ? "default"
                : ais.ContainsKey(npc.AiPath) ? npc.AiPath
                : AiMetadataStorage.Substitute(npc.AiPath, names);
            Dictionary<string, int> counts = runs is null or "default" ? [] : UnsupportedOf(runs);
            string status = counts.Keys.Any(Blocking.Contains) ? "blocked"
                : counts.Count > 0 || runs is null ? "degraded"
                : "ok";
            AnimationMetadata? animation = npcStorage.GetAnimation(npc.Model.Name);
            var attacks = new Dictionary<string, int>();
            foreach (NpcMetadataSkill.Entry entry in npc.Skill.Entries.Distinct()) {
                if (skillStorage.TryGet(entry.Id, entry.Level, out SkillMetadata? skill)) {
                    foreach ((string attackKind, int attackRows) in AttackRows(skill, animation, Region, paths)) {
                        attacks[attackKind] = attacks.GetValueOrDefault(attackKind) + attackRows;
                    }
                }
            }
            rows.Add((npc.Id, npc.Name ?? "", kind, npc.AiPath, runs ?? "default-ai (missing)", status, counts, attacks));
        }

        string outputDir = Environment.GetEnvironmentVariable("BEHAVIOR_REGISTRY_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(outputDir);
        using (var csv = new StreamWriter(Path.Combine(outputDir, "behavior-registry.csv"), false, new UTF8Encoding(false)) { NewLine = "\n" }) {
            csv.WriteLine("NpcId,Name,Kind,AiPath,RunsAi,Status,Unsupported,Attacks");
            foreach (var row in rows) {
                string unsupported = string.Join(";", row.Unsupported.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
                string attacks = string.Join(";", AttackKinds.Where(row.Attacks.ContainsKey).Select(attackKind => $"{attackKind}:{row.Attacks[attackKind]}"));
                csv.WriteLine(string.Join(",", row.Id, Quote(row.Name), row.Kind, Quote(row.AiPath), Quote(row.RunsAi), row.Status, unsupported, attacks));
            }
        }

        string[] kinds = ["boss", "monster", "friendly"];
        var md = new StringBuilder();
        md.Append("# Behaviour registry\n\n");
        md.Append($"Data: {Path.GetFileName(path)}, {File.GetLastWriteTime(path!):yyyy-MM-dd}. NPCs with an AI: {rows.Count}. ");
        md.Append("Blocked: an AI step boss phases need does nothing (summons, master/slave values); degraded: another step does nothing, ");
        md.Append("or the AI file is missing and the default AI runs.\n\n");
        md.Append("| Kind | ok | degraded | blocked |\n|---|---|---|---|\n");
        foreach (string kind in kinds) {
            int Count(string status) => rows.Count(row => row.Kind == kind && row.Status == status);
            md.Append($"| {kind} | {Count("ok")} | {Count("degraded")} | {Count("blocked")} |\n");
        }
        md.Append("\n| Unsupported step | AIs | NPCs | bosses |\n|---|---|---|---|\n");
        foreach (string step in Blocking.Concat(Degrading)) {
            int aisUsing = unsupportedByAi.Count(pair => pair.Value.ContainsKey(step));
            int npcs = rows.Count(row => row.Unsupported.ContainsKey(step));
            int bosses = rows.Count(row => row.Kind == "boss" && row.Unsupported.ContainsKey(step));
            md.Append($"| {step} | {aisUsing} | {npcs} | {bosses} |\n");
        }
        md.Append("\nAttack rows of the NPCs' skills by the rule they fire by (NB-28): keyless at the sequence's end, missing sequence at once, ");
        md.Append("cube splash around each target the cast takes, cube splash sensor when a sensor sees its target (not yet).\n\n");
        md.Append("| Attack rows | rows | NPCs | bosses |\n|---|---|---|---|\n");
        foreach (string attackKind in AttackKinds) {
            int attackRows = rows.Sum(row => row.Attacks.GetValueOrDefault(attackKind));
            int npcs = rows.Count(row => row.Attacks.ContainsKey(attackKind));
            int bosses = rows.Count(row => row.Kind == "boss" && row.Attacks.ContainsKey(attackKind));
            md.Append($"| {attackKind} | {attackRows} | {npcs} | {bosses} |\n");
        }
        File.WriteAllText(Path.Combine(outputDir, "BEHAVIOR_REGISTRY.md"), md.ToString(), new UTF8Encoding(false));

        foreach (string kind in kinds) {
            TestContext.WriteLine($"{kind}: " + string.Join(", ", rows.Where(row => row.Kind == kind).GroupBy(row => row.Status)
                .OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}")));
        }
        Assert.That(rows, Is.Not.Empty, "no NPC with an AI in the data");

        Func<int, AiMetadata?> aiOf = AiLookup(ais, rows.ToDictionary(row => row.Id, row => row.AiPath));
        HashSet<int> meetable = QuestRegistryTest.NpcsAPlayerCanMeet(context, aiOf);
        Dictionary<string, int> fidelity = WriteFidelityRegistry(context, ais, rows.Select(row => (row.Id, row.AiPath, row.RunsAi)), meetable, outputDir, path!);
        Assert.That(DataSweepTest.OverThresholds(fidelity.Select(pair => (pair.Key, pair.Value)), Thresholds), Is.Empty,
            $"fidelity counts above their FT-04 thresholds; the lists are in {outputDir}");
    }

    // FT-04: what the server does not do like the original, from the data and the server's sources: the calls of trigger functions that do
    // nothing, the uses of empty AI nodes, NPCs whose AI path has no file, RecvOps without a handler and SendOps never sent that the
    // captures have (capture-opcode-tally.tsv, FT-01), storage tables never read, and condition types the data's quests and trophies use
    // that are never reported. A name in the sources counts as a handler, a send, a read or a report, so those counts are floors.
    // FT-07: of what a player can meet. Stub calls count the scripts that have a map row, minus the on-purpose no-ops; empty AI nodes count the AIs an
    // NPC runs; NPCs without an AI file count those placed or summoned (meetable); the table scan also reads Maple2.Database.
    private static Dictionary<string, int> WriteFidelityRegistry(MetadataContext context, Dictionary<string, AiMetadata> ais, IEnumerable<(int Id, string AiPath, string RunsAi)> npcs, HashSet<int> meetable, string outputDir, string dataPath) {
        var rows = new List<(string Kind, string Name, int? Uses)>();
        var counts = new Dictionary<string, int>();
        void Add(string count, string kind, IEnumerable<(string Name, int? Uses)> found, bool byUses = false) {
            List<(string Name, int? Uses)> list = found.OrderByDescending(item => item.Uses).ThenBy(item => item.Name, StringComparer.Ordinal).ToList();
            rows.AddRange(list.Select(item => (kind, item.Name, item.Uses)));
            counts[count] = counts.GetValueOrDefault(count) + (byUses ? list.Sum(item => item.Uses ?? 0) : list.Count);
        }

        HashSet<(string Kind, string Name)> stubs = TriggerFunctionMapping.ActionMap.Select(pair => (Kind: "action", Name: pair.Key, Factory: (Delegate) pair.Value))
            .Concat(TriggerFunctionMapping.ConditionMap.Select(pair => (Kind: "condition", Name: pair.Key, Factory: (Delegate) pair.Value)))
            .Where(function => ContextMethod(function.Factory) is { } method && DoesNothing(method))
            .Select(function => (function.Kind, function.Name)).ToHashSet();
        HashSet<string> mapBlocks = context.MapMetadata.Select(map => map.XBlock).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var calls = new Dictionary<(string Kind, string Name), int>();
        var onPurpose = new Dictionary<(string Kind, string Name), int>();
        foreach (TriggerMetadata trigger in context.TriggerMetadata.AsEnumerable().Where(trigger => mapBlocks.Contains(trigger.MapXBlock))) {
            var document = new XmlDocument();
            try {
                document.LoadXml(trigger.Xml);
            } catch (XmlException) {
                continue; // the sweep's trigger check lists it
            }
            foreach (string kind in new[] { "action", "condition" }) {
                foreach (XmlNode node in document.SelectNodes($"//{kind}")!) {
                    if (node.Attributes?["name"]?.Value is { } name && stubs.Contains((kind, name))) {
                        Dictionary<(string Kind, string Name), int> into = OnPurposeNoOps.ContainsKey((kind, name)) ? onPurpose : calls;
                        into[(kind, name)] = into.GetValueOrDefault((kind, name)) + 1;
                    }
                }
            }
        }
        foreach (string kind in new[] { "action", "condition" }) {
            Add("Trigger stub calls", $"trigger {kind} doing nothing", calls.Where(pair => pair.Key.Kind == kind).Select(pair => (pair.Key.Name, (int?) pair.Value)), byUses: true);
        }

        rows.AddRange(onPurpose.OrderBy(pair => pair.Key.Name, StringComparer.Ordinal).Select(pair => ($"trigger {pair.Key.Kind} doing nothing on purpose", pair.Key.Name, (int?) pair.Value)));
        var nodeUses = new Dictionary<string, int>();
        foreach ((string step, int uses) in npcs.Select(npc => npc.RunsAi).Where(ais.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(name => Unsupported(ais[name]))) {
            nodeUses[step] = nodeUses.GetValueOrDefault(step) + uses;
        }
        Add("Empty AI node uses", "empty AI node", nodeUses.Select(pair => (pair.Key, (int?) pair.Value)), byUses: true);
        Add("NPCs without an AI file", "AI path without a file", npcs.Where(npc => meetable.Contains(npc.Id) && npc.AiPath != "AI_DefaultNew.xml" && !ais.ContainsKey(npc.AiPath))
            .GroupBy(npc => npc.AiPath, StringComparer.OrdinalIgnoreCase).Select(group => (group.Key, (int?) group.Count())), byUses: true);

        // The sources sit beside capture-opcode-tally.tsv in the Maple2 folder, above the test's output folder.
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "capture-opcode-tally.tsv"))) {
            root = Path.GetDirectoryName(root);
        }
        Assert.That(root, Is.Not.Null, "capture-opcode-tally.tsv not found above the test output");
        Dictionary<string, string> sources = new[] { "Maple2.Database", "Maple2.Server.Core", "Maple2.Server.Game", "Maple2.Server.Login", "Maple2.Server.World", "Maple2.Server.Web" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root!, project), "*.cs", SearchOption.AllDirectories))
            .ToDictionary(file => file, File.ReadAllText);
        string code = string.Join("\n", sources.Values);
        HashSet<string> Names(string text, string pattern) => Regex.Matches(text, pattern).Select(match => match.Groups[1].Value).ToHashSet();
        // The tally's columns: direction (C client, S server), op, then capture file counts, the last without duplicates.
        Dictionary<(string, string), int> traffic = File.ReadLines(Path.Combine(root!, "capture-opcode-tally.tsv")).Skip(1).Select(line => line.Split('\t'))
            .ToDictionary(cells => (cells[0], cells[1]), cells => int.Parse(cells[^1]));
        HashSet<string> handled = Names(code, @"RecvOp OpCode => RecvOp\.(\w+);");
        HashSet<string> sent = Names(code, @"\bSendOp\.(\w+)");
        HashSet<string> memberNames = Names(code, @"\.(\w+)");
        // ConditionUtil checks a condition's code and target; reports are made elsewhere.
        HashSet<string> reported = Names(string.Join("\n", sources.Where(pair => Path.GetFileName(pair.Key) != "ConditionUtil.cs").Select(pair => pair.Value)), @"\bConditionType\.(\w+)");
        reported.Add(nameof(ConditionType.trigger)); // SetAchievement reports the type its script names, and the scripts name only trigger
        Add("RecvOps without a handler", "RecvOp without a handler", Enum.GetNames<RecvOp>().Where(op => !handled.Contains(op) && traffic.ContainsKey(("C", op)))
            .Select(op => (op, (int?) traffic[("C", op)])));
        Add("SendOps never sent", "SendOp never sent", Enum.GetNames<SendOp>().Where(op => !sent.Contains(op) && traffic.ContainsKey(("S", op)))
            .Select(op => (op, (int?) traffic[("S", op)])));
        Add("Tables never read", "table never read", typeof(TableMetadataStorage).GetProperties().Concat(typeof(ServerTableMetadataStorage).GetProperties())
            .Select(property => property.Name).Where(name => !memberNames.Contains(name)).Select(name => (name, (int?) null)));
        IEnumerable<ConditionMetadata> conditions = context.QuestMetadata.AsEnumerable().SelectMany(quest => quest.Conditions)
            .Concat(context.AchievementMetadata.AsEnumerable().SelectMany(achievement => achievement.Grades.Values.Select(grade => grade.Condition)));
        Add("Condition types never reported", "condition type never reported", conditions.GroupBy(condition => condition.Type.ToString())
            .Where(group => !reported.Contains(group.Key)).Select(group => (group.Key, (int?) group.Count())));

        using (var csv = new StreamWriter(Path.Combine(outputDir, "fidelity-registry.csv"), false, new UTF8Encoding(false)) { NewLine = "\n" }) {
            csv.WriteLine("Kind,Name,Uses");
            foreach ((string kind, string name, int? uses) in rows) {
                csv.WriteLine(string.Join(",", kind, Quote(name), uses));
            }
        }
        var md = new StringBuilder();
        md.Append("# Fidelity registry\n\n");
        md.Append($"Data: {Path.GetFileName(dataPath)}, {File.GetLastWriteTime(dataPath):yyyy-MM-dd}. What the server does not do like the original (FT-04). ");
        md.Append("fidelity-registry.csv lists what each count counts, with its uses: script calls, AI node uses, NPCs, capture files, quest and trophy conditions.\n\n");
        md.Append("| Count | Value | Threshold |\n|---|---|---|\n");
        foreach ((string count, int value) in counts) {
            md.Append($"| {count} | {value} | {Thresholds.GetValueOrDefault(count)} |\n");
        }
        File.WriteAllText(Path.Combine(outputDir, "FIDELITY_REGISTRY.md"), md.ToString(), new UTF8Encoding(false));
        TestContext.WriteLine(string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}")));
        return counts;
    }

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
