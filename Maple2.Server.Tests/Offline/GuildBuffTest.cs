using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>GD-03: a personal guild buff is the guildbuff row at the guild's level, and its duration is in minutes.</summary>
[TestFixture]
public class GuildBuffTest {
    private static GuildTable Table() {
        var buffs = new Dictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> {
            [1] = new Dictionary<short, GuildTable.Buff> { [1] = new GuildTable.Buff(110000113, 1, 1, 20000000, 0, 10080) },
            [10002] = new Dictionary<short, GuildTable.Buff> {
                [1] = new GuildTable.Buff(110000118, 1, 1, 100000, 0, 60),
                [2] = new GuildTable.Buff(110000118, 2, 3, 120000, 20000000, 60),
            },
        };
        return new GuildTable(buffs,
            new Dictionary<int, IReadOnlyDictionary<int, GuildTable.House>>(),
            new Dictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>>(),
            new Dictionary<short, GuildTable.Property>());
    }

    [Test]
    public void PersonalBuffIsTheGuildsLevelRow() {
        GuildTable.Buff? buff = Table().PersonalBuff(10002, 1);
        Assert.That(buff, Is.Not.Null);
        Assert.That(buff!.Cost, Is.EqualTo(100000));
        Assert.That(buff.Id, Is.EqualTo(110000118));
        Assert.That(buff.Level, Is.EqualTo((short) 1));
        Assert.That(Table().PersonalBuff(10002, 2)?.Cost, Is.EqualTo(120000));
    }

    [Test]
    public void GuildWideAndMissingRowsAreNotPersonal() {
        GuildTable table = Table();
        Assert.That(table.PersonalBuff(1, 1), Is.Null);
        Assert.That(table.PersonalBuff(10002, 0), Is.Null);
        Assert.That(table.PersonalBuff(10002, 3), Is.Null);
        Assert.That(table.PersonalBuff(10009, 1), Is.Null);
    }

    [Test]
    public void DurationIsInMinutes() {
        Assert.That(new GuildTable.Buff(110000118, 1, 1, 100000, 0, 60).DurationMs(), Is.EqualTo(3_600_000));
        Assert.That(new GuildTable.Buff(110000113, 1, 1, 20000000, 0, 10080).DurationMs(), Is.EqualTo(604_800_000));
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void EveryPersonalBuffHasItsCostAndEffect() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        GuildTable table = new TableMetadataStorage(context).GuildTable;
        var skills = new SkillMetadataStorage(context);

        Assert.That(table.Buffs.Keys.OrderBy(id => id), Is.EqualTo(new[] { 1, 2, 3, 4, 10001, 10002, 10003, 10004, 10005 }));
        foreach (int id in new[] { 1, 2, 3, 4 }) {
            Assert.That(table.PersonalBuff(id, 1), Is.Null, $"{id}");
        }
        foreach (int id in new[] { 10001, 10002, 10003, 10004, 10005 }) {
            GuildTable.Buff? first = table.PersonalBuff(id, 1);
            Assert.That(first, Is.Not.Null, $"{id}");
            Assert.That((first!.Cost, first.Duration, first.RequireLevel), Is.EqualTo((100000, 60, (short) 1)), $"{id}");
            foreach (GuildTable.Buff row in table.Buffs[id].Values) {
                Assert.That(skills.TryGetEffect(row.Id, row.Level, out _), Is.True, $"{id}: {row.Id},{row.Level}");
            }
        }
    }
}
