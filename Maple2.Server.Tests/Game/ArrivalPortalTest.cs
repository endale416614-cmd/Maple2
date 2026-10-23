using NUnit.Framework;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class ArrivalPortalTest {
    [Test]
    public void TestArrivalPortalId() {
        Assert.That(FieldManager.ArrivalPortalId(0), Is.EqualTo(-1));
        Assert.That(FieldManager.ArrivalPortalId(-1), Is.EqualTo(-1));
        Assert.That(FieldManager.ArrivalPortalId(1), Is.EqualTo(1));
        Assert.That(FieldManager.ArrivalPortalId(25003), Is.EqualTo(25003));
    }
}