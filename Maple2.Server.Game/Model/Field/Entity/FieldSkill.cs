using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using Serilog;

namespace Maple2.Server.Game.Model;

public enum SkillSource {
    Region,
    Cube,
}

public class FieldSkill : FieldEntity<SkillMetadata> {
    // Scene skills that clear the monsters of a cutscene. Their ApplyTarget (5) means players for every other
    // region skill (traps, pushes, buffs), so these would kill the player instead: Soul Binder intro 63000029
    // "GroundSplitMobKill" and 63000028 "KillAll" (skill 70000096, 999x damage).
    private static readonly HashSet<int> MonsterOnlySkills = [70000096];

    public IActor Caster { get; init; }
    /// <summary>The id the client credits with this region's hits (the user/owner object id): the casting player, as before
    /// upstream #637, so the hits show as the player's (numbers, enemy HP); map, trigger and monster regions keep the
    /// region's own id, which #637 needed for their knockback.</summary>
    public int UserId => Caster is FieldPlayer ? Caster.ObjectId : ObjectId;
    public SkillSource Source { get; init; } = SkillSource.Region;
    public int Interval { get; }
    public int FireCount { get; private set; }
    public bool Enabled => FireCount > 0 || NextTick <= endTick || Field.FieldTick <= endTick;
    public bool Active { get; private set; } = true;
    public int TriggerId { get; init; }

    public readonly Vector3[] Points;
    public readonly bool UseDirection;
    private readonly long endTick;
    // The client's sensor of a sensing region: it arms after StartDelay, then fires once on the first hit.
    private readonly SkillMetadataSensor? sensor;
    public long NextTick { get; private set; }
    private readonly ILogger logger = Log.ForContext<FieldSkill>();

