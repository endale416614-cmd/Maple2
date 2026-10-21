namespace Maple2.Server.Game.Util;

/// <summary>
/// Boss HP holds (AI MinimumHpNode): a standing health floor that damage cannot pass until the boss's next phase lowers
/// or removes it. A floor is released <see cref="HoldLimitMs"/> after health first reached
/// it (heals in between do not restart the count), so content whose AI never lowers its floor cannot soft-lock an encounter.
/// </summary>
public static class BossHpFloor {
    public const long HoldLimitMs = 60_000;

    /// <summary>The health a floor keeps: percent of max health, at least 1 while a floor is set; 0 without a floor.</summary>
    public static long FloorHealth(float percent, long total) => percent <= 0 ? 0 : Math.Max(1, (long) (total * percent / 100));

    /// <summary>
    /// One update: the health to keep, whether the floor is released, and the tick health first reached the floor
    /// (0 until it has).
    /// </summary>
    public static (long Health, bool Release, long HeldSince) Step(long current, long total, float percent, long heldSince, long now) {
        long floor = FloorHealth(percent, total);
        if (floor <= 0) {
            return (current, false, 0);
        }
        if (heldSince != 0 && now - heldSince >= HoldLimitMs) {
            return (current, true, 0);
        }
        if (current > floor) {
            return (current, false, heldSince);
        }
        return (floor, false, heldSince == 0 ? now : heldSince);
    }
}
