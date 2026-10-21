using System.Numerics;
using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public static class ItemOptionRarity {
    // Rarity of an item created without one. Boxes and consumables keep theirs in Option.ConstantId (1-6). For gear
    // ConstantId is an option table id, and drop tables leave the grade out for gear that exists at one rarity only
    // (raid gear is Epic, Legendary or Ascendant), so gear takes the lowest rarity its option tables define.
    public static int Default(ItemMetadataOption? option, ItemOptionConstantTable constants, ItemOptionStaticTable statics, ItemOptionRandomTable randoms) {
        if (option == null) {
            return 1;
        }
        if (option.ConstantId is > 0 and < 7) {
            return option.ConstantId;
        }

        return OptionRarities(constants.Options, option.ConstantId)
            .Concat(OptionRarities(statics.Options, option.StaticId))
            .Concat(OptionRarities(randoms.Options, option.RandomId))
            .Where(rarity => rarity is > 0 and < 7)
            .DefaultIfEmpty(1)
            .Min();
    }

    private static IEnumerable<int> OptionRarities<T>(IReadOnlyDictionary<int, IReadOnlyDictionary<int, T>> options, int id) {
        return id > 0 && options.TryGetValue(id, out IReadOnlyDictionary<int, T>? byRarity) ? byRarity.Keys : [];
    }
}

public record ItemOptionConstantTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>> Options
) : Table;

public record ItemOptionRandomTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOption>> Options
) : Table;

public record ItemOptionStaticTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOption>> Options
) : Table;

public record ItemOptionConstant(
    IReadOnlyDictionary<BasicAttribute, int> Values,
    IReadOnlyDictionary<BasicAttribute, float> Rates,
    IReadOnlyDictionary<SpecialAttribute, int> SpecialValues,
    IReadOnlyDictionary<SpecialAttribute, float> SpecialRates);

public record ItemOption(
    float MultiplyFactor,
    ItemOption.Range<int> NumPick,
    ItemOption.Entry[] Entries
) {
    public readonly record struct Entry(
        BasicAttribute? BasicAttribute = null,
        SpecialAttribute? SpecialAttribute = null,
        Range<int>? Values = null,
        Range<float>? Rates = null);
    public readonly record struct Range<T>(T Min, T Max) where T : INumber<T>;
}

public record ItemOptionPickTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>> Options
) : Table {
    public record Option(
        IReadOnlyDictionary<BasicAttribute, int> ConstantValue,
        IReadOnlyDictionary<BasicAttribute, int> ConstantRate,
        IReadOnlyDictionary<BasicAttribute, int> StaticValue,
        IReadOnlyDictionary<BasicAttribute, int> StaticRate,
        IReadOnlyDictionary<BasicAttribute, int> RandomValue,
        IReadOnlyDictionary<BasicAttribute, int> RandomRate
    );
}

public record ItemVariationTable(
    IReadOnlyDictionary<BasicAttribute, ItemVariationTable.Range<int>[]> Values,
    IReadOnlyDictionary<BasicAttribute, ItemVariationTable.Range<float>[]> Rates,
    IReadOnlyDictionary<SpecialAttribute, ItemVariationTable.Range<int>[]> SpecialValues,
    IReadOnlyDictionary<SpecialAttribute, ItemVariationTable.Range<float>[]> SpecialRates
) : Table {
    public readonly record struct Range<T>(T Min, T Max, T Variation) where T : INumber<T>;
}

public record ItemEquipVariationTable(
    IReadOnlyDictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]> Values,
    IReadOnlyDictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]> Rates,
    IReadOnlyDictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]> SpecialValues,
    IReadOnlyDictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]> SpecialRates
) : Table {
    public record Set<T>(T Value, int Weight) where T : INumber<T>;
}
