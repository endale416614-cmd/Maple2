using NUnit.Framework;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class RumbleEndTest {
    [Test]
    public void ColosseumRewardShares() {
        Assert.That(DungeonManager.RewardShares(true, 3, 0, 0, 0, 10), Is.EqualTo(3));
        Assert.That(DungeonManager.RewardShares(true, 6, 0, 3, 0, 10), Is.EqualTo(3));
        Assert.That(DungeonManager.RewardShares(true, 0, 0, 6, 0, 10), Is.EqualTo(0), "a continued run lost in its first round");
        Assert.That(DungeonManager.RewardShares(true, 2, 0, 6, 0, 10), Is.EqualTo(0), "a restart");
        Assert.That(DungeonManager.RewardShares(true, 10, 0, 8, 0, 10), Is.EqualTo(2));
        Assert.That(DungeonManager.RewardShares(true, 10, 0, 10, 0, 10), Is.EqualTo(0));
    }

    [Test]
    public void OtherRoomRewardShares() {
        Assert.That(DungeonManager.RewardShares(false, 0, 0, 0, 0, 1), Is.EqualTo(1), "SubRewardCount 0 passes");
        Assert.That(DungeonManager.RewardShares(false, 0, 0, 1, 0, 1), Is.EqualTo(0));
        Assert.That(DungeonManager.RewardShares(false, 0, 2, 0, 3, 6), Is.EqualTo(1));
        Assert.That(DungeonManager.RewardShares(false, 0, 3, 0, 3, 6), Is.EqualTo(0));
        Assert.That(DungeonManager.RewardShares(false, 0, 0, 6, 3, 6), Is.EqualTo(0));
    }

    [Test]
    public void RumbleHostCondition() {
        Assert.That(NpcTalkUtil.RumbleHostCondition(20, 0, 10), Is.False);
        Assert.That(NpcTalkUtil.RumbleHostCondition(20, 6, 10), Is.True);
        Assert.That(NpcTalkUtil.RumbleHostCondition(40, 6, 10), Is.False);
        Assert.That(NpcTalkUtil.RumbleHostCondition(40, 10, 10), Is.True);
        Assert.That(NpcTalkUtil.RumbleHostCondition(10, 0, 10), Is.True);
    }
}
