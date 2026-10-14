using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition, OFFLINE_DUNGEON_EPIC_GEAR=true (launcher.ini: DungeonEpicGear=true): weapons, armor and accessories
/// dropped in dungeons are at least Epic, but only when the item has Epic (or higher) stats in its option tables.
/// Gear made only below Epic, such as the starter weapons, keeps its rarity.
/// </summary>
public static class OfflineDungeonGear {
    public const int EPIC = 4;

    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_DUNGEON_EPIC_GEAR"), "true", StringComparison.OrdinalIgnoreCase);

    public static int Rarity(ItemMetadata metadata, int rarity, ItemOptionConstantTable constants, ItemOptionPickTable picks) {
        if (rarity >= EPIC || !OfflineGearRepair.IsGear(metadata)) {
            return rarity;
        }

        return OfflineGearRepair.BuildableRarities(metadata.Option, constants, picks)
            .Where(candidate => candidate >= EPIC)
            .DefaultIfEmpty(rarity)
            .Min();
    }
}
