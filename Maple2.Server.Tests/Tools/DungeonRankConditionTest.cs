using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Tools;

// Quests 93000173-93000181 need dungeon_rank on the solo trials 24091005-24095005; ranks are not scored yet (QF-03).
[TestFixture]
public class DungeonRankConditionTest {
    private static readonly ConditionMetadata Condition = new(ConditionType.dungeon_rank, 1,
        new ConditionMetadata.Parameters(Integers: new[] { 24091005 }),
        new ConditionMetadata.Parameters(Integers: new[] { 3 }));

    // Check reads the session only for party conditions.
    [Test]
    public void ClearOfTheDungeon_CountsForAnyRankTarget() {
        Assert.That(Condition.Check(null!, codeLong: 24091005), Is.True);
    }

    [Test]
    public void ClearOfAnotherDungeon_DoesNotCount() {
        Assert.That(Condition.Check(null!, codeLong: 24092005), Is.False);
    }
}
