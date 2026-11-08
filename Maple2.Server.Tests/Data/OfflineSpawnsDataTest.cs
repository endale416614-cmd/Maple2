using System.IO;
using System.Linq;
using System.Numerics;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>EL-02: every row of offline-spawns.json has its region and a named NPC; Dr. Pippy's fits its map, no script there reads his death step, and his kills feed trophy 21220028 (BT-141).</summary>
[Category("Data")]
[NonParallelizable]
public class OfflineSpawnsDataTest {
    [Test]
    public void TheRowsAndDrPippy() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        OfflineSpawns.Config config = OfflineSpawns.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-spawns.json")))!;
        foreach (OfflineSpawns.Spawn row in config.Spawns) {
            Assert.That(Region(context, row), Is.Not.Null, $"map {row.MapId} region {row.RegionId}");
            Assert.That(context.NpcMetadata.Single(npc => npc.Id == row.NpcId).Name, Is.Not.Null.And.Not.Empty, $"NPC {row.NpcId}");
        }

        OfflineSpawns.Spawn pippy = config.Spawns.Single(row => row.NpcId == 22400062);
        NpcMetadata doctor = context.NpcMetadata.Single(npc => npc.Id == pippy.NpcId);
        Assert.That((doctor.Name, doctor.Basic.Level, doctor.Basic.Class, doctor.Basic.Friendly), Is.EqualTo(("Dr. Pippy", (short) 42, 3, 0)));

        MapMetadata square = context.MapMetadata.Single(map => map.Id == pippy.MapId);
        Assert.That((square.Name, square.Property.Type, square.Drop.Level), Is.EqualTo(("Clock Tower Square", MapType.Telescope, 40)));
        // NPC level minus map level in the data's 25 field Elite spawns: 0 to 3.
        Assert.That(doctor.Basic.Level - square.Drop.Level, Is.InRange(0, 3));
        Assert.That(Region(context, pippy)!.Position, Is.EqualTo(new Vector3(150, 150, 3900)));

        // His death step writes the field's BattleEnd user value, and nothing in this map may react to it.
        Assert.That(context.TriggerMetadata.Where(trigger => trigger.MapXBlock == square.XBlock).AsEnumerable().Any(trigger => trigger.Xml.Contains("BattleEnd")), Is.False);

        AchievementMetadata trophy = context.AchievementMetadata.Single(row => row.Id == 21220028);
        Assert.That(trophy.Grades.Values.Select(grade => grade.Condition.Value).Order(), Is.EqualTo(new long[] { 1, 50, 200 }));
        foreach (AchievementMetadataGrade grade in trophy.Grades.Values) {
            Assert.That(grade.Condition.Type, Is.EqualTo(ConditionType.npc));
            Assert.That(grade.Condition.Codes?.Integers, Is.EqualTo(new[] { pippy.NpcId }));
            Assert.That(grade.Condition.Target, Is.Null, "a kill counts in any map");
        }
    }

    private static Ms2RegionSpawn? Region(MetadataContext context, OfflineSpawns.Spawn row) {
        string xBlock = context.MapMetadata.Single(map => map.Id == row.MapId).XBlock;
        return context.MapEntity.Where(entity => entity.XBlock == xBlock).AsEnumerable()
            .Select(entity => entity.Block).OfType<Ms2RegionSpawn>().FirstOrDefault(region => region.Id == row.RegionId);
    }
}
