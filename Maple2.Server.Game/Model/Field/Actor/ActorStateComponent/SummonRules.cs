using System.Numerics;

namespace Maple2.Server.Game.Model.ActorStateComponent;

/// <summary>AI summon rules (NB-10).</summary>
public static class SummonRules {
    // Ours: a summon not linked to its summoner (master None) has no cap in the data; this keeps a looping node bounded.
    public const int UnlinkedCap = 30;

    /// <summary>The NPC id a node's npcId N names: entry N - 1 of the summoner's summon list.</summary>
    public static int? Resolve(IReadOnlyList<int>? summons, int index) => summons != null && index >= 1 && index <= summons.Count ? summons[index - 1] : null;

    /// <summary>How many to spawn now: npcCount per run (at least 1); a linked summon fills up to npcCountMax live slaves of
    /// its group (no cap when npcCountMax is 0); an unlinked one up to UnlinkedCap live unlinked summons.</summary>
    public static int Count(int npcCount, int npcCountMax, int live, bool linked) {
        int perRun = Math.Max(1, npcCount);
        int cap = linked ? (npcCountMax > 0 ? npcCountMax : int.MaxValue) : UnlinkedCap;
        return Math.Clamp(cap - live, 0, perRun);
    }

    /// <summary>summonPos is absolute; else summonPosOffset is from the summoner; else summonTargetOffset from its target; else
    /// the summoner's position. summonRadius scatters each axis at random (a single radius, which the parser puts in Z,
    /// counts for X and Y).</summary>
    public static Vector3 Position(Vector3 summoner, Vector3? target, Vector3 pos, Vector3 posOffset, Vector3 targetOffset, Vector3 radius, Random random) {
        Vector3 anchor = pos != Vector3.Zero ? pos
            : posOffset != Vector3.Zero ? summoner + posOffset
            : targetOffset != Vector3.Zero && target is { } targetPosition ? targetPosition + targetOffset
            : summoner;
        Vector2 spread = radius.X == 0 && radius.Y == 0 ? new Vector2(radius.Z, radius.Z) : new Vector2(radius.X, radius.Y);
        float Scatter(float range) => range > 0 ? (random.NextSingle() * 2 - 1) * range : 0;
        return anchor + new Vector3(Scatter(spread.X), Scatter(spread.Y), 0);
    }

    // TODO: the parser leaves lifeTime in seconds as written but turns decimals (15.6) into milliseconds, so the values mix
    // units; below 6,000 they are seconds, above milliseconds. Upgrade when the ingest keeps the unit (with the next data rebuild).
    public static TimeSpan LifeTime(long value) => value >= 6000 ? TimeSpan.FromMilliseconds(value) : TimeSpan.FromSeconds(value);
}