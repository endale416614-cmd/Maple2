using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class QuestDropTest {
    private static ConditionMetadata Condition(ConditionType type) =>
        new(type, 1, new ConditionMetadata.Parameters(Integers: new[] { 30000069 }), null);

    [Test]
    public void StillNeeds() {
        Assert.That(QuestManager.StillNeeds(Condition(ConditionType.item_exist), 0, 30000069), Is.True);
        Assert.That(QuestManager.StillNeeds(Condition(ConditionType.item_exist), 1, 30000069), Is.False); // 1 of 1 held
        Assert.That(QuestManager.StillNeeds(Condition(ConditionType.item_exist), 0, 30000070), Is.False);
        Assert.That(QuestManager.StillNeeds(Condition(ConditionType.npc), 0, 30000069), Is.False);
        Assert.That(QuestManager.StillNeeds(Condition(ConditionType.item_collect), 0, 30000069), Is.True);
    }
}
