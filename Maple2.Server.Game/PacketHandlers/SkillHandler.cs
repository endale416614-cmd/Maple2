using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Enum;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;

namespace Maple2.Server.Game.PacketHandlers;

public class SkillHandler : FieldPacketHandler {
    /// <summary>A cancel stops the motion only for the newest cast: a late cancel of an older cast must not stop the newer cast's motion (ActiveSkill buffs read it).</summary>
    public static bool CancelsMotion(long? newestCastUid, long cancelUid) => newestCastUid == cancelUid;

    public override RecvOp OpCode => RecvOp.Skill;

    private enum Command : byte {
        Use = 0,
        Attack = 1,
        Sync = 2,
        TickSync = 3,
        Cancel = 4,
    }

    private enum SubCommand : byte {
        Point = 0,
        Target = 1,
        CubeMagicPath = 2,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required SkillMetadataStorage SkillMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Use:
                HandleUse(session, packet);
                return;
            case Command.Attack:
                var subcommand = packet.Read<SubCommand>();
                switch (subcommand) {
                    case SubCommand.Point:
                        HandlePoint(session, packet);
                        return;
                    case SubCommand.Target:
                        HandleTarget(session, packet);
                        return;
                    case SubCommand.CubeMagicPath:
                        HandleCubeMagicPath(session, packet);
                        return;
                }
                return;
            case Command.Sync:
                HandleSync(session, packet);
                return;
            case Command.TickSync:
                HandleTickSync(session, packet);
                return;
            case Command.Cancel:
                HandleCancel(session, packet);
                return;
        }
    }

    private void HandleUse(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        long skillUid = packet.ReadLong();
        int serverTick = packet.ReadInt();
        int skillId = packet.ReadInt();
        short level = packet.ReadShort();

        if (session.HeldLiftup != null) {
            if (session.HeldLiftup.SkillId == skillId && session.HeldLiftup.Level == level) {
                session.HeldLiftup = null;
            } else {
                // Cannot use other skills while holding LiftupWeapon.
                return;
            }
        }
        byte motionPoint = packet.ReadByte();
        var position = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>();
        var rotation = packet.Read<Vector3>();
        float rotate2Z = packet.ReadFloat(); // Rotation2Z

        SkillRecord? record = session.Player.CastSkill(skillId, level, skillUid, serverTick, position, direction, rotation, rotate2Z, motionPoint);
        if (record == null) {
            return;
        }

        SkillMetadataMotionProperty motion = record.Metadata.Data.Motions.First().MotionProperty;
        session.Animation.TryPlaySequence(motion.SequenceName, motion.SequenceSpeed, AnimationType.Skill, record.Metadata);

        packet.ReadInt(); // ClientTick
        record.Unknown = packet.ReadBool(); // UnkBool
        record.ItemUid = packet.ReadLong();
        record.IsHold = packet.ReadBool();
        if (record.IsHold) {
            record.HoldInt = packet.ReadInt();
            record.HoldString = packet.ReadUnicodeString();

            if (session.Player.DebugSkills) {
                session.Send(NoticePacket.Message($"Skill.Use: {skillId}, {skillUid}; IsHold: true; HoldInt: {record.HoldInt}; HoldString: {record.HoldString}; UnkBool: {record.Unknown}"));
            }
        } else if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Use: {skillId}, {skillUid}; IsHold: false; UnkBool: {record.Unknown}"));
        }

        if (!session.Player.SkillCastConsume(record)) {
            // The client already plays the cast: stop it, and send the server's spirit and stamina, which its own check missed.
            session.Send(SkillUseFailedPacket.Fail(record));
            session.Send(StatsPacket.Update(session.Player, BasicAttribute.Spirit));
            session.Send(StatsPacket.Update(session.Player, BasicAttribute.Stamina));
            return;
        }

        long startTick = session.Field.FieldTick;
        if (record.Metadata.Property.State != ActorState.None && record.StateNextTick == 0) {
            // Same first check delay as StateSkillHandler; with 0 the state skill was checked on the very next tick,
            // before the client could report the new state, and cancelled straight away.
            record.StateNextTick = startTick + (int) TimeSpan.FromSeconds(Math.Max(0.01f, record.Motion.MotionProperty.SequenceSpeed)).TotalMilliseconds;
        }
        session.Player.InBattle = record.Metadata.State.InBattle;
        session.Player.ActiveSkills.Add(record);
        session.Field.Broadcast(SkillPacket.Use(record));
        session.Field.Broadcast(StatsPacket.Init(session.Player));

        session.Player.ApplyEffects(record.Metadata.Data.Skills, session.Player, session.Player, EventConditionType.Activate, skillId: record.SkillId, targets: [session.Player]);
        session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCasted, skillId: record.SkillId);

        session.ConditionUpdate(ConditionType.skill, codeLong: skillId, targetLong: session.Field.MapId);
        session.Config.SaveSkillCooldown(record.Metadata, startTick);
    }

    private void HandlePoint(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Point Skill {SkillUid}", skillUid);
            return;
        }

        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        record.Position = packet.Read<Vector3>();
        record.Direction = packet.Read<Vector3>();

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Point: {skillUid}; AttackPoint: {attackPoint}"));
        }

        byte count = packet.ReadByte();
        // Note: counts up for skills that are held down (reused skillUid)
        int iterations = packet.ReadInt();

        for (byte i = 0; i < count; i++) {
            var targets = new List<TargetRecord>();
            var targetRecord = new TargetRecord {
                Uid = packet.ReadLong(),
                TargetId = packet.ReadInt(),
                Unknown = packet.ReadByte(),
            };
            targets.Add(targetRecord);

            // While more targets in packet.
            while (packet.ReadBool()) {
                targetRecord = new TargetRecord {
                    PrevUid = targetRecord.Uid,
                    Uid = packet.ReadLong(),
                    TargetId = packet.ReadInt(),
                    Unknown = packet.ReadByte(),
                    Index = packet.ReadByte(),
                };
                targets.Add(targetRecord);
            }

            session.Player.InBattle = true;

            // When the client reports no valid target (TargetId=0), the NPC may have died since the last
            // SubCommand.Target. Do server-side detection to find hittable corpses in attack range.
            if (targets.All(t => t.TargetId == 0) && session.Field != null) {
                float angle = MathF.Atan2(record.Direction.Y, record.Direction.X) * (180f / MathF.PI);
                Prism attackPrism = record.Attack.Range.GetPrism(record.Position, angle);
                foreach (FieldNpc npc in attackPrism.Filter(session.Field.Mobs.Values.Where(n => n.IsCorpse), record.Attack.TargetCount)) {
                    record.Targets.TryAdd(npc.ObjectId, npc);
                }

                // Fallback: if no corpse found in prism, pick the nearest corpse within range (non-directional).
                if (record.Targets.IsEmpty) {
                    float maxDist = record.Attack.Range.Distance + record.Attack.Range.RangeAdd.Y;
                    float maxHeight = record.Attack.Range.Height + record.Attack.Range.RangeAdd.Z;
                    FieldNpc? nearestCorpse = null;
                    float nearestDist = float.MaxValue;
                    foreach (FieldNpc npc in session.Field.Mobs.Values) {
                        if (!npc.IsCorpse) continue;
                        float dist = Vector2.Distance(new Vector2(record.Position.X, record.Position.Y),
                            new Vector2(npc.Position.X, npc.Position.Y));
                        float heightDelta = MathF.Abs(npc.Position.Z - record.Position.Z);
                        if (dist <= maxDist && heightDelta <= maxHeight && dist < nearestDist) {
                            nearestDist = dist;
                            nearestCorpse = npc;
                        }
                    }

                    if (nearestCorpse != null) {
                        record.Targets.TryAdd(nearestCorpse.ObjectId, nearestCorpse);
                    }
                }
                if (!record.Targets.IsEmpty) {
                    session.Player.TargetAttack(record);
                    record.Targets.Clear();
                    session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCastEnd, skillId: record.SkillId);
                    continue;
                }
            }

            session.Field?.Broadcast(SkillDamagePacket.Target(record, targets), session);

            // Unsure if this is correct.
            session.Buffs.TriggerEvent(session.Player, session.Player, session.Player, EventConditionType.OnSkillCastEnd, skillId: record.SkillId);
        }
    }

    private void HandleTarget(GameSession session, IByteReader packet) {
        if (session.Field == null) {
            return;
        }

        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Target Skill {SkillUid}", skillUid);
            return;
        }

        record.TargetUid = packet.ReadLong();
        record.ImpactPosition = packet.Read<Vector3>();
        packet.Read<Vector3>(); // ImpactPosition2
        record.Direction = packet.Read<Vector3>();
        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        byte count = packet.ReadByte();
        int iterations = packet.ReadInt();
        if (iterations != 0) {
            // A held skill (Iron Shield) counts its loops here; each loop packet is handled.
            Logger.Debug("Unhandled skill-Target iterations: ({Value}): {Record}", iterations, record);
        }

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Damage: {skillUid}; AttackPoint: {attackPoint}"));
        }

        // Read client target IDs
        var clientIds = new List<int>(count);
        for (int i = 0; i < count && packet.Available >= 5; i++) {
            clientIds.Add(packet.ReadInt());
            packet.ReadByte(); // unknown
        }

        // Pierce, bounce and chain: the targets the client reports, if the server also finds them in the attack's range
        // (around the impact point or from the caster), up to the skill's limit (DL-18).
        int limit = SkillUtils.MaxTargets(record.Attack.TargetCount, record.Attack.Arrow.BounceType, record.Attack.Arrow.BounceCount);
        SkillMetadataRange range = record.Attack.Range;
        Prism[] prisms = [range.GetPrism(record.ImpactPosition, record.Rotation.Z), range.GetPrism(record.Caster.Position, record.Rotation.Z)];
        Dictionary<int, IActor> inRange = new();
        foreach (IActor actor in session.Field.GetTargets(record.Caster, prisms, range, byte.MaxValue)) {
            inRange.TryAdd(actor.ObjectId, actor);
        }
        foreach (int id in SkillUtils.PickClientTargets(clientIds, inRange.Keys.ToHashSet(), limit)) {
            record.Targets.TryAdd(id, inRange[id]);
        }
        if (record.Targets.IsEmpty) { // nothing the client named is in range here: the server's own pick, as before
            foreach (IActor target in session.Field.GetTargets(record)) {
                record.Targets.TryAdd(target.ObjectId, target);
            }
        }
        session.Player.TargetAttack(record);
    }

    private void HandleCubeMagicPath(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Attack-Splash Skill {SkillUid}", skillUid);
            return;
        }

        byte attackPoint = packet.ReadByte();
        if (!record.TrySetAttackPoint(attackPoint)) {
            Logger.Error("Invalid AttackPoint({AttackPoint}) for {Record}", attackPoint, record);
            return;
        }

        int unknown1 = packet.ReadInt(); // Unknown(0)
        if (unknown1 != 0) {
            // A held skill's loop number (Smiting Aura 10400121), as the Target packet's iterations; nothing reads it.
            Logger.Debug("Unhandled skill-MagicPath value1({Value}): {Record}", unknown1, record);
        }

        int attackIndex = packet.ReadInt(); // Unknown(0)
        if (attackIndex != 0) {
            // The client's projectile number on magic paths with several projectiles (Fireball 99910021: 3); nothing reads it.
            Logger.Debug("Unhandled skill-MagicPath attackIndex ({Value}): {Record}", attackIndex, record);
        }

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Attack.Region: {skillUid}; AttackPoint: {attackPoint}; UnkInt: {unknown1}; UnkInt: {attackIndex}"));
        }

        record.Position = packet.Read<Vector3>();
        record.Rotation = packet.Read<Vector3>();

        session.Field?.AddSkill(record);
    }

    private void HandleSync(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Sync Skill {SkillUid}", skillUid);
            return;
        }
        int skillId = packet.ReadInt();
        short skillLevel = packet.ReadShort();
        byte motionPoint = packet.ReadByte();

        if (record.Metadata.Data.Motions.Length <= motionPoint) {
            Logger.Warning("Invalid motion point {MotionPoint} for {SkillRecord}", motionPoint, record);
            session.Send(SkillUseFailedPacket.Fail(record));
            return;
        }

        if (session.Player.Animation.PlayingSequence is null) {
            // A sync for a cast whose motion already ended or was cancelled is normal client traffic; the motion is replayed below.
            Logger.Debug("Last motion already expired on skill {SkillUid}", skillUid);
        }

        record.TrySetMotionPoint(motionPoint);

        var position = packet.Read<Vector3>();
        var direction = packet.Read<Vector3>(); // either velocity or direction
        var rotation = packet.Read<Vector3>();
        var input = packet.Read<Vector3>(); // x and y match with wasd/arrow input, not normalized
        bool isCharge = packet.ReadBool();
        bool isRelease = packet.ReadBool();
        int unk3 = packet.ReadInt();

        session.Field?.Broadcast(SkillPacket.Sync(record), session);

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Sync: {skillId},{skillUid}; AttackPoint: {motionPoint}; IsCharge: {isCharge}; IsReleased: {isRelease}; UnkInt: {unk3}; Direction: {direction}"));
        }

        SkillMetadataMotionProperty motion = record.Metadata.Data.Motions[motionPoint].MotionProperty;

        session.Animation.TryPlaySequence(motion.SequenceName, motion.SequenceSpeed, AnimationType.Skill, record.Metadata);
    }

    private void HandleTickSync(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid TickSync Skill {SkillUid}", skillUid);
            return;
        }

        record.ServerTick = packet.ReadInt();

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.SyncTick: {skillUid}"));
        }

        if (!session.Player.SkillCastConsume(record)) {
            session.Send(SkillUseFailedPacket.Fail(record));
            return;
        }

        string skillSequence = record.Motion.MotionProperty.SequenceName;
        string playingSequence = session.Player.Animation.PlayingSequence?.Name ?? "";

        if (skillSequence != playingSequence) {
            Logger.Warning($"Motion point on skill cast {skillUid} '{skillSequence}' doesn't match playing sequence '{playingSequence}'", skillUid);
            return;
        }

        session.Player.Animation.SetLoopSequence(true, true);
    }

    private void HandleCancel(GameSession session, IByteReader packet) {
        long skillUid = packet.ReadLong();
        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Cancel Skill {SkillUid}", skillUid);
            return;
        }

        session.Player.InBattle = true;
        session.Field?.Broadcast(SkillPacket.Cancel(record));

        if (session.Player.DebugSkills) {
            session.Send(NoticePacket.Message($"Skill.Cancel: {skillUid}"));
        }

        if (CancelsMotion(session.Player.ActiveSkills.GetMostRecent()?.CastUid, skillUid)) {
            session.Animation.CancelSequence();
        }
    }
}
