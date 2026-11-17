using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>VR-04: every invincible effect a monster can hold has an end or a named removal. A hit on a target with an effect whose
/// DefensiveProperty invincible is 1 or 2 deals nothing (NB-21), so such an effect that never ends keeps its monster unkillable. The effects a
/// monster can hold are the buffs of its data, the effects of the skills it casts (and of the effects and skills those apply), the buffs its AI
/// adds and the buffs a script adds to monsters. An effect ends by itself with a duration unless its keep condition is unlimited (Buff.cs);
/// else a named removal is a script's remove_buff, an AI's remove step or the Cancel of another effect a monster can hold. The effects with
/// neither are findings: listed, not fixed, and held by <see cref="KnownWithoutEnd"/>, so a new one fails the test.</summary>
[Category("Data")]
[NonParallelizable]
public class InvincibleEffectsTest {
    // Each of these is held by a monster, never ends by itself and has no removal in the data: 15 effect levels of 13 effects.
    // The monster's death is its only end (71000046's Cancel, 71000045, is held by no monster); whether the original removes them by a mechanism the data does not show is not known.
    private static readonly (int Id, short Level)[] KnownWithoutEnd = [
        (40361001, 1), (49281001, 1), (49281001, 2), (49286001, 1), (50001000, 1), (50001001, 1), (50001001, 2), (50001100, 2),
        (50001101, 1), (50001102, 1), (50001106, 1), (60030001, 1), (60070001, 1), (69000401, 1), (71000046, 1),
    ];

    private static IEnumerable<(int Id, short Level)> References(SkillEffectMetadata[] effects) =>
        effects.SelectMany(effect => effect.Skills).Select(skill => (skill.Id, skill.Level));

    private static IEnumerable<(int Id, short Level)> References(SkillMetadataLevel level) =>
        References(level.Skills).Concat(level.Motions.SelectMany(motion => motion.Attacks)
            .SelectMany(attack => References(attack.Skills).Concat(References(attack.SkillsOnDamage))));

    private static void WalkAi(AiMetadata.Entry entry, List<(int Id, short Level)> adds, HashSet<int> removes) {
        if (entry is AiMetadata.BuffNode buff) {
            if (buff.Type == NodeBuffType.Add) {
                adds.Add((buff.Id, buff.Level));
            } else {
                removes.Add(buff.Id);
            }
        }
        if (entry is AiMetadata.Node node) {
            foreach (AiMetadata.Entry child in node.Entries) {
                WalkAi(child, adds, removes);
            }
        }
    }

    private static void WithData(Action<MetadataContext> test) {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        test(context);
    }

