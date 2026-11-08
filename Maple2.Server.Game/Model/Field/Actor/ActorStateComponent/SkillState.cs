using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class SkillState {
    private readonly IActor actor;

    public SkillState(IActor actor) {
        this.actor = actor;
    }

    public void SkillCastAttack(SkillRecord cast, byte attackPoint, List<IActor> attackTargets) {
        if (!cast.TrySetAttackPoint(attackPoint)) {
            return;
        }

        SkillMetadataAttack attack = cast.Attack;

        if (attack.MagicPathId != 0) {
            if (actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? magicPaths)) {
                int targetIndex = 0;

                foreach (MagicPath path in magicPaths) {
                    int targetId = 0;

                    if (attack.Arrow.Overlap && attackTargets.Count > targetIndex) {
                        targetId = attackTargets[targetIndex].ObjectId;
                    }

                    var targets = new List<TargetRecord>();
                    var targetRecord = new TargetRecord {
                        Uid = 0 + 2 + targetIndex,
                        TargetId = targetId,
                        Unknown = 0,
                    };
                    targets.Add(targetRecord);

                    // TODO: chaining
                    // While chaining
                    // while (packet.ReadBool()) {
                    //     targetRecord = new TargetRecord {
                    //         PrevUid = targetRecord.Uid,
                    //         Uid = packet.ReadLong(),
                    //         TargetId = packet.ReadInt(),
                    //         Unknown = packet.ReadByte(),
                    //         Index = packet.ReadByte(),
                    //     };
                    //     targets.Add(targetRecord);
                    // }
                    if (attackTargets.Count > targetIndex) {
                        // if attack.direction == 3, use direction to target, if attack.direction == 0, use rotation maybe?
                        cast.Position = actor.Position;
                        cast.Direction = Vector3.Normalize(attackTargets[targetIndex].Position - actor.Position);
                    }

                    actor.Field.Broadcast(SkillDamagePacket.Target(cast, targets));
                }
            }
        }

        // Apply damage to targets server-side for NPC attacks
        // Always use the attack range prism to resolve targets so spatial checks are respected
        Tools.Collision.Prism attackPrism = attack.Range.GetNpcTargetPrism((actor as FieldNpc)?.Value.Metadata.Basic.Friendly ?? 0, attack.CubeMagicPathId, actor.Position, actor.Rotation.Z);
        // TODO: no tested rule fits projectile (MagicPath) attacks, so they vibrate no cubes; upgrade when one does.
        if (attack.BrokenOffence != 0 && attack.MagicPathId == 0) {
            var vibrate = new DamageRecord(cast.Metadata, attack) {
                SkillUid = cast.CastUid,
                SkillId = cast.SkillId,
                Level = cast.Level,
                MotionPoint = cast.MotionPoint,
                AttackPoint = cast.AttackPoint,
                Position = actor.Position,
            };
            actor.Field.VibrateObjects(vibrate, [attack.Range.GetCubePrism(actor.Position, actor.Rotation.Z)]);
        }

        var resolvedTargets = new List<IActor>();
        int queryLimit = attack.TargetCount > 0 ? attack.TargetCount : 1;
        foreach (IActor target in actor.Field.GetTargets(actor, [attackPrism], attack.Range, queryLimit)) {
            resolvedTargets.Add(target);
        }

        if (resolvedTargets.Count > 0) {
            int limit = attack.TargetCount > 0 ? attack.TargetCount : 1;
            for (int i = 0; i < resolvedTargets.Count && i < limit; i++) {
                IActor target = resolvedTargets[i];
                cast.Targets.TryAdd(target.ObjectId, target);
            }

            // A missile hits when it arrives: its nearest target's distance over its path's velocity, as captured (NB-25).
            if (attack.MagicPathId != 0 && actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.MagicPathId, out IReadOnlyList<MagicPath>? missilePath)) {
                TimeSpan flight = SkillRecord.MissileFlight(missilePath, cast.Targets.Values.Min(target => Vector3.Distance(actor.Position, target.Position)));
                if (flight > TimeSpan.Zero) {
                    SkillRecord missile = cast.Missile();
                    actor.Field.Scheduler.Schedule(() => MissileHit(missile), flight);
                    return;
                }
            }

            // Reuse existing pipeline to calculate and broadcast damage/effects
            actor.TargetAttack(cast);
        }

        // With no target too: a mine's path is laid around its caster (NB-27).
        PlaceCubePath(attack, cast.Targets.Values);
    }

    // A missile hits the targets the field still holds alive when it arrives, unless its caster left the field first, as captured (NB-25),
    // and lays its cube path around each of them then (NB-26).
    // TODO(BT-68): the captures miss most targets that left the missile's line, but no tested rule fits its collision; upgrade with one.
    private void MissileHit(SkillRecord missile) {
        if (!actor.Field.TryGetActor(actor.ObjectId, out IActor? caster) || caster != actor) {
            return;
        }
        foreach ((int id, IActor target) in missile.Targets) {
            if (target.IsDead || !actor.Field.TryGetActor(id, out IActor? held) || held != target) {
                missile.Targets.TryRemove(id, out _);
            }
        }
        actor.TargetAttack(missile);
        PlaceCubePath(missile.Attack, missile.Targets.Values);
    }

    // A cube path places its splash around each target the cast takes, the caster too when it is one (Vine Wave's, or a Last attack's
    // with an empty pool), turned by the caster's facing, as the captures show (NB-23, BT-68); a missile's around each target it hits,
    // when it hits (NB-26).
    private void PlaceCubePath(SkillMetadataAttack attack, ICollection<IActor> targets) {
        if (attack.CubeMagicPathId == 0
            || !actor.Field.TableMetadata.MagicPathTable.Entries.TryGetValue(attack.CubeMagicPathId, out IReadOnlyList<MagicPath>? path)) {
            return;
        }
        // TODO(BT-68): a missile's region with its own sensor (53 rows of mines, Pamokan's and Teus's pillars) keeps its splash and is laid
        // at the targets the missile hits, as before NB-26: no capture shows one; upgrade with one.
        bool missile = attack.MagicPathId != 0;

        foreach (SkillEffectMetadata effect in attack.Skills.Where(effect => effect.Splash != null && effect.Skills.Length > 0)) {
            if (!actor.Field.SkillMetadata.TryGet(effect.Skills[0].Id, effect.Skills[0].Level, out SkillMetadata? region)) {
                continue;
            }

            // A region with its own sensor (the bosses' speakers and mines, Eupheria's mines) waits for it and is laid around the
            // caster, also by a cast with no target, as the captures show (NB-27).
            SkillEffectMetadata laid = missile ? effect : effect with { Splash = SkillRecord.CubeRegionSplash(region.Data.Sensor, effect.Splash!) };

            // TODO(BT-68): the client aligns a move unless it says align="0", which the data reads as false like no align, so a box
            // region stands in until the ingest keeps it (232 of 236 captured casts); the captures also put an aligned point on the
            // floor under it and drop it with none, where this takes the anchor's floor (upgrade with a floor query on the map's cells).
            bool align = SkillRecord.AlignsCubeRegion(region.Data.Motions.FirstOrDefault()?.Attacks.FirstOrDefault()?.Range.Type);
            foreach (IActor anchor in SkillRecord.CubeAnchors(targets, actor, missile || SkillRecord.PlacesCubeRegion(region.Data.Sensor, effect.Splash!) ? null : region.Data.Sensor)) {
                Vector3[] points = SkillRecord.CubePoints(path, anchor.Position, actor.Rotation.Z, align);
                if (points.Length > 0) {
                    actor.Field.AddSkill(actor, laid, points, actor.Rotation);
                }
            }
        }
    }
}
