using Maple2.Database.Context;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Offline edition, OFFLINE_CLASS_DROPS=true (launcher.ini: ClassDropsOnly=true): gear made for other classes (by the
/// classes it is made for, JobRecommends, or may be worn by, JobLimits) is not dropped by monsters, breakables, scripts
/// or dungeon clear rewards. Weapons can be worn by every class and name their class only in JobRecommends. Items for
/// every class (potions, materials, gear without a class) are not affected, and neither is an item that a started quest
/// of the player still needs.
/// </summary>
public static class OfflineClassDrops {
    public static readonly bool Enabled =
        OfflineProfile.Flag("OFFLINE_CLASS_DROPS", true);

    /// <summary>True when the item may drop for these players: made for one of their classes, or still needed by a started
    /// quest of one of them (the Soul Binder's first quest needs a training orb whose data names the Heavy Gunner, BT-138).</summary>
    public static bool Allowed(Item item, IEnumerable<FieldPlayer> players) => !Enabled
        || AllowedFor(item.Metadata.Limit.JobLimits, item.Metadata.Limit.JobRecommends, players.Select(player => player.Value.Character.Job.Code()))
        || players.Any(player => player.Session.Quest.NeedsItem(item.Id));

    public static bool AllowedFor(JobCode[] jobLimits, IEnumerable<JobCode> jobs) {
        return jobLimits.Length == 0 || jobLimits.Contains(JobCode.None) || jobs.Any(jobLimits.Contains);
    }

    public static bool AllowedFor(JobCode[] jobLimits, JobCode[] jobRecommends, IEnumerable<JobCode> jobs) {
        return AllowedFor(jobLimits, jobs) && AllowedFor(jobRecommends, jobs);
    }
}
