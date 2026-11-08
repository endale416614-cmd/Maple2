using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// FT-03: monsterExpTable.xml, in the data since RB-02. A Release build reads the server tables in the storage's constructor, so a DB
/// without the row fails the server's start. Its goldens, and the common column is the curve monsters give today (exp table 2).
/// </summary>
[Category("Data")]
[NonParallelizable]
public class MonsterExpTableDataTest {
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

        var storage = new ServerTableMetadataStorage(context);

        MonsterExpTable table = storage.MonsterExpTable;
        Assert.That(table.Entries.Keys, Is.EqualTo(Enumerable.Range(1, 99)));
        Assert.That(table.Entries[99], Is.EqualTo(new MonsterExpTable.Entry(Common: 120744, Elite: 274694, DungeonElite: 1177262, Boss: 3104868, DungeonBoss: 4346815)));
        Assert.That(table.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Common), Is.EquivalentTo(new TableMetadataStorage(context).ExpTable.ExpBase[2]));
    }
}
