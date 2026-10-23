using NUnit.Framework;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class BeautyShopTest {
    [Test]
    public void OpensGameShop() {
        Assert.IsFalse(NpcTalkUtil.OpensGameShop(506, 34)); // Beauty NPC
        Assert.IsFalse(NpcTalkUtil.OpensGameShop(507, 35)); // Beauty NPC
        Assert.IsFalse(NpcTalkUtil.OpensGameShop(509, 33)); // Beauty NPC
        Assert.IsTrue(NpcTalkUtil.OpensGameShop(100, 13)); // Regular NPC
        Assert.IsFalse(NpcTalkUtil.OpensGameShop(0, 13)); // No shop
        Assert.IsTrue(NpcTalkUtil.OpensGameShop(100, 40)); // Regular NPC (kind 40)
    }
}
