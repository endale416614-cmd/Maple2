using System.Collections.Concurrent;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition player cheats, ON unless OFFLINE_CHEATS=false (launcher.ini: CheatCommands=false).
/// Chat commands for every player (no admin rights needed): /godmode, /killall, /unstuck. Meant to get past
/// quests or situations that the emulator cannot finish properly.
/// </summary>
public static class OfflineCheats {
    /// <summary>Henesys (02000076), where /unstuck sends the player.</summary>
    public const int UnstuckMapId = 2000076;

    public static readonly bool Enabled =
        !string.Equals(Environment.GetEnvironmentVariable("OFFLINE_CHEATS"), "false", StringComparison.OrdinalIgnoreCase);

    // By character id, so god mode survives map changes and dungeon migrations (new session) until the servers stop.
    private static readonly ConcurrentDictionary<long, byte> GodModeCharacters = new();

    public static bool IsGodMode(long characterId) => Enabled && GodModeCharacters.ContainsKey(characterId);

    /// <summary>Turns god mode on or off for this character; returns the new state.</summary>
    public static bool ToggleGodMode(long characterId) {
        if (GodModeCharacters.TryRemove(characterId, out _)) {
            return false;
        }
        GodModeCharacters[characterId] = 0;
        return true;
    }
}
