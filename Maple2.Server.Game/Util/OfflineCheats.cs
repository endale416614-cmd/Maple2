using Maple2.Database.Context;
using System.Collections.Concurrent;
using Maple2.Model.Enum;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition player cheats: on in Adapted, off under Legacy (the original had none); OFFLINE_CHEATS (launcher.ini: CheatCommands) wins over the mode either way.
/// Chat commands for every player (no admin rights needed): /godmode, /killall, /unstuck, /freebies, /allboxes. Meant to get past
/// quests or situations that the emulator cannot finish properly.
/// </summary>
public static class OfflineCheats {
    /// <summary>Henesys (02000076), where /unstuck sends the player.</summary>
    public const int UnstuckMapId = 2000076;

    /// <summary>Where /unstuck goes: a solo instance (a quest map) is entered again fresh at its spawn, which replays its
    /// scene and script; anything else, or /unstuck --town, goes to Henesys.</summary>
    public static int UnstuckTarget(int mapId, InstanceType type, bool toTown) =>
        !toTown && type == InstanceType.solo ? mapId : UnstuckMapId;

    /// <summary>/unstuck on an open field map (not a dungeon, solo instance, home or guild house) goes to the map's own spawn point.</summary>
    public static bool UnstuckToSpawn(InstanceType type, bool dungeon, bool toTown) =>
        !toTown && !dungeon && type == InstanceType.none;

    public static readonly bool Enabled =
        OfflineProfile.Flag("OFFLINE_CHEATS", true);

    // By character id, so cheats survive map changes and dungeon migrations (new session) until the servers stop.
    private static readonly ConcurrentDictionary<long, byte> GodModeCharacters = new();
    private static readonly ConcurrentDictionary<long, byte> FreebiesCharacters = new();
    // God mode without the one-hit kill (/godmode --protect): play tests that measure the player's damage (RFF).
    private static readonly ConcurrentDictionary<long, byte> ProtectOnlyCharacters = new();
    // Map scripts see these characters inside every trigger box (/allboxes): scripted dungeon runs (V-03).
    private static readonly ConcurrentDictionary<long, byte> AllBoxesCharacters = new();

    public static bool IsGodMode(long characterId) => Enabled && GodModeCharacters.ContainsKey(characterId);

    /// <summary>Turns god mode on or off for this character; returns the new state.</summary>
    public static bool ToggleGodMode(long characterId) => Toggle(GodModeCharacters, characterId);

    /// <summary>God mode's one-hit kill: on unless god mode was turned on with --protect.</summary>
    public static bool IsGodModeKill(long characterId) => IsGodMode(characterId) && !ProtectOnlyCharacters.ContainsKey(characterId);

    /// <summary>Turns god mode on or off; protectOnly keeps the player's damage normal. Returns the new state.</summary>
    public static bool ToggleGodMode(long characterId, bool protectOnly) {
        bool on = ToggleGodMode(characterId);
        if (on && protectOnly) {
            ProtectOnlyCharacters[characterId] = 0;
        } else {
            ProtectOnlyCharacters.TryRemove(characterId, out _);
        }
        return on;
    }

    /// <summary>Player is inside all trigger boxes on the map.</summary>
    public static bool IsInAllBoxes(long characterId) => Enabled && AllBoxesCharacters.ContainsKey(characterId);

    /// <summary>Turns all boxes mode on or off for this character; returns the new state.</summary>
    public static bool ToggleAllBoxes(long characterId) => Toggle(AllBoxesCharacters, characterId);

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
