using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DotRecast.Detour;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.DotRecast;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// PI-15 (BT-270): the Steamorbs that the Blaknov No. 1 scripts place on the long strips of the navmesh beside the train (spawn points 118 and
/// 119 of the Cargo Car, 147 to 149 and 172 and 173 of the Cabin) are no longer spawned. Reads the real map data, scripts and navmeshes.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class BlaknovSpawnDataTest {
    private static readonly string[] Maps = ["02000335_bf", "02000336_bf", "02000337_bf", "02010086_bf", "02010087_bf"];

    // A strip is a connected part of the mesh at least 150 navmesh units (15,000 game units) long; a car's decks are under 125.
    private const float StripLength = 150;
    private const int Steamorb = 24001503; // No. 2's Steamorb is 24002103

    // The dummies 24002110 of No. 2 (spawn 999 on a strip at x -12000) are not Steamorbs and stay as they are.
    private static readonly int[] Steamorbs = [24001503, 24002103];

    private static readonly Dictionary<string, int[]> Dropped = new() {
        ["02000335_bf"] = [118, 119],
        ["02000336_bf"] = [147, 148, 149, 172, 173],
    };

    private SqliteConnection connection = null!;
    private MetadataContext context = null!;

    [SetUp]
    public void SetUp() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Navmeshes", "02000335_bf.navmesh"))) {
            Assume.That(false, "Navmeshes submodule not checked out");
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    [TearDown]
    public void TearDown() {
        context?.Dispose();
        connection?.Dispose();
    }

    private static OfflineTriggers.Config Shipped() => OfflineTriggers.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-triggers.json")))!;

    /// <summary>The spawn ids the map's scripts spawn, with the shipped patches applied or not.</summary>
    private HashSet<int> Spawned(string xblock, bool patched) {
        var spawned = new HashSet<int>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select Name, Xml from trigger where MapXBlock = $x";
        command.Parameters.AddWithValue("$x", xblock);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            string xml = patched ? OfflineTriggers.Apply(xblock, reader.GetString(0), reader.GetString(1), Shipped()) : reader.GetString(1);
            foreach (Match action in Regex.Matches(xml, "<action name=\"spawn_monster\" spawn_ids=\"([^\"]*)\"")) {
                foreach (string id in action.Groups[1].Value.Split(',')) {
                    spawned.Add(int.Parse(id));
                }
            }
        }
        return spawned;
    }

    /// <summary>The length (navmesh units) of the connected part of the mesh the spawn point's nearest polygon belongs to, or 0 off the mesh.</summary>
    private static float PartLength(Navigation navigation, SpawnPointNPC spawn) {
        DtNavMeshQuery query = navigation.Crowd.GetNavMeshQuery();
        query.FindNearestPoly(DotRecastHelper.ToNavMeshSpace(spawn.Position), AgentNavigation.SearchExtents, navigation.Crowd.GetFilter(0), out long start, out _, out _);
        if (start == 0) {
            return 0;
        }

        DtNavMesh mesh = query.GetAttachedNavMesh();
        var seen = new HashSet<long> { start };
        var pending = new Queue<long>([start]);
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        while (pending.Count > 0) {
            mesh.GetTileAndPolyByRef(pending.Dequeue(), out DtMeshTile tile, out DtPoly poly);
            for (int i = 0; i < poly.vertCount; i++) {
                int vertex = poly.verts[i] * 3;
                minX = Math.Min(minX, tile.data.verts[vertex]);
                maxX = Math.Max(maxX, tile.data.verts[vertex]);
                minZ = Math.Min(minZ, tile.data.verts[vertex + 2]);
                maxZ = Math.Max(maxZ, tile.data.verts[vertex + 2]);
            }
            for (int link = poly.firstLink; link != -1; link = tile.links[link].next) {
                long next = tile.links[link].refs;
                if (next != 0 && seen.Add(next)) {
                    pending.Enqueue(next);
                }
            }
        }
        return Math.Max(maxX - minX, maxZ - minZ);
    }

    private IReadOnlyDictionary<int, SpawnPointNPC> SpawnPoints(string xblock) =>
        context.MapEntity.AsEnumerable().Where(entity => entity.XBlock == xblock && entity.Block is SpawnPointNPC)
            .Select(entity => (SpawnPointNPC) entity.Block).GroupBy(spawn => spawn.SpawnPointId).ToDictionary(group => group.Key, group => group.First());

    // The data puts these Steamorbs on a strip beside the train; the patches stop the scripts spawning them, and only them.
    [Test]
    public void DroppedPointsLieOnAStripAndAreNoLongerSpawned() {
        foreach ((string xblock, int[] ids) in Dropped) {
            var navigation = new Navigation(xblock);
            IReadOnlyDictionary<int, SpawnPointNPC> points = SpawnPoints(xblock);
            HashSet<int> before = Spawned(xblock, patched: false), after = Spawned(xblock, patched: true);
            foreach (int id in ids) {
                Assert.That(points[id].NpcList.Select(entry => entry.NpcId), Is.EqualTo(new[] { Steamorb }), $"{xblock} spawn {id} is a Steamorb");
                Assert.That(PartLength(navigation, points[id]), Is.GreaterThanOrEqualTo(StripLength), $"{xblock} spawn {id} lies on a strip");
                Assert.That(before, Does.Contain(id), $"{xblock} spawn {id} was spawned by a script");
                Assert.That(after, Does.Not.Contain(id), $"{xblock} spawn {id} is dropped");
            }
            Assert.That(before.Except(after).OrderBy(id => id), Is.EqualTo(ids.OrderBy(id => id)), $"{xblock}: nothing else is dropped");
        }
    }

    // After the patches no Steamorb of the five Blaknov maps is spawned at a point on a strip or off the mesh.
    [TestCaseSource(nameof(Maps))]
    public void NoSpawnedPointLiesOffTheTrain(string xblock) {
        var navigation = new Navigation(xblock);
        IReadOnlyDictionary<int, SpawnPointNPC> points = SpawnPoints(xblock);
        foreach (int id in Spawned(xblock, patched: true).Where(id => points.TryGetValue(id, out SpawnPointNPC? point) && point.NpcList.Any(entry => Steamorbs.Contains(entry.NpcId))).Order()) {
            Assert.That(PartLength(navigation, points[id]), Is.InRange(0.01f, StripLength - 0.01f), $"{xblock} spawn {id} (NPC {points[id].NpcList[0].NpcId}) lies on the train's part of the mesh");
        }
    }
}
