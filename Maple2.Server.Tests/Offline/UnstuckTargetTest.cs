using NUnit.Framework;
using Maple2.Server.Game.Util;
using Maple2.Model.Enum;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class UnstuckTargetTest {
    [Test]
    public void TestUnstuckTarget() {
        Assert.That(OfflineCheats.UnstuckTarget(52000120, InstanceType.solo, false), Is.EqualTo(52000120));
        Assert.That(OfflineCheats.UnstuckTarget(52000120, InstanceType.solo, true), Is.EqualTo(2000076));
        Assert.That(OfflineCheats.UnstuckTarget(2000001, InstanceType.none, false), Is.EqualTo(2000076));
        Assert.That(OfflineCheats.UnstuckTarget(62000001, InstanceType.ugcMap, false), Is.EqualTo(2000076));
    }

    [Test]
    public void TestUnstuckToSpawn() {
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.none, false, false), Is.True);
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.none, true, false), Is.False); // a dungeon
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.none, false, true), Is.False); // --town
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.solo, false, false), Is.False);
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.ugcMap, false, false), Is.False);
        Assert.That(OfflineCheats.UnstuckToSpawn(InstanceType.GuildHouse, false, false), Is.False);
    }
}
