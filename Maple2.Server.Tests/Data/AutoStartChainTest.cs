using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>autoStart follow-ups (DL-35): the quests the client data starts as soon as their prerequisite completes.</summary>
[Category("Data")]
[NonParallelizable]
public class AutoStartChainTest {
    private List<QuestMetadata> quests = [];

    [OneTimeSetUp]
    public void LoadQuests() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        quests = context.QuestMetadata.AsNoTracking().ToList();
    }

    [TestCase(40002736, 40002737)]
    [TestCase(40002740, 40002741)]
    [TestCase(40002733, 40002734)]
    [TestCase(40002767, 40002768)]
    [TestCase(40002793, 40002794)]
    public void FollowUpStarts(int completed, int next) {
        Assert.That(QuestManager.AutoStartAfter(quests, completed).Select(quest => quest.Id), Does.Contain(next));
    }

    [Test]
    public void NotAutoStart() {
        // 60100065 requires 60100060 but is not autoStart.
        Assert.That(QuestManager.AutoStartAfter(quests, 60100060).Select(quest => quest.Id), Does.Not.Contain(60100065));
    }
}
