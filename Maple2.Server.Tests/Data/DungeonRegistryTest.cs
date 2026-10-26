using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Dungeon registry: one CSV row per dungeon room with what the data says about entry (lobby and field
/// maps, player counts), the encounter (NPC spawns and triggers in its fields), completion detection (a dungeon_clear
/// trigger action) and rewards (drop boxes known to the individual item drop table), with a status and reasons; plus a
/// Markdown summary. A report generator like DataSweepTest: data findings never fail the test, only a crash does.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class DungeonRegistryTest {
    private record Row(DungeonRoomMetadata Room, int Spawns, int Triggers, bool ClearTrigger, int[] RewardBoxes, string Status, List<string> Reasons);

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
        var tables = new TableMetadataStorage(context);

        var xblockByMap = context.MapMetadata.Select(map => new { map.Id, map.XBlock }).ToList()
            .GroupBy(map => map.Id).ToDictionary(group => group.Key, group => group.First().XBlock);
        // One pass over the map entities: NPC spawn points with at least one NPC, per xblock.
        var spawnsByXBlock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (MapEntity entity in context.MapEntity) {
            if (entity.Block is SpawnPointNPC { NpcList.Count: > 0 }) {
                spawnsByXBlock[entity.XBlock] = spawnsByXBlock.GetValueOrDefault(entity.XBlock) + 1;
            }
        }
        var triggersByXBlock = new Dictionary<string, (int Count, bool Clear)>(StringComparer.OrdinalIgnoreCase);
        foreach (TriggerMetadata trigger in context.TriggerMetadata) {
            (int count, bool clear) = triggersByXBlock.GetValueOrDefault(trigger.MapXBlock);
            // dungeon_clear and dungeon_clear_round mark the room cleared (TriggerFunctionMapping); the offline edition
            // clears a Dark Stream run with its last dark_stream_clear_round (TriggerContext.DarkStreamClearRound).
            triggersByXBlock[trigger.MapXBlock] = (count + 1,
                clear || trigger.Xml.Contains("\"dungeon_clear") || trigger.Xml.Contains("\"dark_stream_clear_round\""));
        }
        // DungeonManager gives a clear reward from each unlimited box id in both drop tables; limited boxes are not read.
        // ItemDropManager reads the server tables (individualItemDrop.xml, globalItemDrop), not the client table of the same name.
        var serverTables = new ServerTableMetadataStorage(context);
        var individualBoxes = serverTables.IndividualDropItemTable.Entries;
        var globalBoxes = serverTables.GlobalDropItemBoxTable.DropGroups;

        var rows = new List<Row>();
        foreach (DungeonRoomMetadata room in tables.DungeonRoomTable.Entries.Values.OrderBy(room => room.Id)) {
            var reasons = new List<string>();
            bool missing = false;
            int spawns = 0, triggers = 0;
            bool clear = false;
            foreach (int mapId in room.FieldIds.Prepend(room.LobbyFieldId).Where(id => id > 0)) {
                if (!xblockByMap.TryGetValue(mapId, out string? xblock)) {
                    reasons.Add(mapId == room.LobbyFieldId ? $"lobby map {mapId} missing" : $"field map {mapId} missing");
                    missing = true;
                    continue;
                }
                spawns += spawnsByXBlock.GetValueOrDefault(xblock);
                (int count, bool hasClear) = triggersByXBlock.GetValueOrDefault(xblock);
                triggers += count;
                clear |= hasClear;
            }
            int[] boxes = (room.Reward.UnlimitedDropBoxIds ?? []).Where(id => id > 0).ToArray();

            if (!clear) {
                reasons.Add("no clear trigger");
            }
            if (spawns == 0 && triggers == 0) {
                reasons.Add("no encounter data");
            }
            reasons.AddRange(boxes.Where(id => !individualBoxes.ContainsKey(id) && !globalBoxes.ContainsKey(id)).Select(id => $"reward box {id} unknown"));
            if (boxes.Length == 0 && room.Reward.Exp == 0 && room.Reward.Meso == 0) {
                reasons.Add("no reward");
            }
            if (room.Limit.MinUserCount > room.Limit.MaxUserCount) {
                reasons.Add($"min users {room.Limit.MinUserCount} above max {room.Limit.MaxUserCount}");
            }
            string status = missing ? "error" : reasons.Count > 0 ? "warning" : "ok";
            rows.Add(new Row(room, spawns, triggers, clear, boxes, status, reasons));
        }

        string outputDir = Environment.GetEnvironmentVariable("DUNGEON_REGISTRY_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(outputDir);
        var utf8 = new UTF8Encoding(false);

        using (var csv = new StreamWriter(Path.Combine(outputDir, "dungeon-registry.csv"), false, utf8) { NewLine = "\n" }) {
            csv.WriteLine("Id,GroupType,PlayType,Level,MinUsers,MaxUsers,Lobby,Fields,Spawns,Triggers,ClearTrigger,RewardBoxes,Status,Reasons");
            foreach (Row row in rows) {
                DungeonRoomMetadata room = row.Room;
                csv.WriteLine(string.Join(",", room.Id, room.GroupType, room.PlayType, room.Level, room.Limit.MinUserCount, room.Limit.MaxUserCount,
                    room.LobbyFieldId, string.Join(" ", room.FieldIds), row.Spawns, row.Triggers, row.ClearTrigger ? "yes" : "no",
                    string.Join(" ", row.RewardBoxes), row.Status, Quote(string.Join("; ", row.Reasons))));
            }
        }

        using (var md = new StreamWriter(Path.Combine(outputDir, "DUNGEON_REGISTRY.md"), false, utf8) { NewLine = "\n" }) {
            md.WriteLine("# Dungeon registry");
            md.WriteLine();
            md.WriteLine($"Database: {path}");
            md.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd}");
            md.WriteLine();
            md.WriteLine("## Status by group type");
            md.WriteLine("| Group type | Ok | Warning | Error |");
            md.WriteLine("|---|---:|---:|---:|");
            foreach (IGrouping<DungeonGroupType, Row> group in rows.GroupBy(row => row.Room.GroupType).OrderBy(group => group.Key.ToString())) {
                md.WriteLine($"| {group.Key} | {group.Count(r => r.Status == "ok")} | {group.Count(r => r.Status == "warning")} | {group.Count(r => r.Status == "error")} |");
            }
            md.WriteLine();
            md.WriteLine("## Reason kinds");
            md.WriteLine("| Reason | Count |");
            md.WriteLine("|---|---:|");
            foreach (IGrouping<string, string> kind in rows.SelectMany(row => row.Reasons).GroupBy(reason => Regex.Replace(reason, @"\d+", "N")).OrderByDescending(kind => kind.Count())) {
                md.WriteLine($"| {kind.Key} | {kind.Count()} |");
            }
            md.WriteLine();
            md.WriteLine("## Rooms with an error or a warning");
            md.WriteLine("| Id | Group type | Status | Reasons |");
            md.WriteLine("|---|---|---|---|");
            foreach (Row row in rows.Where(row => row.Status != "ok")) {
                md.WriteLine($"| {row.Room.Id} | {row.Room.GroupType} | {row.Status} | {string.Join("; ", row.Reasons)} |");
            }
        }

        Assert.That(rows.Count, Is.EqualTo(tables.DungeonRoomTable.Entries.Count));
        TestContext.WriteLine($"{rows.Count} rooms: ok {rows.Count(r => r.Status == "ok")}, warning {rows.Count(r => r.Status == "warning")}, error {rows.Count(r => r.Status == "error")}");
    }

    private static string Quote(string field) => field.Contains(',') || field.Contains('"') ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
