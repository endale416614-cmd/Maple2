using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// PE-03: petexp.xml, in the data since RB-02. A Release build reads the tables in the storage's constructor, so a DB without the row
/// fails the server's start. GMS table/petexp.xml: levels 1 to 49 at normal 22000, rare 55000, elite 90000, excellent 90000,
/// legendary 90000, artifact 0, and level 50 all 0.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class PetExpDataTest {
    [Test]
    public void TheStorageStartsWithTheTable() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var storage = new TableMetadataStorage(context);

        PetExpTable table = storage.PetExpTable;
        Assert.That(table.Entries.Keys.Order(), Is.EqualTo(Enumerable.Range(1, 50)));
        Assert.That(table.Entries.Where(entry => entry.Key < 50).Select(entry => entry.Value), Has.All.EqualTo(new long[] {22000, 55000, 90000, 90000, 90000, 0}));
        Assert.That(table.Entries[50], Is.EqualTo(new long[6]));
    }
}
