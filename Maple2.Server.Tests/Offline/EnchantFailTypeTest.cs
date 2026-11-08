using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-14b: the warning of Ophelia's window. Golden: Ophelia_1_to_10_exc.msb, type 0 to +8, type 2 at +9 and +10; Peachy_1_to_10.msb, always 0.</summary>
[TestFixture]
public class EnchantFailTypeTest {
    [TestCase(0)]
    [TestCase(8)]
    public void Ophelia_NoWarningUpTo8(int enchants) {
        Assert.That(ItemEnchantManager.FailType(EnchantType.Ophelia, enchants), Is.EqualTo(EnchantFailType.None));
    }

    [TestCase(9)]
    [TestCase(10)]
    [TestCase(14)]
    public void Ophelia_WarnsFrom9(int enchants) {
        Assert.That(ItemEnchantManager.FailType(EnchantType.Ophelia, enchants), Is.EqualTo(EnchantFailType.Unstabilize));
    }

    [TestCase(0)]
    [TestCase(10)]
    public void Peachy_NeverWarns(int enchants) {
        Assert.That(ItemEnchantManager.FailType(EnchantType.Peachy, enchants), Is.EqualTo(EnchantFailType.None));
    }
}