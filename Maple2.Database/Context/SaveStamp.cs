using System.Linq;
using Maple2.Database.Context;

namespace Maple2.Database.Context;

/// <summary>Mode stamp of a save: which rules it is played under, and every mode it has seen (R12-08).</summary>
public static class SaveStamp {
    public const string Adapted = "Adapted";
    public const string Legacy = "Legacy";

    /// <param name="Changed">the rows must be written (first stamp, or the mode changed)</param>
    /// <param name="WarnLegacyAfterAdapted">the save is opened under Legacy and has run under Adapted before</param>
    public sealed record Result(string Mode, string History, bool Changed, bool WarnLegacyAfterAdapted);

    /// <summary>storedMode/storedHistory: the rows of the save (null when it has no stamp yet); current: the running profile.</summary>
    public static Result Next(string? storedMode, string? storedHistory, string current) {
        bool changed = storedMode != current;
        string history = string.IsNullOrEmpty(storedHistory) ? current : (changed ? storedHistory + "," + current : storedHistory);
        bool warn = current == Legacy && history.Split(',').Contains(Adapted);
        return new Result(current, history, changed, warn);
    }

    public static string Current => OfflineProfile.Adapted ? Adapted : Legacy;
}