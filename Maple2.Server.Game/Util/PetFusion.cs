using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Pet fusion (PE-03, BT-224). A fodder pet gives calc_petEnchantExp's exp and its bonus (the bonus while the day's fusions are
/// within DailyPetEnchantMaxCount) and counts one fusion; pet_fusion.msb: a rarity 3 level 1 Balrog of level limit 50 gave
/// 12000 + 3000. It costs calc_petEnchantCost mesos by the target's rarity (the capture paid half, the client showing a fusion
/// cost sale). A Fusion Stone (AddPetEXP) gives its parameter per stone for calc_petEnchantBookCost mesos, and counts no fusion.
/// </summary>
public static class PetFusion {
    /// <summary>What one fodder entry gives: its exp, bonus included, its mesos and the fusions it counts.</summary>
    public readonly record struct Gain(long Exp, long Meso, int Fusions);

    // The formulas' tables hold rarities 1 to 5; an artifact (6) counts as legendary.
    private static int Grade(int rarity) => Math.Clamp(rarity, 1, 5);

    /// <summary>The fodder's gain for the target pet, or null when it is neither a pet nor a Fusion Stone.</summary>
    public static Gain? Fodder(Item target, Item fodder, int amount, int fusionsToday, int dailyMaxCount) {
        int grade = Grade(target.Rarity);
        if (fodder.Pet is { } pet) {
            (long exp, long bonus) = Lua.CalcPetEnchantExp(grade, Grade(fodder.Rarity), pet.Level, pet.Exp, fusionsToday, dailyMaxCount, fodder.Metadata.Limit.Level);
            return new Gain(exp + bonus, Lua.CalcPetEnchantCost(grade, Grade(fodder.Rarity), pet.Level, pet.Exp), 1);
        }
        if (fodder.Metadata.Function is { Type: ItemFunction.AddPetEXP } function && long.TryParse(function.Parameters, out long stoneExp)) {
            return new Gain(stoneExp * amount, Lua.CalcPetEnchantBookCost(grade) * amount, 0);
        }
        return null;
    }

    /// <summary>
    /// Adds exp to the pet and raises its level while its exp reaches the threshold of its level: petexp.xml's for its rarity, through
    /// calc_petExp for its level limit (pet_fusion.msb: 95416 at level 12 stays below 202500 at limit 70). The rest carries over;
    /// maxLevel is the last. Returns the levels gained.
    /// </summary>
    public static int AddExp(Item pet, long exp, PetExpTable table, int maxLevel) {
        ItemPet data = pet.Pet!;
        data.Exp += exp;
        int gained = 0;
        while (data.Level < maxLevel && table.Entries.TryGetValue(data.Level, out long[]? row) && pet.Rarity >= 1 && pet.Rarity <= row.Length) {
            long next = Lua.CalcPetExp(row[pet.Rarity - 1], pet.Metadata.Limit.Level);
            if (next <= 0 || data.Exp < next) {
                break;
            }
            data.Exp -= next;
            data.Level++;
            gained++;
        }
        return gained;
    }
}
