using System.IO;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class DefensiveEffectsTest {
    [Test]
    public void DamageTakenMultiplier_EachBuffMultiplies() {
        Assert.That(Damage.DamageTakenMultiplier([]), Is.EqualTo(1).Within(1e-6));
        Assert.That(Damage.DamageTakenMultiplier([0.25f]), Is.EqualTo(0.75).Within(1e-6), "Iron Shield level 1");
        Assert.That(Damage.DamageTakenMultiplier([0.8f]), Is.EqualTo(0.2).Within(1e-6), "Iron Shield level 12");
        Assert.That(Damage.DamageTakenMultiplier([0.25f, 0.1f]), Is.EqualTo(0.675).Within(1e-6), "with Iron Defense");
        Assert.That(Damage.DamageTakenMultiplier([1.2f]), Is.EqualTo(0).Within(1e-6), "never below 0");
    }

    [Test]
    public void Parse() {
        var config = DefensiveEffects.Parse("""{"Effects": {"10100151": {"1": [0.25, 0.25], "12": [0.8, 0.8]}}}""");
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects[10100151][12], Is.EqualTo(new[] { 0.8f, 0.8f }));
    }

    [Test]
    public void ShippedFile_IronShieldAndIronDefense() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "defensive-effects.json");
        Assert.That(File.Exists(path), Is.True, "defensive-effects.json ships next to the solution");
        var config = DefensiveEffects.Parse(File.ReadAllText(path));
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects[10100151][1], Is.EqualTo(new[] { 0.25f, 0.25f }), "Iron Shield level 1");
        Assert.That(config.Effects[10100151][12], Is.EqualTo(new[] { 0.8f, 0.8f }), "Iron Shield level 12");
        Assert.That(config.Effects.ContainsKey(10100152), Is.False, "the PvP copy is left out");
        Assert.That(config.Effects[10100061][1], Is.EqualTo(new[] { 0.1f, 0.1f }), "Iron Defense");
        Assert.That(DefensiveEffects.Rate(10100151, 12, magic: true), Is.EqualTo(0.8f));
        Assert.That(DefensiveEffects.Rate(999999, 1, magic: false), Is.EqualTo(0), "unknown effect");
        Assert.That(DefensiveEffects.Rate(10100151, 99, magic: false), Is.EqualTo(0), "unknown level");
    }
}
