using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

[Category("Data")]
public class QuestDeadlockTest {
    // Directive 15: the event system must not block progression. Event-only quests (an EventTag needs an active QuestTag
    // event; event missions start only through their event) never start offline, so no other quest may need one first.
    [Test]
    public void NoQuestOutsideTheEventsNeedsAnEventQuest() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        List<QuestMetadata> quests = context.QuestMetadata.ToList();

        static bool EventOnly(QuestMetadata quest) => !string.IsNullOrEmpty(quest.Basic.EventTag) || quest.EventMissionType != QuestEventMissionType.none;

        // Blocked: event-only, or a required quest is blocked, or every selectable quest is.
        HashSet<int> blocked = quests.Where(EventOnly).Select(quest => quest.Id).ToHashSet();
        bool changed = true;
        while (changed) {
            changed = false;
            foreach (QuestMetadata quest in quests) {
                if (blocked.Contains(quest.Id)) {
                    continue;
                }
                int[] required = quest.Require.Quest ?? [];
                int[] selectable = quest.Require.SelectableQuest ?? [];
                if (required.Any(blocked.Contains) || (selectable.Length > 0 && selectable.All(blocked.Contains))) {
                    blocked.Add(quest.Id);
                    changed = true;
                }
            }
        }

        List<string> deadlocked = quests
            .Where(quest => !EventOnly(quest) && !quest.Basic.Disabled && blocked.Contains(quest.Id))
            .Select(quest => $"{quest.Id} {quest.Name}")
            .ToList();
        TestContext.WriteLine($"{quests.Count} quests, {quests.Count(EventOnly)} event-only, {blocked.Count} blocked");
        Assert.That(deadlocked, Is.Empty, "quests outside the events that need an event quest first");
    }
}
