using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition, OFFLINE_PET_TAMING=true (launcher.ini: PetTaming=true): wild pets spawn 3x as often, a full taming
/// gauge (candy) captures the pet, captured pets are Epic twice as often, and each throw of a taming item uses one up.
/// The client mod offline-pets lowers the taming items' level limit to 10 so shops show them.
/// </summary>
public static class OfflinePets {
    public const int SPAWN_RATE_MULTIPLIER = 3;
    public const int SPAWN_RATE_TOTAL = 10000;
    private static readonly string[] TamingCategories = ["PetTaming", "PetTrap", "PetEx"]; // candy, G1-G3 snares

    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_PET_TAMING"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Wild-pet spawn chance per spawn cycle, out of <see cref="SPAWN_RATE_TOTAL"/>.</summary>
    public static int SpawnRate(int rate) => Enabled ? ScaledSpawnRate(rate) : rate;

    public static int ScaledSpawnRate(int rate) => rate <= 0 ? rate : (int) Math.Min((long) rate * SPAWN_RATE_MULTIPLIER, SPAWN_RATE_TOTAL);

    /// <summary>
    /// Whether this hit captures the pet. Traps keep the original rule; without a trap only the offline option captures,
    /// once the gauge is full (the data has no non-forced trap, so candy alone could never capture).
    /// </summary>
    public static bool Captures(int trapLevel, bool forcedTaming, int tamingPoint, int maxPoint) => Captures(trapLevel, forcedTaming, tamingPoint, maxPoint, Enabled);

    public static bool Captures(int trapLevel, bool forcedTaming, int tamingPoint, int maxPoint, bool enabled) {
        bool full = maxPoint > 0 && tamingPoint >= maxPoint;
        return trapLevel > 0 ? forcedTaming || full : enabled && full;
    }

    /// <summary>
    /// Rarity of a captured pet from its taming rank (1-100). The emulator's own placeholder is rank/25+1 (Epic 25%,
    /// Legendary 1%); the option doubles Epic and Legendary: Normal/Rare/Exceptional 16% each, Epic 50%, Legendary 2%.
    /// </summary>
    public static int CaptureRarity(int tamingRank) => CaptureRarity(tamingRank, Enabled);

    public static int CaptureRarity(int tamingRank, bool enabled) {
        if (!enabled) {
            return tamingRank / 25 + 1;
        }
        return tamingRank switch {
            <= 16 => 1,
            <= 32 => 2,
            <= 48 => 3,
            <= 98 => 4,
            _ => 5,
        };
    }

    /// <summary>Taming items cast their throw skill without the data's useItem flag, so nothing used them up.</summary>
    public static bool ConsumesOnCast(Item item, int skillId) => Enabled && IsTamingItem(item.Metadata.Property.Category, item.Metadata.Skill?.Id, skillId);

    public static bool IsTamingItem(string category, int? itemSkillId, int skillId) => TamingCategories.Contains(category) && itemSkillId == skillId;
}
