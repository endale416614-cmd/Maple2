using System;
using System.IO;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>BL-01: the damage factors are values of damage-factors.json; the shipped values are today's constants.</summary>
public class DamageFactorsTest {
    [Test]
    public void TheShippedFileHoldsTodaysFactors() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "damage-factors.json");
        Assert.That(File.Exists(path), Is.True, "damage-factors.json ships next to the solution");
        DamageFactors.Config? config = DamageFactors.Parse(File.ReadAllText(path));
        Assert.That(DamageFactors.Valid(config), Is.True);
        Assert.That(config!.PlayerFactor, Is.EqualTo(4));
        Assert.That(config.NpcFactor, Is.EqualTo(2.8));
        Assert.That(config, Is.EqualTo(new DamageFactors.Config()), "the defaults are the file's values");
    }

    [Test]
    public void TheDefaultsAreTheConstants() {
        var defaults = new DamageFactors.Config();
        Assert.That(defaults.PlayerFactor, Is.EqualTo(Constant.AttackDamageFactor));
        Assert.That(defaults.NpcFactor, Is.EqualTo(Constant.NpcAttackDamageFactor));
    }

    [Test]
    public void AFileSetsTheFactorsAndAMissingKeyKeepsTheDefault() {
        DamageFactors.Config? config = DamageFactors.Parse("{ /* a comment */ \"playerFactor\": 5 }");
        Assert.That(DamageFactors.Valid(config), Is.True);
        Assert.That(config!.PlayerFactor, Is.EqualTo(5));
        Assert.That(config.NpcFactor, Is.EqualTo(2.8));
    }

    [TestCase("""{"PlayerFactor": 0}""")]
    [TestCase("""{"NpcFactor": -1}""")]
    [TestCase("""{"PlayerFactor": 1000000}""")]
    [TestCase("null")]
    public void ANonPositiveOrHugeFactorIsInvalid(string json) => Assert.That(DamageFactors.Valid(DamageFactors.Parse(json)), Is.False);

    [Test]
    public void BrokenJsonThrowsForTheLoaderToCatch() => Assert.Throws<System.Text.Json.JsonException>(() => DamageFactors.Parse("{ not json"));
}
