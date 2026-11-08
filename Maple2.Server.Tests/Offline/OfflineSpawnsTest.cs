using System.IO;
using System.Linq;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class OfflineSpawnsTest {
    [Test]
    public void Parse_AndSpawnPointAtTheRegion() {
        string json = """
            {
              // Test comment
              "Spawns": [
                { "MapId": 1, "NpcId": 2, "Count": 3, "RegionId": 4, "RegenSeconds": 30 }
              ]
            }
            """;

        OfflineSpawns.Config? config = OfflineSpawns.Parse(json);
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Spawns.Length, Is.EqualTo(1));

        OfflineSpawns.Spawn spawn = config.Spawns[0];
        Assert.That(spawn.MapId, Is.EqualTo(1));
        Assert.That(spawn.NpcId, Is.EqualTo(2));
        Assert.That(spawn.Count, Is.EqualTo(3));
        Assert.That(spawn.RegionId, Is.EqualTo(4));
        Assert.That(spawn.RegenSeconds, Is.EqualTo(30));
        Assert.That(spawn.GrindOnly, Is.False);

        // Test ToSpawnPoint conversion
        Ms2RegionSpawn region = new(4, false, new Vector3(10, 20, 30), Vector3.Zero);
        var spawnPoint = OfflineSpawns.ToSpawnPoint(spawn, region);

        Assert.That(spawnPoint.EntityId, Is.EqualTo("offline-spawn-1-2-4"));
        Assert.That(spawnPoint.SpawnPointId, Is.EqualTo(0));
        Assert.That(spawnPoint.Position, Is.EqualTo(new Vector3(10, 20, 30)));
        Assert.That(spawnPoint.Visible, Is.True);
        Assert.That(spawnPoint.SpawnOnFieldCreate, Is.True);
        Assert.That(spawnPoint.SpawnRadius, Is.EqualTo(150));
        Assert.That(spawnPoint.NpcList.Count, Is.EqualTo(1));
        Assert.That(spawnPoint.NpcList[0].NpcId, Is.EqualTo(2));
        Assert.That(spawnPoint.NpcList[0].Count, Is.EqualTo(3));
        Assert.That(spawnPoint.RegenCheckTime, Is.EqualTo(30));
    }

    [Test]
    public void ShippedFile_HasTheQuestSpawns() {
        string json = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-spawns.json"));
        OfflineSpawns.Config? config = OfflineSpawns.Parse(json);
        Assert.That(config, Is.Not.Null);

        // Check that we have the required spawns for QF-06
        var spawns = config.Spawns.ToList();
        Assert.That(spawns.Count, Is.EqualTo(3));

        // Find Wiggler spawn (29000022 in map 2000017)
        var wigglerSpawn = spawns.FirstOrDefault(s => s.NpcId == 29000022 && s.MapId == 2000017);
        Assert.That(wigglerSpawn, Is.Not.Null);
        Assert.That(wigglerSpawn.Count, Is.EqualTo(4));
        Assert.That(wigglerSpawn.RegionId, Is.EqualTo(6));
        Assert.That(wigglerSpawn.RegenSeconds, Is.EqualTo(30));

        // Find Devilin Chief spawn (23000008 in map 2000115)
        var devilinSpawn = spawns.FirstOrDefault(s => s.NpcId == 23000008 && s.MapId == 2000115);
        Assert.That(devilinSpawn, Is.Not.Null);
        Assert.That(devilinSpawn.Count, Is.EqualTo(1));
        Assert.That(devilinSpawn.RegionId, Is.EqualTo(15));
        Assert.That(devilinSpawn.RegenSeconds, Is.EqualTo(900));

        // The quest rows stay in both modes, at their own times.
        Assert.That(wigglerSpawn.GrindOnly, Is.False);
        Assert.That(devilinSpawn.GrindOnly, Is.False);
    }

    [Test]
    public void ShippedFile_HasDrPippyForHisTrophy() {
        OfflineSpawns.Config? config = OfflineSpawns.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-spawns.json")));
        Assert.That(config, Is.Not.Null);

        OfflineSpawns.Spawn pippy = config.Spawns.Single(spawn => spawn.NpcId == 22400062);
        Assert.That((pippy.MapId, pippy.Count, pippy.RegionId, pippy.RegenSeconds, pippy.GrindOnly), Is.EqualTo((2000142, 1, 14, 300, true)));
    }

    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void AGrindOnlyRowNeedsGrindRates(bool grindOnly, bool grindRates, bool expected) {
        Assert.That(OfflineSpawns.Applies(new OfflineSpawns.Spawn(1, 2, 1, 4, 300, GrindOnly: grindOnly), grindRates), Is.EqualTo(expected));
    }

    [Test]
    public void AGrindOnlyRowComesBackAsAFieldEliteDoes() {
        Ms2RegionSpawn region = new(4, false, Vector3.Zero, Vector3.Zero);
        var spawn = new OfflineSpawns.Spawn(1, 2, 1, 4, 300, GrindOnly: true);

        Assert.That(OfflineSpawns.ToSpawnPoint(spawn, region).RegenCheckTime, Is.EqualTo(OfflineGrind.RespawnSeconds(300, fieldElite: true)));
        Assert.That(OfflineSpawns.ToSpawnPoint(spawn with { GrindOnly = false }, region).RegenCheckTime, Is.EqualTo(300));
    }
}