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
    // "GroundSplitMobKill" and 63000028 "KillAll" (skill 70000096, 999x damage). Also a ship turn's first blast in
    // The Jolly Crocker (70000065 level 6, all of the target's health): the capture shows it taking 15 corsairs and
    // never the player beside them, while level 7 of the same skill hits the player, so the rule needs the level.
    private static readonly HashSet<(int Id, short Level)> MonsterOnlySkills = [(70000096, 1), (70000065, 6)];

    public static bool MonsterOnly(int id, short level) => MonsterOnlySkills.Contains((id, level));

    public IActor Caster { get; init; }
    /// <summary>The id the client credits with this region's hits (the user/owner object id): the casting player, as before
    /// upstream #637, so the hits show as the player's (numbers, enemy HP); map, trigger and monster regions keep the
    /// region's own id, which #637 needed for their knockback.</summary>
    public int UserId => Caster is FieldPlayer ? Caster.ObjectId : ObjectId;
    public SkillSource Source { get; init; } = SkillSource.Region;
    public int Interval { get; init; }
    public int FireCount { get; private set; }
    public bool Enabled => FireCount > 0 || NextTick <= endTick || Field.FieldTick <= endTick;
    public bool Active { get; private set; } = true;
    public int TriggerId { get; init; }

    public readonly Vector3[] Points;
    public readonly bool UseDirection;
    private readonly long endTick;
    // The client's sensor of a sensing region: it arms after StartDelay, then fires once on the first hit.
    private readonly SkillMetadataSensor? sensor;
    // Whether the caster has been off his own sensor since the region landed (OwnSensorSees).
    private bool casterLeft;
    public long NextTick { get; private set; }
    /// <summary>The tick the region starts at, which its add packet carries: a splash region's add plus StartDelay, 0 for a map's or a
    /// trigger's region, as the captures show (BT-68, C3).</summary>
    public long StartTick { get; }

    /// <summary>How long a splash region waits before it starts: its Delay. In the captures the add packet's tick is the add's plus Delay, and
    /// without ImmediateActive the first hit comes Delay + Interval after the add (3,441 of 3,520 adds within 150 ms). A Delay of 90 s or more
    /// is no wait: the two captured (50002207, 300 s; 99999763, int.MaxValue) hit before it, so those keep their first hit.</summary>
    public static int StartDelay(SkillEffectMetadataSplash splash) => splash.Delay < 90_000 ? splash.Delay : 0;
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
        StartTick = baseTick + StartDelay(splash);
        if (splash.ImmediateActive) {
            NextTick = baseTick;
            endTick = baseTick + splash.RemoveDelay + (FireCount - 1) * splash.Interval;
        } else {
            // Delay, then Interval; a sensing region keeps its first check (FS-01).
            NextTick = (splash.OnlySensingActive ? baseTick : StartTick) + splash.Interval;
            endTick = baseTick + splash.Delay + splash.RemoveDelay + FireCount * splash.Interval;
        }
        // A boss's own sensor region waits for that sensor whatever its splash (NB-27).
        if (splash.OnlySensingActive || OwnSensor(Value.Data.Sensor, FirstAttackRange, Caster is FieldNpc)) {
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

    /// <summary>Whom this region's attack hits: the monsters for a MonsterOnly skill, else the field's target pool.</summary>
    public IEnumerable<IActor> FindTargets(Prism[] prisms, SkillMetadataRange range, int targetCount) => MonsterOnly(Value.Id, Value.Level)
        ? prisms.Filter<IActor>(Field.Mobs.Values, targetCount)
        : Field.GetTargets(Caster, prisms, CasterRange(range, Caster is FieldNpc, Caster is not (FieldNpc or FieldPlayer) || HoldsCaster(prisms, Caster.Shape)), targetCount);

    /// <summary>The range a region's attack picks its targets by: an NPC's region counts its caster only for an attack on his own side
    /// (Friendly) and only while he stands in it, as the captures show: Lazy Stumpy's 60036032 (IncludeCaster, Hostile) never hits him in
    /// 25 hits, two of them on the player, also with a point on his tile (NB-22a); Pink Bean's speakers 50002207 level 2 (Friendly) hit
    /// him 4 times, each as he steps on one (NB-27). A player's region counts him only while he stands in it, on either side (NB-29): the
    /// captures hit him in 850 of 99900462's 1,620 hits and 121 of Honing Runes' 10900162 level 5's 373, and his position says which in
    /// 3,218 of the 3,393 hits of the players' regions that count their caster. The map's regions keep their range (holdsCaster true).</summary>
    public static SkillMetadataRange CasterRange(SkillMetadataRange range, bool npcCaster, bool holdsCaster) =>
        (npcCaster && range.ApplyTarget != ApplyTargetType.Friendly) || !holdsCaster ? range with { IncludeCaster = IncludeCasterType.Exclude } : range;

    /// <summary>Whether the caster stands in the region, by his shape as the pool's targets are: an NPC stands 1 below the block top, so
    /// Pink Bean at 1349 is under his speakers' 1350 by his position and on them by his shape (NB-27).</summary>
    public static bool HoldsCaster(IEnumerable<Prism> prisms, IPrism casterShape) => prisms.Any(prism => prism.Intersects(casterShape));

    private SkillMetadataRange? FirstAttackRange => Value.Data.Motions.SelectMany(motion => motion.Attacks).FirstOrDefault()?.Range;

    /// <summary>Whether a region's sensor is its NPC caster's own: it names no side while the region's attack counts him (the speakers of
    /// Pink Bean and Super Pink Bean, Balrog's mines, Pamokan's and Teus's pillars). Such a region waits for that sensor whatever its
    /// splash, and the sensor sees only him (NB-27); a player's region (the Wizard's 10300199) keeps its rule.</summary>
    public static bool OwnSensor(SkillMetadataSensor? sensor, SkillMetadataRange? firstAttack, bool npcCaster) => npcCaster
        && sensor is { ForceInvoke: false, Range.ApplyTarget: ApplyTargetType.None } && firstAttack is { IncludeCaster: not IncludeCasterType.Exclude };

    /// <summary>Whether an own sensor fires now: when its caster stands on it, once he has been off it since it landed, so a boss never
    /// sets off his own trap at once where he stands. A reconstruction: the captured speakers hit Pink Bean 31 to 199 s after they land,
    /// each as he steps on one, and no capture shows a boss on one as it lands.</summary>
    public static bool OwnSensorSees(ref bool casterLeft, bool holdsCaster) {
        casterLeft |= !holdsCaster;
        return casterLeft && holdsCaster;
    }

    /// <summary>Whether an earlier hit of this update took the target to 0 health: its death waits for its own update here, but no region hit of
    /// the original lists such a target (captures: 0 of 2,159 kills; the ship turn's level 7 hits Alvanos and the player, never the corsairs
    /// level 6 took to 0). A corpse is dead already and stays a target, as before.</summary>
    public static bool Dying(IActor target) => target is FieldNpc or FieldPlayer && !target.IsDead && target.Stats.Values[BasicAttribute.Health].Current <= 0;

    private bool IsSensed(SkillMetadataSensor sensed) {
        ApplyTargetType applyTarget = sensed.Range.ApplyTarget;
        if (applyTarget == ApplyTargetType.None) {
            applyTarget = Value.Data.Motions.SelectMany(motion => motion.Attacks).FirstOrDefault()?.Range.ApplyTarget ?? ApplyTargetType.None;
        }

        float skillAngle = UseDirection ? Rotation.Z : 0;
        Prism[] prisms = Points
            .Select(point => sensed.Range.GetPrism(point, skillAngle, applyTarget))
            .ToArray();
        if (OwnSensor(sensed, FirstAttackRange, Caster is FieldNpc)) {
            return !Caster.IsDead && OwnSensorSees(ref casterLeft, HoldsCaster(prisms, Caster.Shape));
        }
        // GetTargets would put the caster first without a range test; SensesCaster tests the caster against the sensor.
        SkillMetadataRange range = sensed.Range with { IncludeCaster = IncludeCasterType.Exclude, ApplyTarget = applyTarget };
        if (sensed.TargetHasBuffId != 0) {
            // targetSelectType 2: only a target with the sensor's buff, and without its second buff.
            return Field.GetTargets(Caster, prisms, range, int.MaxValue).Any(target => SensesBuffTarget(sensed.TargetHasBuffOwner,
                target.Buffs.EnumerateBuffs(sensed.TargetHasBuffId).Select(buff => buff.Caster.ObjectId),
                sensed.TargetHasNotBuffId != 0 && target.Buffs.EnumerateBuffs(sensed.TargetHasNotBuffId).Count > 0, Caster.ObjectId));
        }
        return Field.GetTargets(Caster, prisms, range, 1).Any()
               || SensesCaster(sensed.Range.IncludeCaster, prisms, Caster.Position);
    }

    /// <summary>
    /// Whether a target counts for a sensor with targetSelectType 2: one of its buffs of the sensor's id was cast by the caster
    /// (by anyone when ownerOnly is false), and it lacks the sensor's second buff.
    /// </summary>
    public static bool SensesBuffTarget(bool ownerOnly, IEnumerable<int> buffCasterIds, bool hasNotBuff, int casterId) {
        return !hasNotBuff && buffCasterIds.Any(id => !ownerOnly || id == casterId);
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
                    if (FindTargets(prisms, attack.Range, attack.TargetCount).Any()) {
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
                    IActor[] targets = FindTargets(prisms, attack.Range, attack.TargetCount).Where(target => !Dying(target)).ToArray();
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
