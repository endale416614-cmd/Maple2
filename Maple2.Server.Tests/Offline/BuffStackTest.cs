using System;
using System.IO;
using Maple2.Server.Core.Formulas;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>Fighting Spirit (BT-155): every hit that lands adds a stack of Vengeance, and each stack adds 4 % damage.</summary>
[TestFixture]
public class BuffStackTest {
    private static string ReadGame(params string[] path) =>
        File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", Path.Combine(path)));

    /// <summary>The text from a case label to the first break; after it.</summary>
    private static string CaseBlock(string source, string label) {
        int start = source.IndexOf(label, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), label + " should be found in ApplyDamage");
        int end = source.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.That(end, Is.GreaterThan(start), label + " should end with a break;");
        return source.Substring(start, end - start);
    }

    [Test]
    public void NormalHitFiresOnAttacked() {
        string actor = ReadGame("Model", "Field", "Actor", "Actor.cs");
        Assert.That(CaseBlock(actor, "case DamageType.Normal:"), Does.Contain("EventConditionType.OnAttacked"), "a Normal hit lands, so it fires OnAttacked as a Critical one does");
        Assert.That(CaseBlock(actor, "case DamageType.Miss:"), Does.Not.Contain("EventConditionType.OnAttacked"), "a Miss does not land");
    }

    [Test]
    public void StackedRateCountsEachStack() {
        Assert.That(Damage.StackedRate(0.04f, 0), Is.EqualTo(1.04).Within(1e-6), "a buff added without a stack count counts once");
        Assert.That(Damage.StackedRate(0.04f, 1), Is.EqualTo(1.04).Within(1e-6));
        Assert.That(Damage.StackedRate(0.04f, 3), Is.EqualTo(1.12).Within(1e-6), "Fighting Spirit level 1 at full stacks");
        Assert.That(Damage.StackedRate(0.04f, 14), Is.EqualTo(1.56).Within(1e-6), "Fighting Spirit level 12 at full stacks");
    }

    [Test]
    public void VengeanceAddsItsOtherStacks() {
        string calculator = ReadGame("Util", "DamageCalculator.cs");
        Assert.That(calculator, Does.Contain("VengeanceEffect = 11000123"));
        Assert.That(calculator, Does.Contain("EnumerateBuffs(VengeanceEffect)"));
        Assert.That(calculator, Does.Contain("Damage.StackedRate(rate, vengeance.Stacks) - 1 - rate"), "its first stack counts with the other rates (FT-26a), its other stacks here until BS-02");
    }
}
