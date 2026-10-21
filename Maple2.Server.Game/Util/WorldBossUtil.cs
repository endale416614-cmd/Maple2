using Maple2.Model.Metadata;
using Maple2.Tools;

namespace Maple2.Server.Game.Util;

public static class WorldBossUtil {
    public static long ComputeNextSpawnTimestamp(WorldBossMetadata metadata) {
        DateTime now = GameClock.Now;
        if (metadata.EndTime < now || metadata.CycleTime == TimeSpan.Zero) {
            return 0;
        }
        DateTime next = metadata.StartTime;
        if (next < now) {
            double elapsedMs = (now - metadata.StartTime).TotalMilliseconds;
            long cycles = (long) Math.Ceiling(elapsedMs / metadata.CycleTime.TotalMilliseconds);
            next = metadata.StartTime + TimeSpan.FromMilliseconds(cycles * metadata.CycleTime.TotalMilliseconds);
        }
        return next > metadata.EndTime ? 0 : new DateTimeOffset(next).ToUnixTimeSeconds();
    }
}
