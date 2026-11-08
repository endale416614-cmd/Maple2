using System.Collections.Generic;
using System.IO;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Model;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-26a (R3-F, BS-03): a player's attack counts the stats above the base, damage specials count their rates, and no piercing
/// leaves defense whole, so the damage factor 4 meets the GMS captures.</summary>
[TestFixture]
public class DamageFormulaTest {
    private static string ReadGame(params string[] path) =>
        File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", Path.Combine(path)));

    // Golden: the stat packet of a GMS capture of 2020-04-24 (Corrupted Forest), a Runeblader with STR 178 (169 base), DEX 29, INT 137 (13 base),
    // LUK 13: physical attack 117 (111 base), magical attack 77 (7 base).
    private static Stats Runeblader() {
        var stats = new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Strength, 169 },
            { BasicAttribute.Dexterity, 29 },
            { BasicAttribute.Intelligence, 13 },
            { BasicAttribute.Luck, 13 },
        }, JobCode.RuneBlader);
        stats[BasicAttribute.Strength].AddTotal(9);
        stats[BasicAttribute.Intelligence].AddTotal(124);
        return stats;
    }

    [Test]
    public void AttackCountsTheStatsAboveTheBase() {
        Stats stats = Runeblader();
        stats.Total();
        stats.AddStatAttack(JobCode.RuneBlader);
        Assert.That((stats[BasicAttribute.PhysicalAtk].Total, stats[BasicAttribute.PhysicalAtk].Base), Is.EqualTo((117L, 111L)));
        Assert.That((stats[BasicAttribute.MagicalAtk].Total, stats[BasicAttribute.MagicalAtk].Base), Is.EqualTo((77L, 7L)));

        Stats rated = Runeblader();
        rated[BasicAttribute.PhysicalAtk].AddRate(0.5f);
        rated.Total();
        rated.AddStatAttack(JobCode.RuneBlader);
        Assert.That(rated[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(111 + 55 + 9), "the attack's rate counts on the attack of the stats too");
    }

    [Test]
    public void NoPiercingLeavesDefenseWhole() {
        Assert.That(Damage.DefensePierce(0, 0), Is.EqualTo(1).Within(1e-9), "the old term made it 2 and halved every hit");
        Assert.That(Damage.DefensePierce(0.014, 0), Is.EqualTo(0.986).Within(1e-9));
        Assert.That(Damage.DefensePierce(0.5, 0), Is.EqualTo(0.7).Within(1e-9), "piercing lowers defense by 30 % at most");
        Assert.That(Damage.DefensePierce(0.5, 1), Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void DamageSpecialsCountTheirRate() {
        Assert.That(Damage.Special(0, 0.04f), Is.EqualTo(0.04).Within(1e-6), "gear and buffs give damage specials as rates");
        Assert.That(Damage.Special(50, 0), Is.EqualTo(0.05).Within(1e-9));
        Assert.That(Damage.Special(50, 0.04f), Is.EqualTo(0.09).Within(1e-6));
    }

    // Golden: normal hits of the same Runeblader in the GMS captures (its attack from the stat packet, the NPC's Defense and PhysicalRes from the
    // data). A hit is the factor x weapon attack (rolled from min to max) x the skill's rate x attack x resistance / defense.
    [TestCase(148, 192, 0.57, 117, 141, 90, 271, 346)] // skill 10900032 level 5 on 29000252, Corrupted Forest, 38 hits
    [TestCase(397, 519, 0.75, 202, 294, 120, 788, 1024)] // skill 10900033 level 4 on 23100025, Forest of Chaos, 109 hits
    public void CapturedHitsFitFactorFour(int minWeapon, int maxWeapon, double rate, int attack, int defense, int physicalRes, int lowest, int highest) {
        double hit = Constant.AttackDamageFactor * rate * attack * Damage.CalculateResistance(physicalRes, 0) / (defense * Damage.DefensePierce(0, 0));
        Assert.That(lowest, Is.GreaterThanOrEqualTo(hit * minWeapon * 0.95));
        Assert.That(highest, Is.LessThanOrEqualTo(hit * maxWeapon * 1.05));
    }

    // Golden: monsters' normal hits on players in the GMS captures (the player's Defense and resistance from its stat packet, the NPC's attack
    // and the skill's rate from the data). A monster's hit is the factor x the skill's rate x attack x resistance / defense, with no weapon roll.
    [TestCase(1.0, 125285, 1034, 130, 310, 322)] // NPC 21421103 (Lv 60) skill 40600096 level 1, physical, 19 hits
    [TestCase(1.0, 421089, 1036, 134, 1033, 1072)] // NPC 21421513 (Lv 60) skill 40600311 level 1, magical, 17 hits
    public void CapturedMonsterHitsFitTheNpcFactor(double rate, int attack, int defense, int resistance, int lowest, int highest) {
        double hit = Constant.NpcAttackDamageFactor * rate * attack * Damage.CalculateResistance(resistance, 0) / (defense * Damage.DefensePierce(0, 0));
        Assert.That(lowest, Is.GreaterThanOrEqualTo(hit * 0.95));
        Assert.That(highest, Is.LessThanOrEqualTo(hit * 1.05), "the factor 2 left monsters' hits about 30 % below the captures");
    }

    [Test]
    public void TheFormulaReadsRatesAndTheStatAttack() {
        string calculator = ReadGame("Util", "DamageCalculator.cs");
        Assert.That(calculator, Does.Not.Contain(".Multiplier()"), "every damage special and piercing is read with its rate");
        Assert.That(calculator, Does.Contain("Damage.DefensePierce("));
        Assert.That(calculator, Does.Contain("(1 + finalDamage)"));
        string manager = ReadGame("Manager", "StatsManager.cs");
        Assert.That(manager.Split("Values.AddStatAttack(character.Job.Code());").Length, Is.EqualTo(2), "a player's rebuild adds the attack of the stats once");
    }
}
