namespace Maple2.Model.Metadata;

/// <summary>petexp.xml: per pet level, the exp that finishes it, by rarity 1 to 6 (normal, rare, elite, excellent, legendary, artifact).</summary>
public record PetExpTable(IReadOnlyDictionary<int, long[]> Entries) : Table;
