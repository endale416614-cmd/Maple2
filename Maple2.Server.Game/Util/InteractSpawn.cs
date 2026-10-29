using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

public static class InteractSpawn {
    // Entries roll on their own because their probabilities can pass 10000.
    public static IEnumerable<InteractObjectMetadataSpawn> Roll(IEnumerable<InteractObjectMetadataSpawn> spawns, Random random) {
        return spawns.Where(spawn => random.Next(10000) < spawn.Probability);
    }
}
