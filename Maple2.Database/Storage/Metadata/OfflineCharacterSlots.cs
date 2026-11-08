using Maple2.Database.Context;
using Maple2.Model.Metadata;

namespace Maple2.Database.Storage;

/// <summary>
/// Offline edition: every character slot open without 1-slot Character Cards, OFFLINE_CHARACTER_SLOTS (launcher.ini:
/// CharacterSlots, on in Adapted). The open count is worked out when it is sent and never saved, so turning the option
/// off gives back the account's own count (4 plus the cards used), at least its characters, and every character keeps
/// an open row.
/// </summary>
public static class OfflineCharacterSlots {
    public static readonly bool Enabled = OfflineProfile.Flag("OFFLINE_CHARACTER_SLOTS", true);

    /// <summary>The slots the character list opens for an account with <paramref name="saved"/> unlocked slots and
    /// <paramref name="characters"/> characters.</summary>
    public static int Unlocked(int saved, int characters) => Unlocked(saved, characters, Enabled);

    /// <summary>All of them when <paramref name="enabled"/>; else the saved count, but at least the characters (some may
    /// have been made while every slot was open), so each one keeps an open row; never above the game's limit.</summary>
    public static int Unlocked(int saved, int characters, bool enabled) =>
        enabled ? Constant.ServerMaxCharacters : Math.Min(Math.Max(saved, characters), Constant.ServerMaxCharacters);

    /// <summary>The saved count after a 1-slot Character Card: one past the slots the list opens without the option.</summary>
    public static int AfterCard(int saved, int characters) => Math.Min(Math.Max(saved, characters) + 1, Constant.ServerMaxCharacters);
}
