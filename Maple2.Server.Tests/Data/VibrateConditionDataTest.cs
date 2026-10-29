using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>The tree trophy and quests count a hit tree by its wood material tag (NB-18).</summary>
[Category("Data")]
[NonParallelizable]
public class VibrateConditionDataTest {
    [Test]
    public void EveryTreeConditionCountsWood() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var trophies = new SortedDictionary<int, List<ConditionMetadata>>();
        foreach (AchievementMetadata trophy in context.AchievementMetadata) {
            foreach (AchievementMetadataGrade grade in trophy.Grades.Values) {
                if (grade.Condition.Type == ConditionType.vibrate_object) {
                    if (!trophies.TryGetValue(trophy.Id, out List<ConditionMetadata>? list)) {
                        trophies[trophy.Id] = list = [];
                    }
                    list.Add(grade.Condition);
                }
            }
        }

        var quests = new SortedDictionary<int, List<ConditionMetadata>>();
        foreach (QuestMetadata quest in context.QuestMetadata) {
            foreach (ConditionMetadata condition in quest.Conditions) {
                if (condition.Type == ConditionType.vibrate_object) {
                    if (!quests.TryGetValue(quest.Id, out List<ConditionMetadata>? list)) {
                        quests[quest.Id] = list = [];
                    }
                    list.Add(condition);
                }
            }
        }

        // Shaking Trees has five grades; the four coded "Hit a tree" quests and the wedding mission follow.
        Assert.That(trophies.Keys, Is.EqualTo(new[] { 23100023 }));
        Assert.That(trophies[23100023], Has.Count.EqualTo(5));
        Assert.That(quests.Keys, Is.EqualTo(new[] { 50000107, 70400059, 74000024, 89060015, 89060045 }));

        List<ConditionMetadata> alone = trophies.Values.Concat(quests.Values).SelectMany(list => list)
            .Where(condition => condition.PartyCount == 0 && condition.GuildPartyCount == 0).ToList();
        Assert.That(alone, Has.Count.EqualTo(9));
        foreach (ConditionMetadata condition in alone) {
            Assert.That(condition.Check(null!, codeString: "wood"), Is.True, $"{condition}");
            Assert.That(condition.Check(null!, codeString: "grass"), Is.False, $"{condition}");
        }

        // The wedding mission names no material; its party counts still apply.
        ConditionMetadata wedding = quests[50000107].Single();
        Assert.That(wedding.Codes, Is.Null);
        Assert.That(wedding.Target, Is.Null);
        Assert.That(wedding.PartyCount, Is.EqualTo(2));
        Assert.That(wedding.GuildPartyCount, Is.EqualTo(2));
    }
}
