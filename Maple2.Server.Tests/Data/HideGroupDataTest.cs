using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game.Field;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-18's hide groups in the map data, as RB-02's map-data re-import wrote them: the 8 maps whose vibrate cubes name a
/// hideGroupId, with each group's cube count, on the base DB and on the pack DB. Golden: RB-02's gate on both DBs (the xblocks'
/// MS2Vibrate hideGroupId). A DB from before RB-02 has no hide groups and fails.</summary>
[Category("Data")]
[NonParallelizable]
public class HideGroupDataTest {
    // xblock, then "group:cubes" in group order
    private static readonly string[] Groups = [
        "02000037_bf 1:3 2:3 3:4 4:3 5:3 6:3 7:3 8:3 9:3", // Temple of Immortals: Balrog's ShowVibrate 1 to 9 (28 cubes)
        "02000371_bf 1:3 2:5",
        "02000410_bf 1:6 2:6",
        "02000419_bf 1:15 2:15 3:12 4:3 5:6",
        "02000428_bf 1:6 2:6",
        "02000429_bf 1:6 2:6",
        "02000495_bf 1:3 2:5",
        "02010051_bf 1:20",
    ];

    [Test]
    public void TheBaseHasTheHideGroups() {
        string? path = DataDb.Find();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        Check(path);
    }

    [Test]
    public void ThePackHasTheHideGroups() {
        string? path = Environment.GetEnvironmentVariable("PACK_DATA_DB");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Assert.Ignore("set PACK_DATA_DB to the pack's GameData/maple-data.db");
        }
        Check(path);
    }

    private static void Check(string path) {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var storage = new MapDataStorage(context);

        var found = new List<string>();
        foreach (string xblock in Groups.Select(line => line.Split(' ')[0])) {
            Assert.That(storage.TryGet(xblock, out FieldAccelerationStructure? map), Is.True, $"{xblock} has no map-data row");
            IEnumerable<string> groups = map!.VibrateEntities.ToArray().Where(cube => cube.HideGroupId != 0)
                .GroupBy(cube => cube.HideGroupId).OrderBy(group => group.Key).Select(group => $"{group.Key}:{group.Count()}");
            found.Add(string.Join(" ", groups.Prepend(xblock)));
        }
        Assert.That(found, Is.EqualTo(Groups), path);
    }
}
