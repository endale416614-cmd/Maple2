using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class GlobalPortalRuleTest {
    [TestCase(MapType.Quest, InstanceType.solo, false)]
    [TestCase(MapType.Telescope, InstanceType.solo, false)]
    [TestCase(MapType.Telescope, InstanceType.none, true)]
    [TestCase(MapType.Home, InstanceType.ugcMap, true)]
    [TestCase(MapType.Dungeon, InstanceType.none, false)]
    [TestCase(MapType.None, InstanceType.DungeonLobby, false)]
    [TestCase(MapType.Event, InstanceType.massiveEvent, false)]
    public void Accepts(MapType type, InstanceType instance, bool expected) {
        Assert.That(GlobalPortalRule.Accepts(type, instance), Is.EqualTo(expected));
    }
}
