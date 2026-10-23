using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

public static class GlobalPortalRule {
    /// <summary>
    /// MC Kay's mini-game portals: offered and joined only outside solo story instances and dungeon lobbies, whose script
    /// state is lost when the player leaves (Cave Depths 52000066, 1.2.0 report).
    /// </summary>
    public static bool Accepts(MapType type, InstanceType instance) =>
        instance is not (InstanceType.solo or InstanceType.DungeonLobby)
        && type is (>= MapType.None and <= MapType.Telescope) or (>= MapType.Alikar and <= MapType.Shelter);
}
