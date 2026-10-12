using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline edition: dungeon clear-reward limits raised to <see cref="UnlimitedClears"/> per week and per day.
/// Only active with OFFLINE_UNLIMITED_DUNGEON_CLEARS=true (launcher.ini: UnlimitedDungeonClears=true).
/// Clears are still counted, so the weekly "united" milestone rewards keep working.
/// </summary>
public static class OfflineDungeons {
    public const int UnlimitedClears = 9999;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_UNLIMITED_DUNGEON_CLEARS"), "true", StringComparison.OrdinalIgnoreCase);

    public static DungeonRoomTable Apply(DungeonRoomTable table) {
        if (!Enabled) {
            return table;
        }

        // SubRewardCount 0 (no daily limit in the data) also blocked every reward: the check is "daily clears < limit".
        return new DungeonRoomTable(table.Entries.ToDictionary(entry => entry.Key, entry => entry.Value with {
            Reward = entry.Value.Reward with { Count = UnlimitedClears, SubRewardCount = UnlimitedClears },
        }));
    }
}
