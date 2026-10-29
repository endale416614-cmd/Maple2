using System.Numerics;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.PacketHandlers;

public class VibrateHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.Vibrate;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required SkillMetadataStorage SkillMetadata { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        string entityId = packet.ReadString();
        long skillUid = packet.ReadLong();
        int skillId = packet.ReadInt();
        short level = packet.ReadShort();
        byte motionPoint = packet.ReadByte();
        byte attackPoint = packet.ReadByte();

        SkillRecord? record = session.Player.ActiveSkills.Get(skillUid);
        if (record == null) {
            Logger.Warning("Invalid Skill {SkillUid}", skillUid);
            return;
        }

        record.ServerTick = packet.ReadInt();
        record.Position = packet.Read<Vector3>();

        // The client reports the attack it hit with; a point the skill does not have falls back to the record's own.
        SkillMetadataMotion[] motions = record.Metadata.Data.Motions;
        bool reported = motionPoint < motions.Length && attackPoint < motions[motionPoint].Attacks.Length;
        DamageRecord damage = new(record.Metadata, reported ? motions[motionPoint].Attacks[attackPoint] : record.Attack) {
            CasterId = session.Player.ObjectId,
            TargetUid = record.TargetUid,
            OwnerId = session.Player.ObjectId,
            SkillId = record.SkillId,
            Level = record.Level,
            MotionPoint = reported ? motionPoint : record.MotionPoint,
            AttackPoint = reported ? attackPoint : record.AttackPoint,
            Position = record.Position,
            Direction = record.Direction,
        };

        FieldManager? field = session.Field;
        if (field is null) {
            return;
        }

        FieldVibrateEntity? vibrate = field.AccelerationStructure?.GetVibrateEntity(entityId);
        if (reported && vibrate != null) {
            field.Vibrate(vibrate, damage, session);
            return;
        }

        // A cube the map-data does not know, or points the skill does not have, is echoed without a break.
        field.Broadcast(VibratePacket.Attack(entityId, damage, Environment.TickCount));
    }
}
