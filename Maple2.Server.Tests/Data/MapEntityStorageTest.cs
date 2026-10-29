using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>Map entity data that repeats an id still loads (BT-82).</summary>
[Category("Data")]
[NonParallelizable]
public class MapEntityStorageTest {
    [Test]
    public void RepeatedEventItemSpawnLoads() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var storage = new MapEntityStorage(context);

        // The Waddle Penguin Club has two event item spawn points with id 14.
        MapEntityMetadata? entities = storage.Get("02000360_bf");

        Assert.That(entities, Is.Not.Null);
        Assert.That(entities!.EventItemSpawns.ContainsKey(14), Is.True);
    }
}
