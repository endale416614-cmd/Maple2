using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>XP-01 (BT-237): the EXP ledger. A player of level L who kills monsters of level L needs this many kills for the level, by the
/// monster's NPC class (common; boss 3 and 7; dungeon boss 4 and 6), with and without the offline GrindRates setting (the shipped
/// offline-grind.json). The same functions the server uses: MonsterExp (the table lookup) and the grind multiplier, as FieldNpc.GiveExp and
/// ExperienceManager.AddMobExp combine them. A change of the EXP formula or tables fails the golden rows; the full table is in the output.</summary>
[Category("Data")]
[NonParallelizable]
public class ExpLedgerTest {
    private const int Common = 1;
    private const int Boss = 3;
    private const int DungeonBoss = 4;

    // Level, then kills without GrindRates (common, boss, dungeon boss), then kills with GrindRates (common, boss, dungeon boss).
    private static readonly (int Level, long[] Plain, long[] Grind)[] Golden = [
        (10, [130, 4, 3], [130, 4, 3]),
        (20, [693, 22, 16], [201, 7, 5]),
        (30, [1729, 64, 46], [201, 8, 6]),
        (40, [3824, 149, 107], [201, 8, 6]),
        (50, [4085, 159, 114], [201, 8, 6]),
        (60, [3358, 131, 94], [201, 8, 6]),
        (70, [2506, 98, 70], [201, 8, 6]),
    ];

    private static long Kills(long nextExp, long killExp) => killExp <= 0 ? 0 : (nextExp + killExp - 1) / killExp;

    [Test]
    public void KillsPerLevelByMonsterClassAndLevel() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ExpTable exp = new TableMetadataStorage(context).ExpTable;
        MonsterExpTable monsters = new ServerTableMetadataStorage(context).MonsterExpTable;
        OfflineGrind.Config grind = OfflineGrind.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-grind.json")))!;
        Assert.That((grind.TargetKillsPerLevel, grind.MaxExpMultiplier), Is.EqualTo((200, 50f)), "the shipped GrindRates setting");

        // OfflineGrind.ExpMultiplier(ExpTable, level), with the shipped setting instead of the process's.
        float Multiplier(int level) => exp.ExpBase.TryGetValue(2, level, out long monsterExp) && monsterExp > 0 && exp.NextExp.TryGetValue(level, out long nextExp) && nextExp > 0
            ? OfflineGrind.ExpMultiplier(nextExp, monsterExp, grind.TargetKillsPerLevel, grind.MaxExpMultiplier) : 1f;

        long[] Row(int level, bool withGrind) {
            float multiplier = withGrind ? Multiplier(level) : 1f;
            return new[] { Common, Boss, DungeonBoss }.Select(npcClass => {
                long? killExp = ExperienceManager.MonsterExp(monsters, npcClass, level);
                // AddMobExp: (long) (expValue * modifier)
                return killExp is { } value && exp.NextExp.TryGetValue(level, out long nextExp) ? Kills(nextExp, (long) (value * multiplier)) : 0;
            }).ToArray();
        }

        var table = new StringBuilder("level: kills for the level without GrindRates (common, boss 3/7, dungeon boss 4/6) | with GrindRates\n");
        foreach (int level in exp.NextExp.Keys.Order().Where(level => monsters.Entries.ContainsKey(level))) {
            table.AppendLine($"{level}: {string.Join(", ", Row(level, false))} | {string.Join(", ", Row(level, true))}");
        }
        TestContext.Out.WriteLine(table.ToString());

        foreach ((int level, long[] plain, long[] withGrind) in Golden) {
            Assert.That(Row(level, false), Is.EqualTo(plain), $"level {level} without GrindRates");
            Assert.That(Row(level, true), Is.EqualTo(withGrind), $"level {level} with GrindRates");
        }
    }
}