    [Test]
    public void EveryInvincibleEffectAMonsterCanHoldHasAnEnd() {
        var config = DefensiveEffects.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "defensive-effects.json")));
        Assert.That(config?.Invincible, Is.Not.Null);
        // The values the server enforces: 1 and 2 (DefensiveEffects.InvincibleHit).
        HashSet<(int Id, short Level)> invincible = config!.Invincible!
            .SelectMany(effect => effect.Value.Where(level => level.Value is 1 or 2).Select(level => (effect.Key, level.Key))).ToHashSet();

        WithData(context => {
            Dictionary<(int Id, short Level), AdditionalEffectMetadata> effects = context.AdditionalEffectMetadata.AsEnumerable().ToDictionary(effect => (effect.Id, effect.Level));
            Dictionary<int, IReadOnlyDictionary<short, SkillMetadataLevel>> skills = context.SkillMetadata.AsEnumerable().ToDictionary(skill => skill.Id, skill => skill.Levels);

            var held = new Dictionary<(int Id, short Level), HashSet<string>>();
            var visited = new HashSet<(int Id, short Level)>();
            void Visit((int Id, short Level) reference, string source) {
                if (!visited.Add(reference)) {
                    held.GetValueOrDefault(reference)?.Add(source);
                    return;
                }
                if (effects.TryGetValue(reference, out AdditionalEffectMetadata? effect)) {
                    held[reference] = [source];
                    foreach ((int Id, short Level) next in References(effect.Skills).Concat(References(effect.TickSkills))) {
                        Visit(next, source);
                    }
                } else if (skills.TryGetValue(reference.Id, out IReadOnlyDictionary<short, SkillMetadataLevel>? levels) && levels.TryGetValue(reference.Level, out SkillMetadataLevel? level)) {
                    foreach ((int Id, short Level) next in References(level)) {
                        Visit(next, source);
                    }
                }
            }

            List<NpcMetadata> npcs = context.NpcMetadata.AsEnumerable().ToList();
            foreach (NpcMetadata npc in npcs) {
                foreach (NpcMetadataSkill.Entry entry in npc.Skill.Entries) {
                    Visit((entry.Id, entry.Level), "a skill it casts");
                }
                foreach (NpcMetadataBuff buff in npc.Property.Buffs) {
                    Visit((buff.Id, (short) buff.Level), "its spawn buffs");
                }
            }

            // AI steps of the AIs some monster uses.
            HashSet<string> aiPaths = npcs.Select(npc => npc.AiPath).ToHashSet();
            var aiAdds = new List<(int Id, short Level)>();
            var aiRemoves = new HashSet<int>();
            foreach (AiMetadata ai in context.AiMetadata.AsEnumerable().Where(ai => aiPaths.Contains(ai.Name))) {
                foreach (AiMetadata.Entry entry in ((IEnumerable<AiMetadata.Entry>) ai.Reserved).Concat(ai.Battle).Concat(ai.BattleEnd).Concat(ai.AiPresets)) {
                    WalkAi(entry, aiAdds, aiRemoves);
                }
            }
            foreach ((int Id, short Level) add in aiAdds) {
                Visit(add, "its AI");
            }

            // Scripts: add_buff with ignore_player names monsters; remove_buff names its removal.
            var scriptRemoves = new HashSet<int>();
            foreach (TriggerMetadata trigger in context.TriggerMetadata.Where(trigger => trigger.Xml.Contains("_buff")).AsEnumerable()) {
                var document = new XmlDocument();
                document.LoadXml(trigger.Xml);
                foreach (XmlNode action in document.SelectNodes("//action[@name='add_buff']")!) {
                    string ignorePlayer = action.Attributes?["ignore_player"]?.Value ?? string.Empty;
                    if (ignorePlayer.Equals("true", StringComparison.OrdinalIgnoreCase) || ignorePlayer == "1") {
                        Visit((int.Parse(action.Attributes!["skill_id"]!.Value), short.Parse(action.Attributes?["level"]?.Value ?? "1")), "a script");
                    }
                }
                foreach (XmlNode action in document.SelectNodes("//action[@name='remove_buff']")!) {
                    scriptRemoves.Add(int.Parse(action.Attributes!["skill_id"]!.Value));
                }
            }

            HashSet<int> cancelled = held.Keys.Select(key => effects[key].Update.Cancel).OfType<AdditionalEffectMetadataUpdate.CancelEffect>().SelectMany(cancel => cancel.Ids).ToHashSet();
            List<(int Id, short Level)> invincibleHeld = held.Keys.Where(invincible.Contains).OrderBy(key => key).ToList();
            var withoutEnd = new List<(int Id, short Level)>();
            foreach ((int Id, short Level) key in invincibleHeld) {
                AdditionalEffectMetadataProperty property = effects[key].Property;
                bool ends = property.DurationTick > 0 && property.KeepCondition != BuffKeepCondition.UnlimitedDuration;
                string removal = scriptRemoves.Contains(key.Id) ? "a script's remove_buff" : aiRemoves.Contains(key.Id) ? "an AI's remove step"
                    : cancelled.Contains(key.Id) ? "another effect's Cancel" : "none";
                TestContext.Out.WriteLine($"invincible effect {key.Id} level {key.Level}: held through {string.Join(", ", held[key].Order())}; "
                                          + (ends ? $"ends after {property.DurationTick} ms" : $"no end of its own (duration {property.DurationTick} ms, keep condition {property.KeepCondition}); removal: {removal}"));
                if (!ends && removal == "none") {
                    withoutEnd.Add(key);
                }
            }
            TestContext.Out.WriteLine($"{invincibleHeld.Count} of {invincible.Count} enforced invincible effect levels can be held by a monster; {withoutEnd.Count} have no end and no removal");

            Assert.That(invincibleHeld.Count, Is.GreaterThanOrEqualTo(90), "the listing reaches the effects the monsters hold");
            Assert.That(withoutEnd, Is.EquivalentTo(KnownWithoutEnd), "an invincible effect with no end and no removal is a finding: it is listed here, not fixed");
        });
    }
}
