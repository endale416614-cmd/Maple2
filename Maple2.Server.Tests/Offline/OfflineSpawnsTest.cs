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
        Assert.That(spawns.Count, Is.EqualTo(2));

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
    }
}