using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Read-only integrity sweep over maple-data.db. It is a report generator: data findings never fail the test, only crashes in the
/// sweep code do. Writes sweep-report.md to the test working directory. Skipped when no data DB is found (see <see cref="DataDb"/>).
/// </summary>
[Category("Data")]
[NonParallelizable]
public class DataSweepTest {
    private enum Severity { Error, Warning, Info }

    private record Finding(Severity Severity, string Check, string Message);

    private const int MaxListed = 300; // per severity and check; counts stay exact

    private readonly List<Finding> findings = [];

    private void Add(Severity severity, string check, string message) => findings.Add(new Finding(severity, check, message));

    [Test]
    public void Sweep() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var tables = new TableMetadataStorage(context);

        var npcIds = context.NpcMetadata.Select(npc => npc.Id).ToHashSet();
        var maps = context.MapMetadata.Select(map => new { map.Id, map.XBlock }).ToList();
        var mapIds = maps.Select(map => map.Id).ToHashSet();
        var xblockByMap = maps.GroupBy(map => map.Id).ToDictionary(group => group.Key, group => group.First().XBlock);

        // One pass over map entities: portals and NPC spawns per xblock.
        var portalIdsByXBlock = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var portals = new List<(string XBlock, Portal Portal)>();
        var missingSpawnNpcs = new Dictionary<int, (int Count, string Example)>();
        var xblocksWithNpcSpawns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entityXBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int spawnPoints = 0, emptySpawnPoints = 0, entities = 0;
        foreach (MapEntity entity in context.MapEntity) {
            entities++;
            entityXBlocks.Add(entity.XBlock);
            switch (entity.Block) {
                case Portal portal:
                    portals.Add((entity.XBlock, portal));
                    if (!portalIdsByXBlock.TryGetValue(entity.XBlock, out HashSet<int>? ids)) {
                        portalIdsByXBlock[entity.XBlock] = ids = [];
                    }
                    ids.Add(portal.Id);
                    break;
                case SpawnPointNPC spawn:
                    spawnPoints++;
                    xblocksWithNpcSpawns.Add(entity.XBlock);
                    if (spawn.NpcList.Count == 0) {
                        emptySpawnPoints++;
                    }
                    foreach (SpawnPointNPCListEntry entry in spawn.NpcList.Where(entry => !npcIds.Contains(entry.NpcId))) {
                        missingSpawnNpcs[entry.NpcId] = missingSpawnNpcs.TryGetValue(entry.NpcId, out var seen) ? (seen.Count + 1, seen.Example) : (1, entity.XBlock);
                    }
                    break;
            }
        }
        Add(Severity.Info, "Overview", $"{npcIds.Count} npcs, {maps.Count} maps, {entities} map entities, {portals.Count} portals, {spawnPoints} NPC spawn points");

        CheckQuests(context, tables, npcIds, mapIds, xblockByMap, portalIdsByXBlock);
        CheckPortals(portals, mapIds, xblockByMap, portalIdsByXBlock);

        foreach ((int npcId, (int count, string example)) in missingSpawnNpcs.OrderBy(pair => pair.Key)) {
            Add(Severity.Error, "Spawn -> NPC", $"npc {npcId} referenced by {count} spawn entries is not in the npc table (e.g. in {example})");
        }
        Add(Severity.Info, "Spawn -> NPC", $"{spawnPoints} NPC spawn points checked against {npcIds.Count} npcs");
        if (emptySpawnPoints > 0) {
            Add(Severity.Warning, "Spawn -> NPC", $"{emptySpawnPoints} NPC spawn points have an empty NPC list");
        }

