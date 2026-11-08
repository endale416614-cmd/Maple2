using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Model;

/// <summary>
/// A field's Shadow Expedition gauge or score board (SE-01, BT-26). The map's scripts open, set and close it; while it is open each kill
/// adds its points, and on the treasure maps each Distinct Paul picked up adds its amount.
/// </summary>
public class FieldScore {
    private int points;
    public int Points => points;
    public bool IsOpen { get; private set; }
    public bool IsGauge { get; private set; }

    public void Open(bool gauge) {
        points = 0;
        IsOpen = true;
        IsGauge = gauge;
    }

    // The points stay: the scripts read them after the close.
    public void Close() {
        IsOpen = false;
        IsGauge = false;
    }

    public void Set(int value) => points = value;

    /// <summary>Adds to an open score and returns true; false when it is closed or there is nothing to add.</summary>
    public bool Add(int value) {
        if (!IsOpen || value <= 0) {
            return false;
        }
        Interlocked.Add(ref points, value); // pickups arrive on the session's thread, kills on the field's
        return true;
    }

    /// <summary>
    /// What a kill adds: nothing for a friendly NPC, else its spawn_npc_range score, else 20 from Class 7, 10 for a scale above 1 or
    /// HitImmune 99, and 5 for the rest.
    /// </summary>
    public static int KillPoints(int friendly, int npcClass, float scale, int hitImmune, int spawnScore) {
        // TODO: SE-01: the weights are fitted to the two captured gauges (52100031 and 52100041: all 302 kills of 21 NPC ids) and no data holds them; upgrade when a source of the original's values is found.
        if (friendly > 0) {
            return 0;
        }
        if (spawnScore > 0) {
            return spawnScore;
        }
        if (npcClass >= 7) {
            return 20;
        }
        return scale > 1 || hitImmune >= 99 ? 10 : 5;
    }

    public static int KillPoints(NpcMetadata npc, int spawnScore) =>
        KillPoints(npc.Basic.Friendly, npc.Basic.Class, npc.Model.Scale, npc.Basic.HitImmune, spawnScore);
}
