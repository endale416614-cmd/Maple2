using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition, OFFLINE_CLASS_DROPS=true (launcher.ini: ClassDropsOnly=true): gear that only other classes can wear
/// is not dropped by monsters, breakables, scripts or dungeon clear rewards. Items for every class (potions,
/// materials, gear without a class limit) are not affected.
/// </summary>
public static class OfflineClassDrops {
    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("OFFLINE_CLASS_DROPS"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the item may drop for players of these classes.</summary>
    public static bool Allowed(Item item, IEnumerable<JobCode> jobs) => !Enabled || AllowedFor(item.Metadata.Limit.JobLimits, jobs);

    public static bool AllowedFor(JobCode[] jobLimits, IEnumerable<JobCode> jobs) {
        return jobLimits.Length == 0 || jobLimits.Contains(JobCode.None) || jobs.Any(jobLimits.Contains);
    }
}
