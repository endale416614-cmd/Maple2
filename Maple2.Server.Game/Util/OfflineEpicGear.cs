using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition, OFFLINE_EPIC_GEAR=true (launcher.ini: EpicGear=true): new weapons, armor and accessories are at least
/// Epic, wherever they come from (drops, boxes, rewards, shops). Legendary and Ascendant gear stays as it is; outfits,
/// pets and other items keep their rarity. Items already owned are not changed.
/// </summary>
public static class OfflineEpicGear {
    public const int RARITY = 4;

    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_EPIC_GEAR"), "true", StringComparison.OrdinalIgnoreCase);

    public static int Rarity(ItemMetadata metadata, int rarity) => Rarity(metadata.Id, metadata.Property.IsSkin, rarity, Enabled);

    public static int Rarity(int itemId, bool isSkin, int rarity, bool enabled) {
        return enabled && IsGear(itemId, isSkin) ? Math.Max(rarity, RARITY) : rarity;
    }

    public static bool IsGear(int itemId, bool isSkin) {
        var type = new ItemType(itemId);
        return !isSkin && (type.IsWeapon || type.IsArmor || type.IsAccessory);
    }
}
