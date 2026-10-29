using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class QuestTeleportTest {
    [Test]
    public void ResendsState() {
        // 50001621 Under the Palace has a GoToDungeon map: its Teleport button needs the quest-state list
        Assert.That(QuestManager.ResendsState(new QuestMetadataGoToDungeon(QuestState.Started, 21004001, 0)), Is.True);
        // an instance only, or no GoToDungeon at all, sends nothing extra
        Assert.That(QuestManager.ResendsState(new QuestMetadataGoToDungeon(QuestState.Started, 0, 1100001)), Is.False);
        Assert.That(QuestManager.ResendsState(new QuestMetadataGoToDungeon(QuestState.None, 0, 0)), Is.False);
    }
}
