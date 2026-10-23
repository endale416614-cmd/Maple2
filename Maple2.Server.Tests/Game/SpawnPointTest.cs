using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

/// <summary>NB-03: a spawn point refills up to its count, counting only what it still has.</summary>
public class SpawnPointTest {
    [Test]
    public void Missing_RefillsUpToTheCount() {
        Assert.That(FieldSpawnPointNpc.Missing(3, 7, [7, 7, 8]), Is.EqualTo(1));
        Assert.That(FieldSpawnPointNpc.Missing(3, 7, []), Is.EqualTo(3));
        Assert.That(FieldSpawnPointNpc.Missing(3, 7, [7, 7, 7, 7, 7]), Is.EqualTo(0), "never below 0");
        Assert.That(FieldSpawnPointNpc.Missing(2, 8, [7, 7, 8]), Is.EqualTo(1));
    }

    [Test]
    public void PickSpawns_DistinctWhileThePoolLasts() {
        var pool = new List<Vector3> {
            new Vector3(1, 1, 1),
            new Vector3(2, 2, 2),
            new Vector3(3, 3, 3),
            new Vector3(4, 4, 4),
            new Vector3(5, 5, 5)
        };

        // Test pool of 5 distinct vectors and count 5 - should return all 5 distinct positions
        for (int seed = 0; seed < 200; seed++) {
            var random = new Random(seed);
            var result = FieldMobSpawn.PickSpawns(pool, 5, random);
            Assert.That(result.Count, Is.EqualTo(5));
            Assert.That(result.Distinct().Count(), Is.EqualTo(5)); // All distinct
            Assert.That(result.All(p => pool.Contains(p)), Is.True); // All from the pool
        }

        // Test count 7 from a pool of 3 - should return 7 positions, first 3 distinct, all from pool
        var small = pool.Take(3).ToList();
        var result2 = FieldMobSpawn.PickSpawns(small, 7, new Random(0));
        Assert.That(result2.Count, Is.EqualTo(7));
        Assert.That(result2.Take(3).Distinct().Count(), Is.EqualTo(3)); // First 3 are distinct
        Assert.That(result2.All(p => small.Contains(p)), Is.True); // All from the pool

        // Test count 0 and empty pool give empty lists
        var result3 = FieldMobSpawn.PickSpawns(new List<Vector3>(), 0, new Random(0));
        Assert.That(result3.Count, Is.EqualTo(0));

        // Test count 2 from an empty pool gives an empty list
        var result4 = FieldMobSpawn.PickSpawns(new List<Vector3>(), 2, new Random(0));
        Assert.That(result4.Count, Is.EqualTo(0));
    }
}