        CheckTriggers(context, xblockByMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase));
        CheckNavmeshes(maps.Select(map => (map.Id, map.XBlock)).ToList(), xblocksWithNpcSpawns);
        CheckItemOptions(context, tables);
        ListDungeons(tables, mapIds);

        int orphanXBlocks = entityXBlocks.Except(xblockByMap.Values, StringComparer.OrdinalIgnoreCase).Count();
        if (orphanXBlocks > 0) {
            Add(Severity.Warning, "Overview", $"{orphanXBlocks} xblocks have map entities but no map row");
        }

        string report = BuildReport(path!);
        string outPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "sweep-report.md");
        File.WriteAllText(outPath, report);
        TestContext.Out.WriteLine(string.Join('\n', report.Split('\n').TakeWhile(line => !line.StartsWith("## Errors"))));
        Assert.That(File.Exists(outPath), Is.True);
    }

    private void CheckQuests(MetadataContext context, TableMetadataStorage tables, HashSet<int> npcIds, HashSet<int> mapIds, Dictionary<int, string> xblockByMap, Dictionary<string, HashSet<int>> portalIdsByXBlock) {
        const string check = "Quest references";
        int quests = 0, disabled = 0;

        void Npc(QuestMetadata quest, string field, int id) {
            if (id > 0 && !npcIds.Contains(id)) {
                Add(Severity.Error, check, $"quest {quest.Id} ({quest.Name}) {field}: npc {id} missing");
            }
        }
        void Map(QuestMetadata quest, string field, int id) {
            if (id > 0 && !mapIds.Contains(id)) {
                Add(Severity.Error, check, $"quest {quest.Id} ({quest.Name}) {field}: map {id} missing");
            }
        }
        void PortalRef(QuestMetadata quest, string field, int mapId, int portalId) {
            if (portalId > 0 && mapIds.Contains(mapId) && !(portalIdsByXBlock.TryGetValue(xblockByMap[mapId], out HashSet<int>? ids) && ids.Contains(portalId))) {
                Add(Severity.Error, check, $"quest {quest.Id} ({quest.Name}) {field}: portal {portalId} missing on map {mapId}");
            }
        }

        foreach (QuestMetadata quest in context.QuestMetadata) {
            quests++;
            Npc(quest, "StartNpc", quest.Basic.StartNpc);
            Npc(quest, "CompleteNpc", quest.Basic.CompleteNpc);
            foreach (int id in quest.Basic.CompleteMaps ?? []) {
                Map(quest, "CompleteMaps", id);
            }
            foreach (int id in quest.Basic.ProgressMaps ?? []) {
                Map(quest, "ProgressMaps", id);
            }
            if (quest.GoToNpc.Enabled) {
                Map(quest, "GoToNpc.MapId", quest.GoToNpc.MapId);
                PortalRef(quest, "GoToNpc.PortalId", quest.GoToNpc.MapId, quest.GoToNpc.PortalId);
            }
            if (quest.GoToDungeon.MapId > 0 && !tables.DungeonRoomTable.Entries.ContainsKey(quest.GoToDungeon.MapId)) { // holds a dungeon room id
                Add(Severity.Error, check, $"quest {quest.Id} ({quest.Name}) GoToDungeon.MapId: dungeon room {quest.GoToDungeon.MapId} missing");
            }
            Map(quest, "RemoteAccept.MapId", quest.RemoteAccept.MapId);
            Map(quest, "RemoteComplete.MapId", quest.RemoteComplete.MapId);
            if (quest.Dispatch != null) {
                Map(quest, "Dispatch.MapId", quest.Dispatch.MapId);
                PortalRef(quest, "Dispatch.PortalId", quest.Dispatch.MapId, quest.Dispatch.PortalId);
            }
            if (quest.SummonPortal != null) {
                Map(quest, "SummonPortal.MapId", quest.SummonPortal.MapId);
                PortalRef(quest, "SummonPortal.PortalId", quest.SummonPortal.MapId, quest.SummonPortal.PortalId);
            }
            foreach (ConditionMetadata condition in quest.Conditions) {
                string type = condition.Type.ToString();
                if (type is "npc" or "interact_npc") { // codes are npc ids, target is a map id (0 = any)
                    foreach (int id in condition.Codes?.Integers ?? []) {
                        Npc(quest, $"condition {type} code", id);
                    }
                    foreach (int id in condition.Target?.Integers ?? []) {
                        Map(quest, $"condition {type} target", id);
                    }
                }
            }
            if (quest.Basic.Disabled) {
                disabled++;
            }
        }
        Add(Severity.Info, check, $"{quests} quests scanned, {disabled} marked Disabled");
    }

    private void CheckPortals(List<(string XBlock, Portal Portal)> portals, HashSet<int> mapIds, Dictionary<int, string> xblockByMap, Dictionary<string, HashSet<int>> portalIdsByXBlock) {
        const string check = "Portal -> map";
        Add(Severity.Info, check, $"{portals.Count(p => p.Portal.TargetMapId > 0)} portals with a target map checked");
        foreach ((string xblock, Portal portal) in portals) {
            if (portal.TargetMapId <= 0) {
                continue;
            }
            if (!mapIds.Contains(portal.TargetMapId)) {
                Add(Severity.Error, check, $"{xblock} portal {portal.Id} targets map {portal.TargetMapId}, which is not in the map table{(portal.Enable ? "" : " (portal disabled)")}");
            } else if (portal.TargetPortalId > 0 && !(portalIdsByXBlock.TryGetValue(xblockByMap[portal.TargetMapId], out HashSet<int>? ids) && ids.Contains(portal.TargetPortalId))) {
                Add(Severity.Error, check, $"{xblock} portal {portal.Id} targets portal {portal.TargetPortalId} on map {portal.TargetMapId}, which has no such portal{(portal.Enable ? "" : " (portal disabled)")}");
            }
        }
    }

    private void CheckTriggers(MetadataContext context, HashSet<string> mapXBlocks) {
        const string check = "Trigger names";
        var unknown = new Dictionary<(string Kind, string Name), (int Count, string Example)>();
        var usedActions = new HashSet<string>();
        var usedConditions = new HashSet<string>();
        int count = 0;
        foreach (TriggerMetadata trigger in context.TriggerMetadata) {
            count++;
            var document = new XmlDocument();
            try {
                document.LoadXml(trigger.Xml);
                // Same entry point the game uses; catches constructors that throw on odd attributes.
                TriggerCache.ParseTrigger(trigger.MapXBlock, trigger.Name, document);
            } catch (Exception ex) when (ex is XmlException or ArgumentException or FormatException or InvalidOperationException or NullReferenceException) {
                Add(Severity.Error, check, $"{trigger.MapXBlock}/{trigger.Name} does not parse: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            foreach ((string kind, HashSet<string> used, ICollection<string> known) in new[] {
                         ("action", usedActions, (ICollection<string>) TriggerFunctionMapping.ActionMap.Keys),
                         ("condition", usedConditions, TriggerFunctionMapping.ConditionMap.Keys),
                     }) {
                foreach (XmlNode node in document.SelectNodes($"//{kind}")!) {
                    string? name = node.Attributes?["name"]?.Value;
                    if (name == null) {
                        continue;
                    }
                    used.Add(name);
                    if (!known.Contains(name)) {
                        unknown[(kind, name)] = unknown.TryGetValue((kind, name), out var seen) ? (seen.Count + 1, seen.Example) : (1, $"{trigger.MapXBlock}/{trigger.Name}");
                    }
                }
            }
            if (!mapXBlocks.Contains(trigger.MapXBlock)) {
                Add(Severity.Warning, check, $"trigger script {trigger.MapXBlock}/{trigger.Name} belongs to an xblock with no map row");
            }
        }

        foreach (((string kind, string name), (int n, string example)) in unknown.OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Name)) {
            Add(Severity.Error, check, $"unknown {kind} '{name}' used {n} times (e.g. {example}); the game logs an error and skips it");
        }
        foreach (string name in TriggerFunctionMapping.ActionMap.Keys.Except(usedActions).Order()) {
            Add(Severity.Warning, check, $"action '{name}' is implemented but no trigger script uses it");
        }
        foreach (string name in TriggerFunctionMapping.ConditionMap.Keys.Except(usedConditions).Order()) {
            Add(Severity.Warning, check, $"condition '{name}' is implemented but no trigger script uses it");
        }
        Add(Severity.Info, "Overview", $"{count} trigger scripts parsed ({usedActions.Count} distinct actions, {usedConditions.Count} distinct conditions in use)");
    }

    private static string? FindNavmeshDir() {
        string beside = Path.Combine(AppContext.BaseDirectory, "Navmeshes");
        if (Directory.Exists(beside)) {
            return beside;
        }
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "Maple2.Server.Game", "Navmeshes");
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    private void CheckNavmeshes(List<(int Id, string XBlock)> maps, HashSet<string> xblocksWithNpcSpawns) {
        const string check = "Navmesh files";
        string? dir = FindNavmeshDir();
        if (dir == null) {
            Add(Severity.Info, check, "Navmeshes folder not found (submodule not checked out); check skipped");
            return;
        }
        var files = Directory.EnumerateFiles(dir, "*.navmesh").Select(file => Path.GetFileNameWithoutExtension(file)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // FieldManager always builds a Navigation for the map xblock, so a missing file fails field creation.
        foreach ((int id, string xblock) in maps.Where(map => !files.Contains(map.XBlock)).OrderBy(map => map.Id)) {
            Add(Severity.Error, check, $"map {id} xblock '{xblock}' has no .navmesh file{(xblocksWithNpcSpawns.Contains(xblock) ? " (map has NPC spawns)" : "")}");
        }
        var used = maps.Select(map => map.XBlock).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files.Where(file => !used.Contains(file)).Order()) {
            Add(Severity.Warning, check, $"navmesh '{file}' belongs to no map xblock");
        }
        Add(Severity.Info, check, $"{files.Count} navmesh files found for {used.Count} distinct map xblocks");
    }

    private void CheckItemOptions(MetadataContext context, TableMetadataStorage tables) {
        const string check = "Item option tables";
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>> constants = tables.ItemOptionConstantTable.Options;
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOption>> statics = tables.ItemOptionStaticTable.Options;
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOption>> randoms = tables.ItemOptionRandomTable.Options;
        int gear = 0, combatGear = 0, noOption = 0, implicitNone = 0;
        var byRarity = new int[7];
        var gearWithoutRarity = new List<string>();
        foreach (ItemMetadata item in context.ItemMetadata) {
            if (item.SlotNames.Length == 0) {
                continue;
            }
            gear++;
            bool combat = item.Property.GearScore > 0;
            if (combat) {
                combatGear++;
            }
            if (item.Option == null) {
                noOption++;
                continue;
            }
            ItemMetadataOption option = item.Option;
            var rarities = new HashSet<int>();
            void Look<T>(IReadOnlyDictionary<int, IReadOnlyDictionary<int, T>> table, string name, int id) {
                if (id <= 0) {
                    return;
                }
                if (table.TryGetValue(id, out IReadOnlyDictionary<int, T>? byId)) {
                    rarities.UnionWith(byId.Keys);
                } else if (id == item.Id) {
                    implicitNone++; // ingest default: option id = item id, no table row means no options of this kind
                } else {
                    Add(Severity.Error, check, $"gear {item.Id} ({item.Name}) references {name} option id {id}, which is missing from the {name} table");
                }
            }
            if (option.ConstantId >= 7) { // 1-6 on non-gear is a rarity, not a table id
                Look(constants, "constant", option.ConstantId);
            }
            Look(statics, "static", option.StaticId);
            Look(randoms, "random", option.RandomId);
            if (combat) {
                foreach (int rarity in rarities.Where(rarity => rarity is > 0 and < 7)) {
                    byRarity[rarity]++;
                }
            }
            if (combat && !rarities.Any(rarity => rarity is > 0 and < 7)) {
                gearWithoutRarity.Add($"{item.Id} ({item.Name})");
            }
        }
        if (gearWithoutRarity.Count > 0) {
            Add(Severity.Warning, check, $"{gearWithoutRarity.Count} gear items (GearScore > 0) resolve no rarity 1-6 from their option tables (e.g. {string.Join(", ", gearWithoutRarity.Take(5))})");
        }
        Add(Severity.Info, check, $"{gear} equippable items ({combatGear} with GearScore > 0); {noOption} without an Option block; {implicitNone} implicit option ids (id = item id, no table row) ignored");
        for (int rarity = 1; rarity <= 6; rarity++) {
            Add(Severity.Info, check, $"rarity {rarity}: {byRarity[rarity]} GearScore>0 items have at least one option table row");
        }
        Add(Severity.Info, check, $"option tables: {constants.Count} constant, {statics.Count} static, {randoms.Count} random ids");
    }

    private void ListDungeons(TableMetadataStorage tables, HashSet<int> mapIds) {
        const string check = "Dungeons";
        foreach (DungeonRoomMetadata room in tables.DungeonRoomTable.Entries.Values.OrderBy(room => room.Id)) {
            Add(Severity.Info, check, $"dungeon {room.Id}: group={room.GroupType} play={room.PlayType} level={room.Level} users={room.Limit.MinUserCount}-{room.Limit.MaxUserCount} lobby={room.LobbyFieldId} fields=[{string.Join(",", room.FieldIds)}]");
            if (room.Limit.MinUserCount > room.Limit.MaxUserCount) {
                Add(Severity.Warning, check, $"dungeon {room.Id}: MinUserCount {room.Limit.MinUserCount} > MaxUserCount {room.Limit.MaxUserCount}");
            }
            if (room.LobbyFieldId > 0 && !mapIds.Contains(room.LobbyFieldId)) {
                Add(Severity.Error, check, $"dungeon {room.Id}: lobby map {room.LobbyFieldId} missing");
            }
            foreach (int fieldId in room.FieldIds.Where(fieldId => fieldId > 0 && !mapIds.Contains(fieldId))) {
                Add(Severity.Error, check, $"dungeon {room.Id}: field map {fieldId} missing");
            }
        }
    }

    private string BuildReport(string dbPath) {
        var sb = new StringBuilder();
        sb.AppendLine("# Data sweep report");
        sb.AppendLine();
        sb.AppendLine($"Database: {Path.GetFileName(dbPath)} ({new FileInfo(dbPath).Length / (1024 * 1024)} MB, opened read-only)");
        sb.AppendLine();
        sb.AppendLine("Severity: error = a referenced thing is missing; warning = unused, unimplemented or suspicious; info = listings.");
        sb.AppendLine();
        sb.AppendLine("| Check | Errors | Warnings | Info |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (IGrouping<string, Finding> group in findings.GroupBy(finding => finding.Check)) {
            sb.AppendLine($"| {group.Key} | {group.Count(f => f.Severity == Severity.Error)} | {group.Count(f => f.Severity == Severity.Warning)} | {group.Count(f => f.Severity == Severity.Info)} |");
        }
        foreach (Severity severity in Enum.GetValues<Severity>()) {
            sb.AppendLine();
            sb.AppendLine($"## {severity}s");
            foreach (IGrouping<string, Finding> group in findings.Where(f => f.Severity == severity).GroupBy(f => f.Check)) {
                sb.AppendLine();
                sb.AppendLine($"### {group.Key} ({group.Count()})");
                sb.AppendLine();
                int limit = severity == Severity.Info ? int.MaxValue : MaxListed;
                foreach (Finding finding in group.Take(limit)) {
                    sb.AppendLine($"- {finding.Message}");
                }
                if (group.Count() > limit) {
                    sb.AppendLine($"- ... {group.Count() - limit} more");
                }
            }
        }
        return sb.ToString();
    }
}
