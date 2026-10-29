using DotRecast.Detour.Crowd;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Network;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using System.Numerics;
using static Maple2.Server.Game.Model.ActorStateComponent.TaskState;

namespace Maple2.Server.Game.Model;

public sealed class FieldPet : FieldNpc {
    public enum FollowAction { None, Trace, Warp }

    private readonly FieldPlayer? owner;
    public readonly Item Pet;
    public readonly PetMetadata Metadata;

    public int OwnerId => owner?.ObjectId ?? 0;
    public int SkinId { get; private set; }

    public readonly float Scale = 1f;

    public short TamingRank; // (0-70=green, 70-95=yellow, 95-100=red)
    public int TamingPoint;
    private long tamingTick;
    private long followTick;
    private NpcTask? followTask;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => Field.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public FieldPet(FieldManager field, int objectId, DtCrowdAgent agent, Npc npc, Item pet, PetMetadata petMetadata, string aiPath, FieldPlayer? owner = null) : base(field, objectId, agent, npc, aiPath) {
        this.owner = owner;
        Pet = pet;

        Metadata = petMetadata;

        // Wild pets need a TamingRank.
        if (owner == null) {
            TamingRank = (short) (Random.Shared.Next(100) + 1);
        } else {
            BattleState.CanBattle = false; // an owned pet must never target its owner; the taming AI then never runs for it
        }

        // Use Badge for SkinId if equipped.
        if (owner != null && owner.Session.Item.Equips.Badge.TryGetValue(BadgeType.PetSkin, out Item? value)) {
            SkinId = value.Badge?.PetSkinId ?? Value.Id;
        } else {
            SkinId = Value.Id;
        }
    }

    /// <summary>What an owned pet does about its owner's distance: nothing, walk to the owner, or warp to the owner (a Warp of 0 never warps).</summary>
    public static FollowAction FollowStep(Vector3 pet, Vector3 owner, PetMetadataDistance distance) {
        float squared = Vector3.DistanceSquared(pet, owner);
        if (distance.Warp > 0 && squared > distance.Warp * distance.Warp) {
            return FollowAction.Warp;
        }

        return squared > distance.Trace * distance.Trace ? FollowAction.Trace : FollowAction.None;
    }

    public override void Update(long tickCount) {
        if (owner != null && !IsDead && followTick < tickCount) {
            followTick = tickCount + 500;
            Origin = owner.Position; // the idle wander (a patrol point around Origin) stays around the owner

            switch (FollowStep(Position, owner.Position, Metadata.Distance)) {
                case FollowAction.Warp:
                    followTask?.Cancel();
                    if (Navigation?.UpdatePosition(owner.Position) != true) {
                        Position = owner.Position;
                    }
                    break;
                case FollowAction.Trace:
                    // The walk goes to the owner's position now; the next check starts a new one while the owner moves.
                    followTask = MovementState.TryMoveTargetDistance(owner, Metadata.Distance.Trace * 0.5f, true);
                    break;
            }
        }

        base.Update(tickCount);

        if (TamingPoint > 0 && tamingTick < tickCount) {
            tamingTick = tickCount + (long) TimeSpan.FromSeconds(1).TotalMilliseconds;
            TamingPoint = Math.Max(0, TamingPoint - 100);
            Field.Broadcast(PetPacket.SyncTaming(BattleState.TargetId, this));
        }
    }

    public override void ApplyDamage(IActor caster, DamageRecord damage, SkillMetadataAttack attack) {
        // A captured pet stays in the field until removed; it must not be captured twice. An owned pet is not wild.
        if (attack.Pet == null || IsDead || owner != null) {
            return;
        }

        if (BattleState.TargetId != 0 && BattleState.TargetId != caster.ObjectId && caster is FieldPlayer player) {
            player.Session.Send(NoticePacket.Notice(NoticePacket.Flags.Message | NoticePacket.Flags.Alert, new InterfaceText("s_err_taming_attack_other_user"))); // No string code for this?
            return;
        }

        var targetRecord = new DamageRecordTarget(this);
        int damageAmount = TamingPoint - Math.Min(TamingPoint + attack.Pet.TamingPoint, Constants.TamingPetMaxPoint);
        TamingPoint -= damageAmount;
        targetRecord.AddDamage(damageAmount == 0 ? DamageType.Miss : DamageType.Normal, damageAmount);

        if (OfflinePets.Captures(attack.Pet.TrapLevel, attack.Pet.ForcedTaming, TamingPoint, Constants.TamingPetMaxPoint)) {
            IsDead = true;
            OnDeath();
            DropItem(caster);
        }

        damage.Targets.TryAdd(ObjectId, targetRecord);
    }

    protected override void Remove(TimeSpan delay) => Field.RemovePet(ObjectId, delay);

    public void UpdateSkin(int skinId) {
        SkinId = skinId > 0 ? skinId : Value.Id;
    }

    private void DropItem(IActor capturer) {
        // TODO: Determine rarity of pet drop
        int rarity = OfflinePets.CaptureRarity(TamingRank);

        using GameStorage.Request db = Field.GameStorage.Context();
        Item? pet = Pet.Mutate(Pet.Metadata, rarity);
        pet.Stats = Field.ItemStatsCalc.GetStats(pet);

        pet = db.CreateItem(0, pet);
        if (pet == null) {
            return;
        }

        FieldItem fieldItem = Field.SpawnItem(this, pet);
        if (capturer is FieldPlayer player) {
            player.Session.Send(FieldPacket.DropItem(fieldItem));
        } else {
            Field.Broadcast(FieldPacket.DropItem(fieldItem));
        }
    }
}
