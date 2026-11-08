using System.Collections.Concurrent;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;

namespace Maple2.Server.Game.Model.Skill;

public class SkillRecord {
    public readonly SkillMetadata Metadata;
    public SkillMetadataMotion Motion => Metadata.Data.Motions[MotionPoint];
    public SkillMetadataAttack Attack => Motion.Attacks[AttackPoint];

    public int SkillId => Metadata.Id;
    public short Level => Metadata.Level;

    public readonly long CastUid;
    public long TargetUid;
    public readonly IActor Caster;

    public int ServerTick;
    public byte MotionPoint { get; private set; }
    public byte AttackPoint { get; private set; }

    public Vector3 Position;
    public Vector3 ImpactPosition;
    public Vector3 Direction; // Sometimes Rotation
    public Vector3 Rotation;
    public float Rotate2Z;
    public bool Unknown;

    public bool IsHold;
    public int HoldInt;
    public string HoldString = string.Empty;
    public long ItemUid;

    public long StateNextTick; // Used for state skills only

    public ConcurrentDictionary<int, IActor> Targets;

    public SkillRecord(SkillMetadata metadata, long castUid, IActor caster) {
        Metadata = metadata;
        CastUid = castUid;
        Caster = caster;
        Targets = [];
    }

    /// <summary>The rotation a cast's SkillUse carries: the one the caster sent, else the caster's own facing (an NPC cast sends none).</summary>
    public static Vector3 CastRotation(in Vector3 casterRotation, in Vector3 rotation) => rotation == default ? casterRotation : rotation;

    /// <summary>Whether an attack places its splash at each of its targets, its caster too (the Arcroller's 40353031, SK-13): only without a
    /// cube path, whose points place it instead, as the captures show (NB-23): a player's client reports them, one region per report and
    /// none per target (BT-42), and an NPC's SkillState lays them around each target, a missile's when it hits (NB-26).</summary>
    public static bool SplashAtTargets(long cubeMagicPathId) => cubeMagicPathId == 0;

    /// <summary>How long an NPC's missile flies to a target that far: the distance over its path's first move's velocity, as the captures
    /// time its hits (NB-25: 1,103 hits a median 65 ms before it); a path that does not move hits at once.</summary>
    public static TimeSpan MissileFlight(IReadOnlyList<MagicPath> path, float distance) =>
        path.Count > 0 && path[0].Velocity > 0 ? TimeSpan.FromSeconds(distance / path[0].Velocity) : TimeSpan.Zero;

    /// <summary>A copy of this attack point's record for its missile's later hit, as the next attack point clears this one's targets (NB-25).</summary>
    public SkillRecord Missile() {
        var copy = (SkillRecord) MemberwiseClone();
        copy.Targets = new ConcurrentDictionary<int, IActor>(Targets);
        return copy;
    }

    /// <summary>Whether a cube path lays this region skill with its splash as it is (NB-22): not one whose level has its own sensor while
    /// its splash does not sense, which FieldSkill would fire at once (Pink Bean's speakers, Balrog's mines, Eupheria's mines: the captures
    /// show them waiting); CubeRegionSplash lays that one to wait for its sensor (NB-27).</summary>
    public static bool PlacesCubeRegion(SkillMetadataSensor? sensor, SkillEffectMetadataSplash splash) => sensor == null || splash.OnlySensingActive;

    /// <summary>The splash a cube path lays a region with: one with its own sensor waits for it, as a sensing splash does (NB-27).
    /// Eupheria's mines 60300013 level 2 fire when a player steps on one (2 of 30 captured adds; she stood on the middle one in all 6
    /// casts) and else end at their Delay, 15 s, with no hit and no blast (28); Pink Bean's speakers hit him as he steps on one (4 of 4).</summary>
    public static SkillEffectMetadataSplash CubeRegionSplash(SkillMetadataSensor? sensor, SkillEffectMetadataSplash splash) =>
        PlacesCubeRegion(sensor, splash) ? splash : splash with { OnlySensingActive = true };

    /// <summary>Whom a cube path lays a region around: each target the cast takes (NB-23), but a region with its own sensor (a mine) around
    /// the caster, also when the cast takes no target (NB-27): Eupheria's 60300007 lays its five mines around her in 6 of 6 captured casts,
    /// each with no target.</summary>
    public static ICollection<T> CubeAnchors<T>(ICollection<T> targets, T caster, SkillMetadataSensor? sensor) => sensor == null ? targets : [caster];

    /// <summary>Whether a cube path's points snap to the block grid: a region whose first range is a box, a block's tile (NB-22).</summary>
    public static bool AlignsCubeRegion(SkillRegion? firstRange) => firstRange == SkillRegion.Box;

    /// <summary>The points a cube path places an NPC's splash at, as the captures show (NB-22): a move's FireFixed is a map position,
    /// otherwise the anchor plus its FireOffset, turned by the facing + 180 degrees when it rotates. Aligned, a point takes the block
    /// grid and the anchor's floor; unaligned, it keeps its place, 3 above the anchor (an NPC stands 1 below the block top).</summary>
    public static Vector3[] CubePoints(IReadOnlyList<MagicPath> path, in Vector3 anchor, float rotationZ, bool align) {
        Matrix4x4 turn = Matrix4x4.CreateRotationZ((rotationZ + 180) * MathF.PI / 180);
        float floor = MathF.Round(anchor.Z / VectorExtensions.BLOCK_SIZE) * VectorExtensions.BLOCK_SIZE;
        var points = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++) {
            MagicPath move = path[i];
            if (move.FireFixed != default) {
                points[i] = move.FireFixed;
                continue;
            }

            Vector3 point = anchor + (move.Rotate ? Vector3.Transform(move.FireOffset, turn) : move.FireOffset);
            points[i] = align ? (point.Align() with { Z = floor }) : (point with { Z = anchor.Z + 3 });
        }
        return points;
    }

    public bool TrySetMotionPoint(byte motionPoint) {
        if (Metadata.Data.Motions.Length <= motionPoint) {
            return false;
        }

        MotionPoint = motionPoint;
        return true;
    }

    public bool TrySetAttackPoint(byte attackPoint) {
        if (Motion.Attacks.Length <= attackPoint) {
            return false;
        }

        AttackPoint = attackPoint;
        // Each attack point must start with a clean target set.
        // Without this, targets from a prior attack point (e.g. a Friendly AP that includes the caster)
        // bleed into subsequent attack points, causing incorrect damage and splash placement.
        Targets.Clear();
        return true;
    }

    public override string ToString() {
        return $"Uid:{CastUid}, SkillId:{SkillId}, Level:{Level}, MotionPoint:{MotionPoint}, AttackPoint:{AttackPoint}\n"
               + $"- Position:{Position}\n"
               + $"- Rotation:{Rotation}\n"
               + $"- Direction:{Direction}";
    }
}
