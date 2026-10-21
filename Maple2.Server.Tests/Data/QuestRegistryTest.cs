using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Trigger.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Data report test that writes the quest test registry: one CSV row per quest with what the data says about its prerequisites,
/// start and complete NPCs, objectives and rewards, and a status (ok / warning / error / event-only / disabled) with reasons.
/// Like DataSweepTest it is a report generator: data findings never fail the test.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class QuestRegistryTest {
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

        // Get all quests and build a lookup dictionary for efficient access
        var quests = context.QuestMetadata.ToList();
        var questLookup = quests.ToDictionary(quest => quest.Id);

        // Collect all ids
        var npcIds = context.NpcMetadata.Select(npc => npc.Id).ToHashSet();
        var mapIds = context.MapMetadata.Select(map => map.Id).ToHashSet();
        var itemIds = context.ItemMetadata.Select(item => item.Id).ToHashSet();

        // Where the server places NPCs (QF-05): spawn points, trigger spawns, region spawns, object weapons and defense
        // guards (placed); the quest-NPC fallback for start and complete NPCs (questPlaced); world bosses (information).
        var tables = new ServerTableMetadataStorage(context);
        var npcById = context.NpcMetadata.ToList().ToDictionary(npc => npc.Id);
        var maps = context.MapMetadata.ToList();
        Dictionary<string, XBlockEntities> entities = IndexEntities(context);
        var placed = BuildPlaced(context, tables, maps, entities, npcById);
        var questPlaced = BuildQuestPlaced(maps, entities, quests, npcById);
        var worldBoss = BuildWorldBoss(tables);
        bool Friendly(int id) => npcById.TryGetValue(id, out NpcMetadata? npc) && npc.Basic.Friendly > 0;
        // A start, complete or talk NPC: placed (or by the quest fallback) and one the player can talk to.
        bool Reachable(int id) => (placed.Contains(id) || questPlaced.Contains(id)) && Friendly(id);
        string NotReachable(int id) => placed.Contains(id) || questPlaced.Contains(id) ? "not friendly" : "not spawned";

        // Compute all quest data
        var questRows = new List<(QuestMetadata Quest, string Status, List<string> Reasons, int UncheckedCount)>();

        bool EventOnly(QuestMetadata quest) => !string.IsNullOrEmpty(quest.Basic.EventTag) || quest.EventMissionType != QuestEventMissionType.none;

        foreach (QuestMetadata quest in quests.OrderBy(q => q.Id)) {
            var reasons = new List<string>();
            bool missing = false;
            bool soft = false; // not-spawned or event-only prerequisite

            // EventOnly check
            bool eventOnly = EventOnly(quest);

            // Start NPC check: none needed for a remote accept, the postbox or an auto start (QuestHandler, QuestManager)
            bool needsStartNpc = (quest.RemoteAccept?.Type ?? QuestRemoteType.None) == QuestRemoteType.None && !quest.Basic.UsePostbox
                && !(quest.Basic.AutoStart && quest.Basic.CompleteNpc == 0 && quest.Basic.Type != QuestType.FieldMission);
            if (quest.Basic.StartNpc > 0) {
                if (!npcIds.Contains(quest.Basic.StartNpc)) {
                    reasons.Add($"start npc {quest.Basic.StartNpc} missing");
                    missing = true;
                } else if (needsStartNpc && !Reachable(quest.Basic.StartNpc)) {
                    reasons.Add($"start npc {quest.Basic.StartNpc} {NotReachable(quest.Basic.StartNpc)}");
                    soft = true;
                }
            }

            // Complete NPC check: none needed for a remote complete or a field mission (auto-completed)
            bool needsCompleteNpc = (quest.RemoteComplete?.Type ?? QuestRemoteType.None) == QuestRemoteType.None
                && quest.Basic.Type != QuestType.FieldMission;
            if (quest.Basic.CompleteNpc > 0) {
                if (!npcIds.Contains(quest.Basic.CompleteNpc)) {
                    reasons.Add($"complete npc {quest.Basic.CompleteNpc} missing");
                    missing = true;
                } else if (needsCompleteNpc && !Reachable(quest.Basic.CompleteNpc)) {
                    reasons.Add($"complete npc {quest.Basic.CompleteNpc} {NotReachable(quest.Basic.CompleteNpc)}");
                    soft = true;
                }
            }

            // Prerequisites check
            foreach (int prerequisiteId in (quest.Require.Quest ?? []).Concat(quest.Require.SelectableQuest ?? [])) {
                if (prerequisiteId <= 0) continue;

                if (!questLookup.TryGetValue(prerequisiteId, out QuestMetadata? prerequisite)) {
                    reasons.Add($"prerequisite {prerequisiteId} missing");
                    missing = true;
                } else if (EventOnly(prerequisite)) {
                    reasons.Add($"prerequisite {prerequisiteId} event-only");
                    soft = true;
                }
            }

            // Objectives check
            int uncheckedCount = 0;
            foreach (ConditionMetadata condition in quest.Conditions) {
                string type = condition.Type.ToString();
                switch (type) {
                    case "npc" or "talk_in" or "dialogue":
                        if (condition.Codes?.Integers != null) {
                            int[] codes = condition.Codes.Integers.Where(id => id > 0).ToArray();
                            foreach (int id in codes.Where(id => !npcIds.Contains(id))) {
                                reasons.Add($"objective npc {id} missing");
                                missing = true;
                            }
                            // Any one code meets the condition (ConditionUtil); talking needs an NPC the player can talk to.
                            bool talk = type != "npc";
                            int[] known = codes.Where(npcIds.Contains).ToArray();
                            if (known.Length > 0 && !known.Any(id => talk ? Reachable(id) : placed.Contains(id))) {
                                int boss = talk ? 0 : known.FirstOrDefault(worldBoss.Contains);
                                if (boss > 0) {
                                    reasons.Add($"objective npc {boss} world boss (hourly)"); // information: not a warning
                                } else {
                                    foreach (int id in known) {
                                        reasons.Add($"objective npc {id} {(talk ? NotReachable(id) : "not spawned")}");
                                    }
                                    soft = true;
                                }
                            }
                        }
                        break;
                    case "item_exist" or "item_add" or "item_move":
                        if (condition.Codes?.Integers != null) {
                            foreach (int id in condition.Codes.Integers) {
                                if (id <= 0) continue;
                                if (!itemIds.Contains(id)) {
                                    reasons.Add($"objective item {id} missing");
                                    missing = true;
                                }
                            }
                        }
                        break;
                    case "map" or "swimtime":
                        if (condition.Codes?.Integers != null) {
                            foreach (int id in condition.Codes.Integers) {
                                if (id <= 0) continue;
                                if (!mapIds.Contains(id)) {
                                    reasons.Add($"objective map {id} missing");
                                    missing = true;
                                }
                            }
                        }
                        break;
                    case "quest":
                        if (condition.Codes?.Integers != null) {
                            foreach (int id in condition.Codes.Integers) {
                                if (id <= 0) continue;
                                if (!questLookup.TryGetValue(id, out QuestMetadata? targetQuest)) {
                                    reasons.Add($"objective quest {id} missing");
                                    missing = true;
                                } else if (EventOnly(targetQuest)) {
                                    reasons.Add($"objective quest {id} event-only");
                                    soft = true;
                                }
                            }
                        }
                        break;
                    default:
                        uncheckedCount++;
                        break;
                }
            }

            // Rewards check
            foreach (QuestMetadataReward.Item item in quest.AcceptReward.EssentialItem.Concat(quest.AcceptReward.EssentialJobItem)
                .Concat(quest.CompleteReward.EssentialItem).Concat(quest.CompleteReward.EssentialJobItem)) {
                if (item.Id > 0 && !itemIds.Contains(item.Id)) { // id 0 = no item (e.g. a class without a job reward)
                    reasons.Add($"reward item {item.Id} missing");
                    missing = true;
                }
            }

            // Determine status
            string status;
            if (quest.Basic.Disabled) {
                status = "disabled";
            } else if (eventOnly) {
                status = "event-only";
            } else if (missing) {
                status = "error";
            } else if (soft) {
                status = "warning";
            } else {
                status = "ok";
            }

            questRows.Add((quest, status, reasons, uncheckedCount));
        }

        // Write CSV
        string outputDir = Environment.GetEnvironmentVariable("QUEST_REGISTRY_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        if (!Directory.Exists(outputDir)) {
            Directory.CreateDirectory(outputDir);
        }

        string csvPath = Path.Combine(outputDir, "quest-registry.csv");
        using (var writer = new StreamWriter(csvPath, false, new UTF8Encoding(false)) { NewLine = "\n" }) {
            // Write header
            writer.WriteLine("Id,Name,Type,Chapter,Level,Status,Reasons,StartNpc,CompleteNpc,Prerequisites,Objectives,UncheckedObjectives,RewardItems");

            foreach (var (quest, status, reasons, uncheckedCount) in questRows) {
                var prerequisites = new List<string>();
                if (quest.Require.Quest != null) prerequisites.AddRange(quest.Require.Quest.Where(id => id > 0).Select(id => id.ToString()));
                if (quest.Require.SelectableQuest != null) prerequisites.AddRange(quest.Require.SelectableQuest.Where(id => id > 0).Select(id => id.ToString()));

                var objectives = new List<string>();
                foreach (ConditionMetadata condition in quest.Conditions) {
                    string type = condition.Type.ToString();
                    switch (type) {
                        case "npc" or "talk_in" or "dialogue" or "item_exist" or "item_add" or "item_move" or "map" or "swimtime" or "quest":
                            if (condition.Codes?.Integers != null) {
                                objectives.Add($"{type}:{string.Join(" ", condition.Codes.Integers)}");
                            }
                            break;
                    }
                }

                var rewardItems = new List<string>();
                foreach (QuestMetadataReward.Item item in quest.AcceptReward.EssentialItem.Concat(quest.AcceptReward.EssentialJobItem)
                    .Concat(quest.CompleteReward.EssentialItem).Concat(quest.CompleteReward.EssentialJobItem)) {
                    if (item.Id > 0) rewardItems.Add(item.Id.ToString());
                }

                // Write CSV row
                writer.WriteLine($"{quest.Id},{QuoteCsvField(quest.Name ?? "")},{quest.Basic.Type},{quest.Basic.ChapterId},{quest.Basic.StandardLevel},{status},{QuoteCsvField(string.Join("; ", reasons))},{quest.Basic.StartNpc},{quest.Basic.CompleteNpc},{string.Join(" ", prerequisites)},{string.Join("; ", objectives)},{uncheckedCount},{string.Join(" ", rewardItems)}");
            }
        }

        // Write Markdown report
        string mdPath = Path.Combine(outputDir, "QUEST_REGISTRY.md");
        using (var writer = new StreamWriter(mdPath, false, new UTF8Encoding(false)) { NewLine = "\n" }) {
            writer.WriteLine("# Quest test registry");
            writer.WriteLine();
            writer.WriteLine($"Database: {path}");
            writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd}");
            writer.WriteLine();

            // Status counts table
            var statusCounts = questRows.GroupBy(row => row.Status).ToDictionary(g => g.Key, g => g.Count());
            writer.WriteLine("## Status counts");
            writer.WriteLine("| Status | Count |");
            writer.WriteLine("|---|---|");
            foreach (var kvp in statusCounts.OrderBy(pair => pair.Key)) {
                writer.WriteLine($"| {kvp.Key} | {kvp.Value} |");
            }
            writer.WriteLine();

            // Type x Status counts table
            var typeStatusCounts = questRows.GroupBy(row => new { row.Quest.Basic.Type, row.Status })
                .ToDictionary(g => g.Key, g => g.Count());
            writer.WriteLine("## Type x Status counts");
            writer.WriteLine("| Type | Disabled | Event-only | Error | Warning | Ok |");
            writer.WriteLine("|---|---:|---:|---:|---:|---:|");

            var allTypes = questRows.Select(row => row.Quest.Basic.Type).Distinct().OrderBy(t => t.ToString()).ToList();
            var allStatuses = new[] { "disabled", "event-only", "error", "warning", "ok" };

            foreach (var type in allTypes) {
                writer.Write($"| {type} | ");
                foreach (string status in allStatuses) {
                    int count = 0;
                    foreach (var kvp in typeStatusCounts) {
                        if (kvp.Key.Type == type && kvp.Key.Status == status) {
                            count = kvp.Value;
                            break;
                        }
                    }
                    writer.Write($"{count} | ");
                }
                writer.WriteLine();
            }
            writer.WriteLine();

            // Most common reason kinds
            var reasonKinds = questRows.SelectMany(row => row.Reasons)
                .Select(reason => Regex.Replace(reason, @"\d+", "N"))
                .GroupBy(kind => kind)
                .ToDictionary(g => g.Key, g => g.Count())
                .OrderByDescending(pair => pair.Value)
                .Take(20);

            writer.WriteLine("## Most common reason kinds");
            writer.WriteLine("| Kind | Count |");
            writer.WriteLine("|---|---|");
            foreach (var kvp in reasonKinds) {
                writer.WriteLine($"| {kvp.Key} | {kvp.Value} |");
            }
            writer.WriteLine();

            // Error quests list
            var errorQuests = questRows.Where(row => row.Status == "error").Select(row => row.Quest).ToList();
            writer.WriteLine("## Error quests");
            writer.WriteLine("| Id | Name | Type | Reasons |");
            writer.WriteLine("|---|---|---|---|");
            foreach (var quest in errorQuests.Take(1000)) {
                var reasonsForQuest = questRows.First(r => r.Quest == quest).Reasons;
                writer.WriteLine($"| {quest.Id} | {quest.Name ?? ""} | {quest.Basic.Type} | {string.Join("; ", reasonsForQuest)} |");
            }

            if (errorQuests.Count > 1000) {
                writer.WriteLine($"| ... | ... | ... | ... |");
                writer.WriteLine($"(Only showing first 1000 of {errorQuests.Count} error quests)");
            }
        }

        // Assertions
        Assert.That(questRows.Count, Is.EqualTo(quests.Count));

        TestContext.WriteLine("Status counts:");
        foreach (var kvp in questRows.GroupBy(row => row.Status).ToDictionary(g => g.Key, g => g.Count()).OrderBy(pair => pair.Key)) {
            TestContext.WriteLine($"  {kvp.Key}: {kvp.Value}");
        }
    }

    /// <summary>A map's spawn and script entities (one xblock can serve several maps).</summary>
    private sealed class XBlockEntities {
        public readonly List<SpawnPointNPC> Spawns = [];
        public readonly List<EventSpawnPointNPC> EventSpawns = [];
        public readonly HashSet<string> Triggers = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<int> RegionSpawns = [];
        public readonly HashSet<int> WeaponNpcs = [];
    }

    private static Dictionary<string, XBlockEntities> IndexEntities(MetadataContext context) {
        var byXBlock = new Dictionary<string, XBlockEntities>(StringComparer.OrdinalIgnoreCase);
        foreach (MapEntity entity in context.MapEntity) {
            if (!byXBlock.TryGetValue(entity.XBlock, out XBlockEntities? x)) {
                byXBlock[entity.XBlock] = x = new XBlockEntities();
            }
            switch (entity.Block) {
                case EventSpawnPointNPC eventSpawn: // before SpawnPointNPC, which it derives from
                    x.EventSpawns.Add(eventSpawn);
                    break;
                case SpawnPointNPC spawn:
                    x.Spawns.Add(spawn);
                    break;
                case TriggerModel model:
                    x.Triggers.Add(model.Name);
                    break;
                case Ms2RegionSpawn region:
                    x.RegionSpawns.Add(region.Id);
                    break;
                case ObjectWeapon { SpawnNpcId: > 0 } weapon:
                    x.WeaponNpcs.Add(weapon.SpawnNpcId);
                    break;
            }
        }
        return byXBlock;
    }

    /// <summary>NPCs the server places in a field: regular spawn points that spawn by themselves or through a combine
    /// group of the map (FieldManager, FieldNpcSpawnPoint), event spawn points that a script running in the map spawns
    /// (TriggerContext.SpawnNpc), region spawns by tag and difficulty, object weapons and defense guard items.</summary>
    private static HashSet<int> BuildPlaced(MetadataContext context, ServerTableMetadataStorage tables, List<MapMetadata> maps,
            Dictionary<string, XBlockEntities> entities, Dictionary<int, NpcMetadata> npcById) {
        var placed = new HashSet<int>();
        // Spawn point ids that the scripts of an xblock spawn; a script runs only when a TriggerModel of the xblock names it.
        var scriptSpawns = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (TriggerMetadata trigger in context.TriggerMetadata) {
            if (!entities.TryGetValue(trigger.MapXBlock, out XBlockEntities? x) || !x.Triggers.Contains(trigger.Name)) {
                continue;
            }
            if (!scriptSpawns.TryGetValue(trigger.MapXBlock, out HashSet<int>? ids)) {
                scriptSpawns[trigger.MapXBlock] = ids = [];
            }
            ids.UnionWith(SpawnedIds(trigger.Xml));
        }

        var tags = new NpcMetadataStorage(context);
        foreach (MapMetadata map in maps) {
            if (!entities.TryGetValue(map.XBlock, out XBlockEntities? x)) {
                continue;
            }
            HashSet<int> combine = CombineSpawnIds(tables.CombineSpawnTable, map.Id);
            foreach (SpawnPointNPC spawn in x.Spawns.Where(s => s.SpawnOnFieldCreate || s.RegenCheckTime > 0 || combine.Contains(s.SpawnPointId))) {
                placed.UnionWith(spawn.NpcList.Select(entry => entry.NpcId));
            }
            if (scriptSpawns.TryGetValue(map.XBlock, out HashSet<int>? spawned)) {
                foreach (EventSpawnPointNPC spawn in x.EventSpawns.Where(s => spawned.Contains(-1) || spawned.Contains(s.SpawnPointId))) {
                    placed.UnionWith(spawn.NpcList.Select(entry => entry.NpcId));
                }
            }
            foreach (MapMetadataSpawn spawn in map.Spawns.Where(s => s.Population > 0 && x.RegionSpawns.Contains(s.Id))) {
                foreach (string tag in spawn.Tags) {
                    if (tags.TryLookupTag(tag, out IReadOnlyCollection<int>? ids)) {
                        placed.UnionWith(ids.Where(id => npcById.TryGetValue(id, out NpcMetadata? npc)
                            && npc.Basic.Difficulty >= spawn.MinDifficulty && npc.Basic.Difficulty <= spawn.MaxDifficulty));
                    }
                }
            }
            placed.UnionWith(x.WeaponNpcs);
        }

        // Defense guards: the NPC id is the first field of "npcId,lifeSpan,distance" (ItemUseHandler).
        foreach (ItemMetadata item in context.ItemMetadata.AsEnumerable()) {
            if (item.Function?.Type != ItemFunction.DefenseGuard) {
                continue;
            }
            string[] parts = item.Function.Parameters.Split(',');
            if (parts.Length >= 3 && int.TryParse(parts[0], out int npcId) && npcId > 0) {
                placed.Add(npcId);
            }
        }
        return placed;
    }

    /// <summary>Spawn point ids of a script's spawn_monster, spawn_npc_range and change_monster actions; -1 = all.</summary>
    private static HashSet<int> SpawnedIds(string xml) {
        var ids = new HashSet<int>();
        var doc = new XmlDocument();
        try {
            doc.LoadXml(xml);
        } catch (XmlException) {
            return ids;
        }
        foreach (XmlElement action in doc.GetElementsByTagName("action").OfType<XmlElement>()) {
            string value = action.GetAttribute("name") switch {
                "spawn_monster" => action.GetAttribute("spawn_ids"),
                "spawn_npc_range" => action.GetAttribute("range_ids"),
                "change_monster" => action.GetAttribute("to_spawn_id"),
                _ => "",
            };
            if (value.Length == 0) {
                continue;
            }
            try {
                ids.UnionWith(TriggerFunctionMapping.ParseIntArray(value));
            } catch (FormatException) {
                // a script variable, not a number: nothing to count
            }
        }
        return ids;
    }

    /// <summary>Regular spawn point ids that the npc combine groups of a map name (CombineSpawnTable, FieldSpawnGroup).</summary>
    private static HashSet<int> CombineSpawnIds(CombineSpawnTable combine, int mapId) {
        var ids = new HashSet<int>();
        if (!combine.Groups.TryGetValue(mapId, out Dictionary<int, SpawnGroupMetadata>? groups)) {
            return ids;
        }
        foreach (SpawnGroupMetadata group in groups.Values.Where(group => group.Type == CombineSpawnGroupType.npc && group.TotalCount > 0)) {
            if (combine.Npcs.TryGetValue(group.GroupId, out Dictionary<int, SpawnNpcMetadata>? npcs)) {
                ids.UnionWith(npcs.Values.Select(npc => npc.SpawnId));
            }
        }
        return ids;
    }

    /// <summary>Start and complete NPCs that only event spawn points hold: the server spawns them for the quests
    /// (FieldManager.TriggerOnlyNpcSpawns, only in maps with event spawn points).</summary>
    private static HashSet<int> BuildQuestPlaced(List<MapMetadata> maps, Dictionary<string, XBlockEntities> entities,
            List<QuestMetadata> quests, Dictionary<int, NpcMetadata> npcById) {
        var questNpcIds = quests.SelectMany(quest => new[] { quest.Basic.StartNpc, quest.Basic.CompleteNpc }).Where(id => id > 0).ToHashSet();
        var placed = new HashSet<int>();
        foreach (string xblock in maps.Select(map => map.XBlock).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!entities.TryGetValue(xblock, out XBlockEntities? x) || x.EventSpawns.Count == 0) {
                continue;
            }
            placed.UnionWith(FieldManager.TriggerOnlyNpcSpawns(x.Spawns, x.EventSpawns,
                id => questNpcIds.Contains(id) && npcById.TryGetValue(id, out NpcMetadata? npc) && npc.Basic.Friendly > 0).Keys);
        }
        return placed;
    }

    /// <summary>World bosses that the time events spawn (WorldServer, FieldManager.SpawnWorldBoss: the first NPC).</summary>
    private static HashSet<int> BuildWorldBoss(ServerTableMetadataStorage tables) {
        return tables.TimeEventTable.WorldBoss.Values
            .Where(boss => boss.CycleTime > TimeSpan.Zero && boss.NpcIds.Length > 0 && boss.TargetMapIds.Length > 0 && boss.SpawnPointIds.Length > 0)
            .Select(boss => boss.NpcIds[0])
            .ToHashSet();
    }

    private static string QuoteCsvField(string field) {
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n')) {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}