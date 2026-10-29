using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Data report test that writes the behaviour registry (NB-08): one CSV row per NPC with an AI (boss, monster or friendly NPC)
/// naming the AI steps the server does not run yet, a status (blocked / degraded / ok), and a summary. The waves of the
/// behaviour restoration lower its counts. Like QuestRegistryTest it is a report generator: data findings never fail it.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class BehaviorRegistryTest {
    // AI steps that do nothing in AiState (2026-10-22). A name leaves its set when its behaviour is implemented.
    // Blocking: boss phases stop. Summons and the master/slave values left with NB-10 and NB-12; the op form of the slave
    // count condition left with NB-16.
    private static readonly HashSet<string> Blocking = [];
    // Degrading: the encounter runs without them.
    private static readonly HashSet<string> Degrading = [
        "RideNode", "ShowVibrateNode", "HideVibrateAllNode", "ModifyRoomTimeNode", "CreateRandomRoomNode", "CreateInteractObjectNode",
    ];

    /// <summary>Counts of the unsupported AI steps in every tree of <paramref name="ai"/> (reserved, battle, battle end, presets).</summary>
    public static Dictionary<string, int> Unsupported(AiMetadata ai) {
        var counts = new Dictionary<string, int>();
        void Walk(AiMetadata.Entry entry) {
            string name = entry.GetType().Name;
            if (Blocking.Contains(name) || Degrading.Contains(name)) {
                counts[name] = counts.GetValueOrDefault(name) + 1;
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
        return counts;
    }

    [Test]
    public void Walk_CountsNestedEntries() {
        var tree = new AiMetadata.Node("battle", [
            new AiMetadata.RideNode("ride", [], default, false, []),
            new AiMetadata.Node("select", [new AiMetadata.RideNode("ride", [new AiMetadata.ShowVibrateNode("vibrate", [], 1)], default, false, [])]),
        ]);

        Dictionary<string, int> counts = Unsupported(new AiMetadata("test", [], [tree], [], []));
        Assert.That(counts, Is.EquivalentTo(new Dictionary<string, int> { ["RideNode"] = 2, ["ShowVibrateNode"] = 1 }));
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
        var unsupportedByAi = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> UnsupportedOf(string name) {
            if (!unsupportedByAi.TryGetValue(name, out Dictionary<string, int>? counts)) {
                counts = Unsupported(ais[name]);
                unsupportedByAi[name] = counts;
            }
            return counts;
        }

        var rows = new List<(int Id, string Name, string Kind, string AiPath, string RunsAi, string Status, Dictionary<string, int> Unsupported)>();
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
            rows.Add((npc.Id, npc.Name ?? "", kind, npc.AiPath, runs ?? "default-ai (missing)", status, counts));
        }

        string outputDir = Environment.GetEnvironmentVariable("BEHAVIOR_REGISTRY_OUT") ?? TestContext.CurrentContext.WorkDirectory;
        Directory.CreateDirectory(outputDir);
        using (var csv = new StreamWriter(Path.Combine(outputDir, "behavior-registry.csv"), false, new UTF8Encoding(false)) { NewLine = "\n" }) {
            csv.WriteLine("NpcId,Name,Kind,AiPath,RunsAi,Status,Unsupported");
            foreach (var row in rows) {
                string unsupported = string.Join(";", row.Unsupported.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));
                csv.WriteLine(string.Join(",", row.Id, Quote(row.Name), row.Kind, Quote(row.AiPath), Quote(row.RunsAi), row.Status, unsupported));
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
        File.WriteAllText(Path.Combine(outputDir, "BEHAVIOR_REGISTRY.md"), md.ToString(), new UTF8Encoding(false));

        foreach (string kind in kinds) {
            TestContext.WriteLine($"{kind}: " + string.Join(", ", rows.Where(row => row.Kind == kind).GroupBy(row => row.Status)
                .OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}")));
        }
        Assert.That(rows, Is.Not.Empty, "no NPC with an AI in the data");
    }

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
