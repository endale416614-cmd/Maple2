using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// FT-10a: the Hidden Realm portals and room_random.xml, in the data since RB-02 (a Release build reads room_random.xml in the storage's
/// constructor, so a DB without it fails the server's start): 241 rows (247 less the 6 of feature Kritias02, which the ingest's feature
/// filter cuts) on 175 maps, naming groups 1, 2 and 4, and each room a pocket realm map of the data.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class HiddenRealmPortalDataTest {
    [Test]
    public void ThePortalsAndTheirRoomsComeTogether() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var storage = new ServerTableMetadataStorage(context);

        IReadOnlyDictionary<int, HiddenRealmPortalMetadata> portals = storage.TimeEventTable.HiddenRealmPortal!;
        IReadOnlyDictionary<int, RandomRoomTable.Group> groups = storage.RandomRoomTable.Entries;
        Assert.That(portals, Has.Count.EqualTo(241));
        Assert.That(portals.Values.SelectMany(portal => portal.TargetMapIds).Distinct().Count(), Is.EqualTo(175));
        Assert.That(portals.Values.Select(portal => portal.RoomId).Distinct().Order(), Is.EqualTo(new[] {1, 2, 4}));
        Assert.That(groups.ToDictionary(pair => pair.Key, pair => pair.Value.Rooms.Length), Is.EquivalentTo(new Dictionary<int, int> {[1] = 7, [2] = 2, [3] = 29, [4] = 4}));
        Assert.That(groups[1].Rooms.Single(room => room.Id == 15), Is.EqualTo(new RandomRoomTable.Room(15, 100, 80000006, 35000, "Eff_event_portal_A01")));
        HashSet<int> pocketRealms = context.MapMetadata.AsEnumerable().Where(map => map.Property.Type == MapType.PocketRealm).Select(map => map.Id).ToHashSet();
        Assert.That(groups.Values.SelectMany(group => group.Rooms).Where(room => !pocketRealms.Contains(room.MapId)).Select(room => room.Id), Is.Empty,
            "every room is a pocket realm map of the data");
    }
}
