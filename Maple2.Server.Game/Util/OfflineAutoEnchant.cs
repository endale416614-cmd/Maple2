using System.Collections.Concurrent;
using Maple2.Database.Context;

namespace Maple2.Server.Game.Util;

/// <summary>
/// /autoenchant: one press of Enchant keeps trying, and one press of a gem upgrade runs to the target's tier. The command is on in Adapted and off under Legacy
/// (the original had none), and each character turns the mode on. Odds, costs and failure charges do not change: each attempt pays and rolls as a click does
/// (ItemEnchantManager.AutoRun, ItemSocketHandler.GemstoneUpgradeRun).
/// </summary>
public static class OfflineAutoEnchant {
    public const int MaxTarget = 15;
    private const int NextLevel = 0;

    public static readonly bool Enabled = OfflineProfile.Flag("OFFLINE_AUTOENCHANT", true);

    // By character id, so it survives map changes and dungeon migrations (new session) until the servers stop.
    private static readonly ConcurrentDictionary<long, int> Targets = new();

    /// <summary>/autoenchant alone toggles; with a target it turns on at that target. Returns the new state: null is off, 0 the next level, else the target.</summary>
    public static int? Set(long characterId, int? target) {
        if (target == null && Targets.TryRemove(characterId, out _)) {
            return null;
        }

        int state = target ?? NextLevel;
        Targets[characterId] = state;
        return state;
    }

    /// <summary>The level one press runs to, for this character at this enchant level or gem tier; null means one ordinary attempt.</summary>
    public static int? Goal(long characterId, int enchants) =>
        Enabled && Targets.TryGetValue(characterId, out int target) ? Goal(target, enchants) : null;

    /// <summary>A target the item has reached gives one ordinary attempt, so a forgotten target burns nothing extra on another item.</summary>
    public static int? Goal(int target, int enchants) =>
        target == NextLevel ? enchants + 1 : target > enchants ? target : null;

    /// <summary>The line the command prints, so the player knows materials will burn.</summary>
    public static string Message(int? state) => state switch {
        null => "Auto enchant OFF.",
        NextLevel => "Auto enchant ON, until the next level: one press of Enchant keeps trying and spends your materials and clean copies until the level comes or they run out. A gem upgrade stays one tier per press: type /autoenchant 5 to run gems to tier 5. Type /autoenchant again to turn it off.",
        _ => $"Auto enchant ON, target +{state}: one press of Enchant keeps trying and spends your materials and clean copies until +{state} or until they run out. One press of a gem upgrade runs the gem to tier {state} or its highest tier, whichever comes first, or until your materials run out. Type /autoenchant again to turn it off.",
    };
}
