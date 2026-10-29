using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.ActorStateComponent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>BT-69: every skillRange, trace and runaway an NPC runs on a skill has a skill distance (detect distance or first attack range).</summary>
[Category("Data")]
[NonParallelizable]
public class AiSkillDistanceDataTest {
    [Test]
    public void EverySkillDistanceReferenceHasADistance() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        Dictionary<int, StoredSkillMetadata> skills = context.SkillMetadata.ToDictionary(skill => skill.Id);

        // The Giant Turtle's gate, the Turtle's fake 250 skill, the Griffin's skill 11 and the Elf Twins' fake close-range skill.
        foreach ((int id, float distance) in new[] { (50000331, 900f), (50009998, 250f), (50000383, 500f), (50000231, 700f) }) {
            SkillMetadataLevel level = skills[id].Levels[1];
            Assert.That(level.Detect.Distance, Is.EqualTo(0), $"{id} detect");
            Assert.That(AiState.SkillDistance(level), Is.EqualTo(distance), $"{id}");
        }

        Dictionary<string, AiMetadata> ais = context.AiMetadata.ToList().ToDictionary(ai => ai.Name, StringComparer.OrdinalIgnoreCase);
        string[] names = ais.Keys.ToArray();
        var noDistance = new List<string>();
        int detectZero = 0;
        foreach (NpcMetadata npc in context.NpcMetadata.ToList().Where(npc => !string.IsNullOrEmpty(npc.AiPath))) {
            // AiState: AI_DefaultNew.xml is the default AI; a missing path takes the NB-05 substitute, else the default AI.
            string runs = npc.AiPath == "AI_DefaultNew.xml" ? Constant.DefaultAiPath
                : ais.ContainsKey(npc.AiPath) ? npc.AiPath
                : AiMetadataStorage.Substitute(npc.AiPath, names) ?? Constant.DefaultAiPath;
            AiMetadata ai = ais[runs];

            var found = new List<AiMetadata.Entry>();
            void Walk(AiMetadata.Entry entry) {
                if (entry is AiMetadata.SkillRangeCondition or AiMetadata.TraceNode or AiMetadata.RunawayNode) {
                    found.Add(entry);
                }
                if (entry is AiMetadata.ConditionsNode conditions) {
                    foreach (AiMetadata.Condition condition in conditions.Conditions) {
                        Walk(condition);
                    }
                }
                if (entry is AiMetadata.Node node) {
                    foreach (AiMetadata.Entry child in node.Entries) {
                        Walk(child);
                    }
                }
            }
            foreach (AiMetadata.Entry entry in ai.Reserved.Concat(ai.Battle).Concat(ai.BattleEnd).Concat(ai.AiPresets)) {
                Walk(entry);
            }

            NpcMetadataSkill.Entry[] entries = npc.Skill.Entries;
            foreach (AiMetadata.Entry entry in found) {
                (int idx, short levelOverride) = entry switch {
                    AiMetadata.SkillRangeCondition range => (range.SkillIdx, range.SkillLev),
                    AiMetadata.TraceNode trace => (trace.SkillIdx, (short) 0),
                    AiMetadata.RunawayNode runaway => (runaway.SkillIdx, (short) 0),
                    _ => (0, (short) 0),
                };
                if (idx < 1 || idx > entries.Length) {
                    continue;
                }

                // TryGetSkill: the entry's own skill and level must exist, then a level override reads that level.
                NpcMetadataSkill.Entry own = entries[idx - 1];
                if (!skills.TryGetValue(own.Id, out StoredSkillMetadata? stored) || !stored.Levels.ContainsKey(own.Level)
                    || !stored.Levels.TryGetValue(levelOverride != 0 ? levelOverride : own.Level, out SkillMetadataLevel? level)) {
                    continue;
                }

                if (level.Detect.Distance == 0) {
                    detectZero++;
                }
                if (AiState.SkillDistance(level) == 0) {
                    noDistance.Add($"npc {npc.Id}, AI {runs}, {entry.GetType().Name} '{entry.Name}', skill {own.Id}, level {(levelOverride != 0 ? levelOverride : own.Level)}");
                }
            }
        }

        Assert.That(noDistance, Is.Empty, string.Join("; ", noDistance.Take(5)));
        Assert.That(detectZero, Is.GreaterThan(6000), "references to a skill with detect distance 0");
    }
}
