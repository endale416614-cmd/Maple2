using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-11: the map data keeps each NPC spawn point's PeaceRestore. Golden: the GMS client's Exported.m2d (spawnpointnpc.flat,
/// default True), where 28,523 of 28,565 spawn points have it and the 42 without are Watchtower Rampart's (02000334_bf), whose soldiers
/// fight the wild pigs.</summary>
[Category("Data")]
[NonParallelizable]
public class PeaceRestoreDataTest {
    [Test]
    public void EverySpawnPointButWatchtowerRampartsHasPeaceRestore() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        List<(string XBlock, SpawnPointNPC Spawn)> spawns = context.MapEntity.AsEnumerable()
            .Where(entity => entity.Block is SpawnPointNPC)
            .Select(entity => (entity.XBlock, (SpawnPointNPC) entity.Block))
            .ToList();
        Assert.That(spawns, Has.Count.GreaterThan(20000));

        List<(string XBlock, SpawnPointNPC Spawn)> without = spawns.Where(pair => !pair.Spawn.PeaceRestore).ToList();
        Assert.That(without.Select(pair => pair.XBlock).Distinct(), Is.EqualTo(new[] { "02000334_bf" }));
        Assert.That(without.All(pair => pair.Spawn is EventSpawnPointNPC), Is.True);
        Assert.That(without.Select(pair => pair.Spawn.SpawnPointId).Order(), Is.EqualTo(new[] {
            101, 102, 103, 104, 105, 106, 111, 112, 113, 114, 115, 131, 132, 133, 134, 150, 151, 152, 160, 190, 199,
            200, 201, 203, 204, 205, 206, 207, 401, 402, 403, 404, 405, 406, 991, 992, 993, 994, 995, 996, 997, 999,
        }));
    }
}
