using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class TrophyCountTest {
    [Test]
    public void NextCounter() {
        Assert.That(AchievementManager.NextCounter(ConditionType.level, 12, 15), Is.EqualTo(15));
        Assert.That(AchievementManager.NextCounter(ConditionType.level, 15, 12), Is.EqualTo(15));
        Assert.That(AchievementManager.NextCounter(ConditionType.trophy_point, 40, 38), Is.EqualTo(40));
        Assert.That(AchievementManager.NextCounter(ConditionType.killcount, 3, 10), Is.EqualTo(10));
        Assert.That(AchievementManager.NextCounter(ConditionType.item_gear_score, 900, 800), Is.EqualTo(900));
        Assert.That(AchievementManager.NextCounter(ConditionType.npc, 12, 1), Is.EqualTo(13));
    }

    [Test]
    public void Check() {
        // hero_achieve with Codes Integers [21300024, 21300025] and no target is true for codeLong 21300024 and false for 21300026
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            null
        ), null!, codeLong: 21300024), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            null
        ), null!, codeLong: 21300026), Is.False);

        // hero_achieve with those codes and Target Integers [1] is true for targetLong 1, false for targetLong 2
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [1]
            }
        ), null!, targetLong: 1, codeLong: 21300024), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [1]
            }
        ), null!, targetLong: 2, codeLong: 21300024), Is.False);

        // dungeon_first_clear [21001001, 25001001] true for 25001001
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_first_clear,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21001001, 25001001]
            },
            null
        ), null!, codeLong: 25001001), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_first_clear,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21001001, 25001001]
            },
            null
        ), null!, codeLong: 21001001), Is.True);

        // dungeon_reward [27000] true for 27000
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_reward,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [27000]
            },
            null
        ), null!, codeLong: 27000), Is.True);

        // quest_revise_achieve [90000660] true for 90000660
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.quest_revise_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [90000660]
            },
            null
        ), null!, codeLong: 90000660), Is.True);

        // trophy_point with Codes Range (20000000, 29999999) true for 20000000
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.trophy_point,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 20000000, Max = 29999999 }
            },
            null
        ), null!, codeLong: 20000000), Is.True);
    }
}