    private ByteWriter GetDamagePacket(DamageRecord record) => Source switch {
        SkillSource.Cube => SkillDamagePacket.Tile(record),
        _ => SkillDamagePacket.Region(record),
    };

    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int interval, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = interval;
        FireCount = int.MaxValue;
        NextTick = Field.FieldTick + interval;
    }

    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int interval, int fireCount, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = interval;
        FireCount = fireCount;
        NextTick = Field.FieldTick + interval;
    }

    // TODO: check if duration of field skill is correct.
    // Example: Heaven's Wrath says duration is 20s but it's lasting ~22s (Probably the splash.Delay being added to the endTick).
    public FieldSkill(FieldManager field, int objectId, IActor caster,
                      SkillMetadata value, int fireCount, SkillEffectMetadataSplash splash, params Vector3[] points) : base(field, objectId, value) {
        Caster = caster;
        Points = points;
        Interval = splash.Interval;
        FireCount = fireCount;
        UseDirection = splash.UseDirection;

        long baseTick = Field.FieldTick;
        if (splash.ImmediateActive) {
            NextTick = baseTick;
            endTick = baseTick + splash.RemoveDelay + (FireCount - 1) * splash.Interval;
        } else {
            NextTick = baseTick + splash.Interval;
            endTick = baseTick + splash.Delay + splash.RemoveDelay + FireCount * splash.Interval;
        }
        if (splash.OnlySensingActive) {
            endTick = baseTick + splash.Delay + splash.RemoveDelay;
            Active = false;
            // sensorForceInvokeByInterval regions keep the plain path until that flag is checked in game.
            if (Value.Data.Sensor is { ForceInvoke: false } levelSensor) {
                sensor = levelSensor;
                NextTick = baseTick + levelSensor.StartDelay;
                endTick = SensingEnd(baseTick, splash, FireCount);
            }
        }
    }

    /// <summary>The tick a sensing region ends without having fired: its life is Delay plus FireCount intervals.</summary>
    public static long SensingEnd(long castTick, SkillEffectMetadataSplash splash, int fireCount) {
        return castTick + splash.Delay + (long) splash.Interval * Math.Max(fireCount, 1);
    }

    /// <summary>Whether the caster counts for a sensor: only when the sensor includes the caster and the caster stands inside it.</summary>
    public static bool SensesCaster(IncludeCasterType includeCaster, IEnumerable<Prism> prisms, in Vector3 casterPosition) {
        if (includeCaster == IncludeCasterType.Exclude) {
            return false;
        }

        foreach (Prism prism in prisms) {
            if (prism.Contains(casterPosition)) {
                return true;
            }
        }
        return false;
    }

    private bool IsSensed(SkillMetadataSensor sensed) {
        ApplyTargetType applyTarget = sensed.Range.ApplyTarget;
        if (applyTarget == ApplyTargetType.None) {
            applyTarget = Value.Data.Motions.SelectMany(motion => motion.Attacks).FirstOrDefault()?.Range.ApplyTarget ?? ApplyTargetType.None;
        }

        float skillAngle = UseDirection ? Rotation.Z : 0;
        Prism[] prisms = Points
            .Select(point => sensed.Range.GetPrism(point, skillAngle, applyTarget))
            .ToArray();
        // GetTargets would put the caster first without a range test; SensesCaster tests the caster against the sensor.
        return Field.GetTargets(Caster, prisms, sensed.Range with { IncludeCaster = IncludeCasterType.Exclude, ApplyTarget = applyTarget }, 1).Any()
               || SensesCaster(sensed.Range.IncludeCaster, prisms, Caster.Position);
    }

    public override void Update(long tickCount) {
        if (!Enabled) {
            Field.RemoveSkill(ObjectId);
            return;
        }

        // A sensing region that nothing entered ends without firing.
        if (sensor != null && tickCount > endTick) {
            Field.RemoveSkill(ObjectId);
            return;
        }

        if (tickCount < NextTick) {
            return;
        }

        // Check Activation for OnlySensingActive
        if (!Active) {
            if (sensor != null) {
                if (!IsSensed(sensor)) {
                    NextTick = Field.FieldTick;
                    return;
                }

                Active = true;
                goto activated;
            }

            foreach (SkillMetadataMotion motion in Value.Data.Motions) {
                foreach (SkillMetadataAttack attack in motion.Attacks) {
                    float skillAngle = UseDirection ? Rotation.Z : 0;
                    Prism[] prisms = Points
                        .Select(point => attack.Range.GetPrism(point, skillAngle, attack.Range.ApplyTarget))
                        .ToArray();
                    if ((MonsterOnlySkills.Contains(Value.Id)
                            ? prisms.Filter<IActor>(Field.Mobs.Values, attack.TargetCount)
                            : Field.GetTargets(Caster, prisms, attack.Range, attack.TargetCount)).Any()) {
                        Active = true;
                        goto activated;
                    }
                }
            }

            NextTick = Field.FieldTick + Interval;
            goto end;
        }

    activated:
        // TODO: These are buffs? seems irrelevant to FieldSkill?
        // foreach (SkillEffectMetadata skill in Value.Data.Skills) {
        //     if (skill.Condition != null) {
        //         ConditionSkill(Field.Players.Values, skill);
        //     } else if (skill.Splash != null) {
        //         SplashSkill(skill);
        //     }
        // }

        var record = new SkillRecord(Value, 0, Caster) {
            TargetUid = (long) ObjectId << 32,
            Position = Position,
            Rotation = Rotation,
        };
        for (byte i = 0; record.TrySetMotionPoint(i); i++) {
            for (byte j = 0; record.TrySetAttackPoint(j); j++) {
                SkillMetadataAttack attack = record.Attack;
                record.TargetUid++;
                var damage = new DamageRecord(record.Metadata, attack) {
                    CasterId = UserId,
                    OwnerId = ObjectId,
                    SkillId = Value.Id,
                    Level = Value.Level,
                    MotionPoint = record.MotionPoint,
                    AttackPoint = record.AttackPoint,
                    Position = Position,
                    Direction = Rotation,
                };
                var targetRecords = new List<TargetRecord>();
                if (attack.Arrow.BounceType > 0) {
                    IActor[] targets = [];
                    var bounceTargets = new List<IActor>();
                    Vector3 position = Position;
                    long prevTargetUid = 0;
                    for (int bounce = 0; bounce <= attack.Arrow.BounceCount; bounce++) {
                        Vector3 box = attack.Arrow.Collision + attack.Arrow.CollisionAdd;
                        var circle = new Circle(new Vector2(position.X, position.Y), attack.Arrow.BounceRadius);
                        // var rectangle = new Rectangle(new Vector2(Position.X, Position.Y), box.X, box.Y, UseDirection ? Rotation.Z : 0);
                        var prism = new Prism(circle, position.Z, box.Z);

                        targets = attack.Arrow.BounceOverlap
                            ? Field.GetTargets(Caster, [prism], record.Attack.Range, 1, targets).ToArray()
                            : Field.GetTargets(Caster, [prism], record.Attack.Range, 1, bounceTargets).ToArray();
                        if (targets.Length <= 0) {
                            break;
                        }

                        IActor target = targets[0];
                        record.TargetUid++;
                        targetRecords.Add(new TargetRecord {
                            PrevUid = prevTargetUid,
                            Uid = record.TargetUid,
                            TargetId = target.ObjectId,
                            Index = (byte) targetRecords.Count,
                        });
                        bounceTargets.Add(target);
                        position = target.Position;
                        prevTargetUid = record.TargetUid;
                    }

                    Field.Broadcast(SkillDamagePacket.Target(record, targetRecords));
                    Field.Broadcast(GetDamagePacket(damage));
                    for (int t = 0; t < bounceTargets.Count; t++) {
                        IActor target = bounceTargets[t];
                        record.TargetUid = targetRecords[t].Uid;
                        record.Position = target.Position;
                        record.Targets.TryAdd(target.ObjectId, target);

                        // TODO: There should be some delay between bounces.
                        target.TargetAttack(record);
                        Caster.ApplyEffects(attack.SkillsOnDamage, Caster, damage, targets: record.Targets.Values.ToArray());
                        Caster.ApplyEffects(attack.Skills, Caster, Caster, skillId: Value.Id, targets: targets);
                    }
                } else {
                    float skillAngle = UseDirection ? Rotation.Z : 0;
                    Prism[] prisms = Points
                        .Select(point => attack.Range.GetPrism(point, skillAngle, attack.Range.ApplyTarget))
                        .ToArray();
                    IActor[] targets = (MonsterOnlySkills.Contains(Value.Id)
                        ? prisms.Filter<IActor>(Field.Mobs.Values, attack.TargetCount)
                        : Field.GetTargets(Caster, prisms, attack.Range, attack.TargetCount)).ToArray();
                    // if (targets.Length > 0) {
                    //     logger.Debug("[{Tick}] {ObjectId}:{AttackPoint} Targeting: {Count}/{Limit} {Type}",
                    //         NextTick, ObjectId, attack.Point, targets.Length, attack.TargetCount, attack.Range.ApplyTarget);
                    //     foreach (IActor target in targets) {
                    //         logger.Debug("- {Id} @ {Position}", target.ObjectId, target.Position);
                    //     }
                    // }
                    // else if (Caster.ObjectId != 0) { // Not Ms2RegionSkill
                    //     logger.Debug("[{Tick}] {ObjectId}:{AttackPoint} has {Count}/{Limit} {Type}",
                    //         NextTick, ObjectId, attack.Point, 0, attack.TargetCount, attack.Range.ApplyTarget);
                    //     logger.Debug("{Range}", attack.Range);
                    //     foreach (Prism prism in prisms) {
                    //         logger.Debug("- {Prism}", prism);
                    //     }
                    // }

                    if (attack.BrokenOffence != 0) {
                        Field.VibrateObjects(damage, Points.Select(point => attack.Range.GetCubePrism(point, skillAngle)).ToArray());
                    }
                    foreach (IActor target in targets) {
                        target.ApplyDamage(Caster, damage, attack);
                        // TODO: Set PrevUid?
                        targetRecords.Add(new TargetRecord {
                            Uid = (long) ObjectId << 32 | (uint) targetRecords.Count,
                            TargetId = target.ObjectId,
                            Index = (byte) targetRecords.Count,
                        });
                    }
                    if (targetRecords.Count > 0) {
                        Field.Broadcast(SkillDamagePacket.Target(record, targetRecords));
                        Field.Broadcast(GetDamagePacket(damage));
                    }

                    Caster.ApplyEffects(attack.SkillsOnDamage, Caster, damage, targets: targets);
                    Caster.ApplyEffects(attack.Skills, Caster, Caster, skillId: Value.Id, targets: targets);
                }

                if (attack.Skills.Any(effect => effect.Splash != null)) {
                    Field.AddSkill(record);
                }
            }
        }

        // A sensing region fires once.
        if (sensor != null) {
            Field.RemoveSkill(ObjectId);
            return;
        }
    end:
        if (Interval == 0) {
            FireCount = 0;
            NextTick = long.MaxValue;
        } else {
            if (FireCount != int.MaxValue) {
                FireCount--;
            }

            if (FireCount <= 0) {
                NextTick = long.MaxValue;
            } else {
                // Keep the cadence, but after a pause (empty field) restart from now instead of firing every tick to catch up.
                NextTick = Math.Max(NextTick + Interval, Field.FieldTick);
            }
        }
    }
}
