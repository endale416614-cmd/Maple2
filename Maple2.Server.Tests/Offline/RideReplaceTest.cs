using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>RM-01 (BT-232): a battle mount given by a map script (buff 99910230 in 52000138) was refused while the player rode an
/// item's mount. An effect's mount now replaces an item's mount; it does not replace another effect's mount or an object ride.</summary>
[TestFixture]
public class RideReplaceTest {
    [Test]
    public void AnEffectMountReplacesOnlyAnItemMount() {
        Assert.That(RideManager.ReplacedByEffectMount(new RideOnActionDefault(1, 2, null!)), Is.True, "a mount from an item");
        Assert.That(RideManager.ReplacedByEffectMount(new RideOnActionBattle(1, 2, 99910230, 1)), Is.False, "a battle mount");
        Assert.That(RideManager.ReplacedByEffectMount(new RideOnActionObject(1, 2)), Is.False, "an object ride");
        Assert.That(RideManager.ReplacedByEffectMount(null), Is.False, "no mount");
    }
}
