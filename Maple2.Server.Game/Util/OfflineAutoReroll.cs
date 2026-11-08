using System.Collections.Concurrent;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

/// <summary>
/// /autoreroll: one press of Change Attributes rolls until the named line appears. The command is on in Adapted and off under Legacy (the original had none),
/// and each character turns the mode on. Odds and costs do not change: each attempt pays and rolls as a click does (ChangeAttributesHandler.Attempt).
/// </summary>
public static class OfflineAutoReroll {
    /// <summary>The attempts of one press. A chosen line takes 10 to 13 on 2-line armor and 21 with the other line locked (1 of 21: 99.2 % within 100). The next press goes on from there.</summary>
    public const int MaxAttempts = 100;

    public static readonly bool Enabled = OfflineProfile.Flag("OFFLINE_AUTOREROLL", true);

    // By character id, so it survives map changes and dungeon migrations (new session) until the servers stop.
    private static readonly ConcurrentDictionary<long, LockOption> Targets = new();

    /// <summary>What a press did. Hit: the last roll holds the line. Error: why the next attempt could not be paid. Neither: the cap ended it, or it was one ordinary attempt.</summary>
    public readonly record struct RunResult(int Attempts, bool Hit, ChangeAttributesError? Error);

    /// <summary>/autoreroll with a line turns on at that line; alone it turns off. Returns the new state.</summary>
    public static LockOption? Set(long characterId, LockOption? line) {
        if (line == null) {
            Targets.TryRemove(characterId, out _);
            return null;
        }

        Targets[characterId] = line.Value;
        return line;
    }

    /// <summary>The line this character's press rolls for; null means one ordinary attempt.</summary>
    public static LockOption? Target(long characterId) =>
        Enabled && Targets.TryGetValue(characterId, out LockOption line) ? line : null;

    /// <summary>The attempts of one press: until the roll holds the line, until attempt says why the next one can not be paid (null when paid and rolled), or until the cap.</summary>
    public static RunResult Run(int maxAttempts, Func<ChangeAttributesError?> attempt, Func<bool> hit) {
        for (int attempts = 0; attempts < maxAttempts;) {
            if (attempt() is { } error) {
                return new RunResult(attempts, false, error);
            }

            attempts++;
            if (hit()) {
                return new RunResult(attempts, true, null);
            }
        }
        return new RunResult(maxAttempts, false, null);
    }

    public static bool HasLine(ItemStats.Option lines, LockOption line) =>
        line.TryGet(out BasicAttribute basic, out _) ? lines.Basic.ContainsKey(basic)
        : line.TryGet(out SpecialAttribute special, out _) && lines.Special.ContainsKey(special);

    /// <summary>The line a name gives, in any case. BasicAttribute first: CriticalDamage is in both enums, and only the basic one is in the random tables.</summary>
    public static LockOption? Parse(string name) {
        foreach (BasicAttribute basic in Enum.GetValues<BasicAttribute>()) {
            if (basic != BasicAttribute.Unknown && basic.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)) {
                return new LockOption(basic);
            }
        }
        foreach (SpecialAttribute special in Enum.GetValues<SpecialAttribute>()) {
            if (special != SpecialAttribute.None && special.ToString().Equals(name, StringComparison.OrdinalIgnoreCase)) {
                return new LockOption(special);
            }
        }
        return null;
    }

    public static string Name(LockOption line) =>
        line.TryGet(out BasicAttribute basic, out _) ? basic.ToString() : line.TryGet(out SpecialAttribute special, out _) ? special.ToString() : string.Empty;

    /// <summary>The line the command prints, so the player knows materials will burn.</summary>
    public static string Message(LockOption? line) => line == null
        ? "Auto reroll OFF. To turn it on, type /autoreroll and a line, for example /autoreroll CriticalRate."
        : $"Auto reroll ON, target {Name(line.Value)}: one press of Change Attributes keeps rolling and spends your materials and lock scrolls until {Name(line.Value)} appears, until they run out, or for {MaxAttempts} rolls. Type /autoreroll alone to turn it off.";
}
