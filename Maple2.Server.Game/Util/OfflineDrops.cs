namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition drop options. Only active with OFFLINE_DUNGEON_ALL_DROPS=true (launcher.ini: DungeonAllDrops=true).
/// </summary>
public static class OfflineDrops {
    /// <summary>
    /// In dungeons, every item a drop table can give drops every time: monster kills, breakables, interact objects,
    /// script drops and the clear reward. Item boxes, gacha, fishing and housing are unaffected.
    /// </summary>
    public static readonly bool DungeonAllDrops =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_DUNGEON_ALL_DROPS"), "true", StringComparison.OrdinalIgnoreCase);
}
