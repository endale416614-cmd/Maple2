using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>SE-05: every trigger skill placed with count 0 repeats until set_skill turns it off, at its placement's Interval (the client's xblocks,
/// MS2TriggerSkill Interval). Golden where a capture shows it: 70000500 every 1 s, 70000572 and 70000573 every 2 s, Interval 0 every 0.1 s.</summary>
[Category("Data")]
[NonParallelizable]
public class TriggerRepeatDataTest {
    [Test]
    public void EveryCountZeroPlacementAndItsInterval() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        List<Ms2TriggerSkill> placed = context.MapEntity.AsEnumerable().Select(entity => entity.Block).OfType<Ms2TriggerSkill>().ToList();
        List<Ms2TriggerSkill> countZero = placed.Where(skill => skill.Count == 0).ToList();
        Assert.That((placed.Count, countZero.Count), Is.EqualTo((1357, 616)));
        Assert.That(placed.Where(skill => skill.Interval == null), Is.Empty);

        // Count 0 repeats; count 1 casts once.
        Assert.That(countZero.All(skill => FieldManager.TriggerFireCount(skill.Count) == int.MaxValue), Is.True);
        Assert.That(placed.Where(skill => skill.Count > 0).All(skill => FieldManager.TriggerFireCount(skill.Count) == 1), Is.True);

        int Every(Ms2TriggerSkill skill) => FieldManager.TriggerInterval(skill.Interval!.Value);
        Assert.That(countZero.GroupBy(Every).OrderBy(group => group.Key).Select(group => (group.Key, group.Count())), Is.EqualTo(new[] {
            (100, 140), (150, 75), (500, 51), (700, 2), (1000, 12), (2000, 52), (2400, 3), (5000, 228), (8000, 1), (15000, 1), (30000, 20), (60000, 24), (120000, 3), (180000, 4),
        }));

        int[] Of(int id, short level) => countZero.Where(skill => skill.SkillId == id && skill.Level == level).Select(Every).Distinct().Order().ToArray();
        // The captured ones.
        Assert.That(Of(70000500, 1), Is.EqualTo(new[] { 1000 }));
        Assert.That(Of(70000572, 1).Concat(Of(70000573, 1)), Is.EqualTo(new[] { 2000, 2000 }));
        Assert.That(new[] { 70000051, 70000055, 70000069, 70000072 }.SelectMany(id => Of(id, 1)).Concat(Of(70000065, 3)), Is.EqualTo(new[] { 100, 100, 100, 100, 100 }));
        Assert.That(Of(70000022, 1), Is.EqualTo(new[] { 5000 }));
        // No capture: Electric AoE (Interval 10) takes the same floor; 02100000's tiles, which nothing turns off; 52010038's score box.
        Assert.That(Of(70000042, 1), Is.EqualTo(new[] { 100 }));
        Assert.That(Of(70000567, 2).Concat(Of(70000567, 3)), Is.EqualTo(new[] { 150, 150 }));
        Assert.That(Of(70000569, 1), Is.EqualTo(new[] { 500 }));
    }
}
