using Maple2.Server.Core.Formulas;
using Maple2.Model.Enum;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class AllAttackTypeTest {
    [Test]
    public void ResistanceOf_PhysicalAtk_IsPhysicalRes() {
        Assert.That(Damage.ResistanceOf(BasicAttribute.PhysicalAtk), Is.EqualTo(BasicAttribute.PhysicalRes));
    }

    [Test]
    public void ResistanceOf_MagicalAtk_IsMagicalRes() {
        Assert.That(Damage.ResistanceOf(BasicAttribute.MagicalAtk), Is.EqualTo(BasicAttribute.MagicalRes));
    }

    [Test]
    public void CalculateResistance_90_0_Is_0_94() {
        Assert.That(Damage.CalculateResistance(90, 0), Is.EqualTo(0.94).Within(1e-9));
    }

    [Test]
    public void CalculateResistance_1503_0_Is_LessThan_0() {
        // Toxic Weed's MagicalAtk is 1503, MagicalRes is 90
        Assert.That(Damage.CalculateResistance(1503, 0), Is.LessThan(0));
    }
}
