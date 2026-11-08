using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-13c: the dungeon rooms keep isExpireTimeOut. Golden: GMS table/na/dungeonroom.xml, where 16 rows have 1 (12 of the 17 timed
/// rows, all but 24090005 and the four 70CH raids; the three untimed lapentas; the Colosseum 24096001) and 24090005 has 0.</summary>
[Category("Data")]
[NonParallelizable]
public class ExpireTimeOutDataTest {
    private static readonly int[] Expiring = [
        23006003, 23007003, 23008003, 23010003, 23011003, 23021003, 23022003, 23023003, 23024003, 23025003, 23026003,
        24096001, 27000001, 27001001, 27002001, 27003001,
    ];

    [Test]
    public void SixteenRoomsFailTheRunAtZero() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        IReadOnlyDictionary<int, DungeonRoomMetadata> rooms = new TableMetadataStorage(context).DungeonRoomTable.Entries;
        Assert.That(rooms, Has.Count.GreaterThan(100));

        Assert.That(rooms.Values.Where(room => room.ExpireTimeOut).Select(room => room.Id).Order(), Is.EqualTo(Expiring));
        Assert.That(rooms[24090005].DurationTick, Is.EqualTo(300000), "timed, and its script fails the run itself");
    }
}
