using Maple2.Database.Context;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline edition: dungeon clear-reward limits raised to <see cref="UnlimitedClears"/> per week and per day.
/// Only active with OFFLINE_UNLIMITED_DUNGEON_CLEARS=true (launcher.ini: UnlimitedDungeonClears=true).
/// Clears are still counted, so the weekly "united" milestone rewards keep working.
/// Also decides which party-size dungeons a smaller group may enter (<see cref="PartyDungeonsOpen"/>).
/// </summary>
public static class OfflineDungeons {
    public const int UnlimitedClears = 9999;

    public static bool Enabled =>
        OfflineProfile.Flag("OFFLINE_UNLIMITED_DUNGEON_CLEARS", true);

    /// <summary>Party-size dungeons (MinUserCount above 1) can be entered by a smaller group, even alone (OFFLINE_PARTY_DUNGEONS).</summary>
    public static readonly bool PartyDungeonsOpen = OfflineProfile.Flag("OFFLINE_PARTY_DUNGEONS", true);

    // Its map scripts wait for the full group with no timeout, so a smaller group would wait forever. The other party-size
    // dungeons in the data (Legion raids L1-L5, Ludibrium Clock Tower) start after a 30-second wait or do not count players.
    private static readonly HashSet<int> NeedsFullGroup = [29003001]; // Cathy Mart Part-Time Job

    /// <summary>True when a group of <paramref name="size"/> players must be refused at the door.</summary>
    public static bool TooFewPlayers(int dungeonId, int minUserCount, int size, bool partyDungeonsOpen) =>
        minUserCount > 1 && size < minUserCount && (!partyDungeonsOpen || NeedsFullGroup.Contains(dungeonId));

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
