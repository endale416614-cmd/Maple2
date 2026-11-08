using Maple2.Model.Enum;

namespace Maple2.Server.Core.Formulas;

public static class Damage {
    /// <summary>Damage-taken multiplier of several DefensiveProperty rates: each buff multiplies by max(0, 1 - rate).</summary>
    public static double DamageTakenMultiplier(IEnumerable<float> rates) {
        double multiplier = 1;
        foreach (float rate in rates) {
            multiplier *= Math.Max(0, 1 - rate);
        }
        return multiplier;
    }

    /// <summary>Damage factor of a rate per stack; a buff added without a stack count has Stacks 0 and counts once.</summary>
    public static double StackedRate(float rate, int stacks) => 1 + rate * Math.Max(1, stacks);

    /// <summary>A damage special as a fraction: its value in per-mille plus its rate (gear and buffs give damage specials as rates).</summary>
    public static double Special(long total, float rate) => total / 1000.0 + rate;

    /// <summary>The defense divisor's piercing factor: piercing (a fraction) lowers defense by up to 30 %, less against the target's piercing resistance; 1 without piercing.</summary>
    public static double DefensePierce(double piercing, float targetPiercingResistance) => 1 - Math.Min(0.3, piercing / (1 + targetPiercingResistance));

    /// <summary>The target stat that resists an attack stat: MagicalAtk is resisted by MagicalRes, anything else by PhysicalRes.</summary>
    public static BasicAttribute ResistanceOf(BasicAttribute attack) =>
        attack == BasicAttribute.MagicalAtk ? BasicAttribute.MagicalRes : BasicAttribute.PhysicalRes;

    public static double CalculateResistance(double targetPiercingResistance, double casterPiercingMultiplier) {
        return (1500.0 - Math.Max(0, targetPiercingResistance - 1500 * casterPiercingMultiplier)) / 1500;
    }

    /// <summary>
    /// Checks hit rate for evading
    /// </summary>
    public static bool Evade(float targetAccuracyResistance, long targetEvasionTotal, long targetDexterityTotal, long casterAccuracyTotal, float casterEvasionResistance, long casterLuckTotal, bool superArmorBreak) {
        if (superArmorBreak) {
            return false;
        }

        // Unconfirmed
        double casterAccuracy = casterAccuracyTotal * (1 - targetAccuracyResistance);
        double targetEvasion = targetEvasionTotal * (1 - casterEvasionResistance);

        double hitRate = (casterAccuracy - 10) / (targetEvasion + (0.8 * casterAccuracy)) * 2 + Math.Min(0.05, targetDexterityTotal / (targetDexterityTotal + 50) * 0.05)
               - Math.Min(0.05, casterLuckTotal / (casterLuckTotal + 50) * 0.05);

        if (Random.Shared.NextDouble() > hitRate) {
            return true;
        }

        return false;
    }

    public static bool TriggerCompulsionEvent(float totalCompulsionRate) {
        if (totalCompulsionRate >= 1 || (totalCompulsionRate > 0 && Random.Shared.NextDouble() <= totalCompulsionRate)) {
            return true;
        }

        return false;
    }
}
