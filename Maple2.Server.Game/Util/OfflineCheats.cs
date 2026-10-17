using Maple2.Database.Context;
using System.Collections.Concurrent;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition player cheats, ON unless OFFLINE_CHEATS=false (launcher.ini: CheatCommands=false).
/// Chat commands for every player (no admin rights needed): /godmode, /killall, /unstuck, /freebies. Meant to get past
/// quests or situations that the emulator cannot finish properly.
/// </summary>
public static class OfflineCheats {
    /// <summary>Henesys (02000076), where /unstuck sends the player.</summary>
    public const int UnstuckMapId = 2000076;

    public static readonly bool Enabled =
        OfflineProfile.Flag("OFFLINE_CHEATS", true);

    // By character id, so cheats survive map changes and dungeon migrations (new session) until the servers stop.
    private static readonly ConcurrentDictionary<long, byte> GodModeCharacters = new();
    private static readonly ConcurrentDictionary<long, byte> FreebiesCharacters = new();

    public static bool IsGodMode(long characterId) => Enabled && GodModeCharacters.ContainsKey(characterId);

    /// <summary>Turns god mode on or off for this character; returns the new state.</summary>
    public static bool ToggleGodMode(long characterId) => Toggle(GodModeCharacters, characterId);

    /// <summary>Purchases cost nothing, whatever the currency (shops, Meret Market, furniture, beauty, expansions).</summary>
    public static bool IsFreebies(long characterId) => Enabled && FreebiesCharacters.ContainsKey(characterId);

    /// <summary>Turns free purchases on or off for this character; returns the new state.</summary>
    public static bool ToggleFreebies(long characterId) => Toggle(FreebiesCharacters, characterId);

    private static bool Toggle(ConcurrentDictionary<long, byte> characters, long characterId) {
        if (characters.TryRemove(characterId, out _)) {
            return false;
        }
        characters[characterId] = 0;
        return true;
    }
}
