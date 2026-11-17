using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>VR-04 (BT-241): a gauge map's spawns can reach its gate. A Shadow Expedition gauge fills by the kill weights of SE-01 (5, 10 or 20
/// per kill) from the moment a script opens it. For each map that opens a gauge the test sums, with the kill rule itself, what the scripts'
/// spawn actions put on the field. Own script: the monsters spawned from the Open on to the Close, and those spawned before the Open that no
/// step waits for or removes; best case: every spawn action of every script of the map, each spawn point at its heaviest monsters. A spawn
/// point places NpcCount of the monsters of its list, so its share in the own-script sum is the mean weight of the list times that count.
/// A map whose best case falls short of its gate cannot be finished by kills (a stall, in <see cref="KnownStalls"/>); one whose own script
/// falls short but whose other scripts can make up for it is at risk (<see cref="KnownAtRisk"/>). A new one in either list fails the test.</summary>
[Category("Data")]
[NonParallelizable]
public class GaugeReachTest {
    private const string OpenAction = "shadow_expedition_open_boss_gauge";
    private const string CloseAction = "shadow_expedition_close_boss_gauge";

    // Not summed: the field games' gauges never start offline (FieldGameStart is never set, SE-01d); 52010038's gauge fills from the score
    // dummies a heal object spawns and kills (SE-03), not from spawned monsters; a gauge map with no spawn point of its own (52020001, 52020016,
    // 99999883, 02000543) has nothing to sum.
    private static readonly HashSet<string> Excluded = ["02020061_bf", "02020062_bf", "52010038_qd"];

    // 02000461 (BT-241): about 1,210 points reachable by its own script (1,380 in the best case of every script) for a gate of 1,400. Taken as
    // it is: weights from data are the maintainer's call (D-10).
    private static readonly string[] KnownStalls = ["02000461_bf"];

    // 02000403 and 52100041 spawn their waves from separate scripts (event_01 to event_05), which the gauge's own script only starts: its own
    // 665 points reach the gate of 1,000 only if those waves are spawned while the gauge is open.
    private static readonly string[] KnownAtRisk = ["02000403_bf", "52100041_qd"];

    private sealed record SpawnPoint(IList<SpawnPointNPCListEntry> NpcList, int NpcCount);

    private sealed record MapResult(string XBlock, int Gate, double Lower, double Upper, double AfterOpen, double Carried);

    private static IEnumerable<int> Ids(string? list) {
        foreach (string part in (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string[] range = part.Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out int first) && int.TryParse(range[1], out int last)) {
                for (int id = first; id <= last; id++) {
                    yield return id;
                }
            } else if (int.TryParse(part, out int single)) {
                yield return single; // a list of "all" or a name names no spawn point
            }
        }
    }

    /// <summary>The states reachable from <paramref name="start"/> by transitions; a state that holds a stop action is visited but not left,
    /// and the state named <paramref name="barrier"/> is not entered.</summary>
    private static List<XmlElement> Reach(Dictionary<string, XmlElement> states, string start, string? stopAction, string? barrier = null) {
        var seen = new List<XmlElement>();
        var queue = new Queue<string>([start]);
        while (queue.Count > 0) {
            string name = queue.Dequeue();
            if (name == barrier || !states.TryGetValue(name, out XmlElement? state) || seen.Contains(state)) {
                continue;
            }
            seen.Add(state);
            if (stopAction != null && state.SelectSingleNode($".//action[@name='{stopAction}']") != null) {
                continue;
            }
            foreach (XmlNode transition in state.SelectNodes(".//transition")!) {
                queue.Enqueue(transition.Attributes?["state"]?.Value ?? string.Empty);
            }
        }
        return seen;
    }

    private static (HashSet<int> Spawned, HashSet<int> Removed) Actions(IEnumerable<XmlElement> states) {
        var spawned = new HashSet<int>();
        var removed = new HashSet<int>();
        foreach (XmlElement state in states) {
            foreach (XmlNode action in state.SelectNodes(".//action")!) {
                string? name = action.Attributes?["name"]?.Value;
                if (name == "spawn_monster") {
                    spawned.UnionWith(Ids(action.Attributes?["spawn_ids"]?.Value));
                } else if (name == "spawn_npc_range") {
                    spawned.UnionWith(Ids(action.Attributes?["range_ids"]?.Value));
                } else if (name == "destroy_monster") {
                    removed.UnionWith(Ids(action.Attributes?["spawn_ids"]?.Value));
                }
            }
            foreach (XmlNode condition in state.SelectNodes(".//condition[@name='monster_dead']")!) {
                removed.UnionWith(Ids(condition.Attributes?["spawn_ids"]?.Value)); // killed before the state is left
            }
        }
        return (spawned, removed);
    }

    private static double Weight(SpawnPoint point, Dictionary<int, int> weights, bool best) {
        var pool = new List<int>();
        foreach (SpawnPointNPCListEntry entry in point.NpcList) {
            pool.AddRange(Enumerable.Repeat(weights.GetValueOrDefault(entry.NpcId), entry.Count));
        }
        if (pool.Count == 0) {
            return 0;
        }
        int count = Math.Min(point.NpcCount, pool.Count);
        return best ? pool.OrderByDescending(weight => weight).Take(count).Sum() : pool.Average() * count;
    }

    private static List<MapResult> Results(MetadataContext context) {
        var results = new List<MapResult>();
        List<TriggerMetadata> triggers = context.TriggerMetadata.Where(trigger => trigger.Xml.Contains(OpenAction)).ToList();
        foreach (TriggerMetadata opener in triggers.OrderBy(trigger => trigger.MapXBlock)) {
            var points = new Dictionary<int, SpawnPoint>();
            foreach (MapEntity entity in context.MapEntity.Where(entity => entity.XBlock == opener.MapXBlock).AsEnumerable()) {
                if (entity.Block is EventSpawnPointNPC spawn) {
                    points[spawn.SpawnPointId] = new SpawnPoint(spawn.NpcList, spawn.NpcCount);
                }
            }
            if (points.Count == 0 || Excluded.Contains(opener.MapXBlock)) {
                continue;
            }

            var weights = new Dictionary<int, int>();
            foreach (int npcId in points.Values.SelectMany(point => point.NpcList).Select(entry => entry.NpcId).Distinct()) {
                NpcMetadata? npc = context.NpcMetadata.SingleOrDefault(row => row.Id == npcId);
                if (npc != null) {
                    weights[npcId] = FieldScore.KillPoints(npc, 0);
                }
            }

            var document = new XmlDocument();
            document.LoadXml(opener.Xml);
            XmlElement[] all = document.SelectNodes("/ms2/state")!.Cast<XmlElement>().ToArray();
            var states = all.ToDictionary(state => state.GetAttribute("name"), state => state);
            XmlElement open = all.First(state => state.SelectSingleNode($".//action[@name='{OpenAction}']") != null);
            int gate = int.Parse(open.SelectSingleNode($".//action[@name='{OpenAction}']")!.Attributes!["max_gauge_point"]!.Value);

            // From the Open on to the Close, and the spawns before the Open that stay alive: not awaited by a monster_dead and not destroyed.
            List<XmlElement> after = Reach(states, open.GetAttribute("name"), CloseAction);
            List<XmlElement> before = Reach(states, all[0].GetAttribute("name"), null, open.GetAttribute("name"));
            HashSet<int> afterSpawns = Actions(after).Spawned;
            (HashSet<int> spawnedBefore, HashSet<int> removed) = Actions(before);
            HashSet<int> carried = spawnedBefore.Except(removed).Except(afterSpawns).ToHashSet();

            double Sum(IEnumerable<int> ids, bool best) => ids.Where(points.ContainsKey).Sum(id => Weight(points[id], weights, best));

            // The upper bound: every spawn action of every script of the map, killed or not.
            var everything = new HashSet<int>();
            foreach (TriggerMetadata script in context.TriggerMetadata.Where(row => row.MapXBlock == opener.MapXBlock && row.Xml.Contains("spawn_")).AsEnumerable()) {
                var scriptDocument = new XmlDocument();
                scriptDocument.LoadXml(script.Xml);
                everything.UnionWith(Actions(scriptDocument.SelectNodes("/ms2/state")!.Cast<XmlElement>()).Spawned);
            }

            double afterSum = Sum(afterSpawns, false);
            double carriedSum = Sum(carried, false);
            results.Add(new MapResult(opener.MapXBlock, gate, afterSum + carriedSum, Sum(everything, true), afterSum, carriedSum));
        }
        return results;
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
    public void TheSpawnsCanReachTheGate() {
        WithData(context => {
            List<MapResult> results = Results(context);
            foreach (MapResult result in results) {
                string verdict = result.Upper < result.Gate ? "STALL" : result.Lower < result.Gate ? "AT RISK" : "ok";
                TestContext.Out.WriteLine($"gauge map {result.XBlock}: gate {result.Gate}, own script {result.Lower:0} (after the Open {result.AfterOpen:0}, carried over {result.Carried:0}), "
                                          + $"best case of every script {result.Upper:0}, {verdict}");
            }
            Assert.That(results.Count, Is.GreaterThanOrEqualTo(5), "the playable gauge maps");
            Assert.That(results.Where(result => result.Upper < result.Gate).Select(result => result.XBlock), Is.EquivalentTo(KnownStalls),
                "a map whose spawns cannot reach its gate (BT-241) is a finding: it is listed here, not fixed");
            Assert.That(results.Where(result => result.Upper >= result.Gate && result.Lower < result.Gate).Select(result => result.XBlock), Is.EquivalentTo(KnownAtRisk));
        });
    }
}
