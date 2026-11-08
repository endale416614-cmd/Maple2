using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Util;

public static class DamageCalculator {
    private const int VengeanceEffect = 11000123; // Fighting Spirit's stacks

    public static (DamageType, long) CalculateDamage(IActor caster, IActor target, DamagePropertyRecord property) {
        // Check block
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.BlockChance, property.SkillId))) {
            return (DamageType.Block, 0);
        }

        // Check evase
        if (Damage.TriggerCompulsionEvent(target.Buffs.TotalCompulsionRate(BuffCompulsionEventType.EvasionChanceOverride, property.SkillId))) {
            return (DamageType.Miss, 0);
        }

        // Get hit rate
        if (Damage.Evade(target.Buffs.GetResistance(BasicAttribute.Accuracy), target.Stats.Values[BasicAttribute.Evasion].Total, target.Stats.Values[BasicAttribute.Dexterity].Total,
                caster.Stats.Values[BasicAttribute.Accuracy].Total, caster.Buffs.GetResistance(BasicAttribute.Evasion), caster.Stats.Values[BasicAttribute.Luck].Total, property.SuperArmorBreak)) {
            return (DamageType.Miss, 0);
        }

        var damageType = DamageType.Normal;
        (double minBonusAtkDamage, double maxBonusAtkDamage) = caster.Stats.GetBonusAttack(target.Buffs.GetResistance(BasicAttribute.BonusAtk), target.Buffs.GetResistance(BasicAttribute.MaxWeaponAtk));
        double attackDamage = minBonusAtkDamage + (maxBonusAtkDamage - minBonusAtkDamage) * double.Lerp(Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble());

        // change the NPCNormalDamage to be changed depending on target?
        // A damage special counts its rate as well as its value: gear and buffs give them as rates, which Multiplier() never read (BS-03).
        double Special(SpecialAttribute attribute) => Damage.Special(caster.Stats.Values[attribute].Total, caster.Stats.Values[attribute].Rate);
        double damageBonus = 1 + Special(SpecialAttribute.TotalDamage) + Special(SpecialAttribute.NormalNpcDamage);
        if (target is FieldNpc npc && npc.Value.IsBoss) {
            damageBonus += Special(SpecialAttribute.BossNpcDamage);
        }

        // Get elemental bonus
        double elementBonus = 0;
        switch (property.Element) {
            case Element.Fire:
                elementBonus = Special(SpecialAttribute.FireDamage);
                break;
            case Element.Ice:
                elementBonus = Special(SpecialAttribute.IceDamage);
                break;
            case Element.Electric:
                elementBonus = Special(SpecialAttribute.ElectricDamage);
                break;
            case Element.Holy:
                elementBonus = Special(SpecialAttribute.HolyDamage);
                break;
            case Element.Dark:
                elementBonus = Special(SpecialAttribute.DarkDamage);
                break;
            case Element.Poison:
                elementBonus = Special(SpecialAttribute.PoisonDamage);
                break;
        }
        damageBonus += elementBonus;

        // Get range bonus
        switch (property.RangeType) {
            case RangeType.None:
                break;
            case RangeType.Melee:
                damageBonus += Special(SpecialAttribute.MeleeDamage);
                break;
            case RangeType.Range:
                damageBonus += Special(SpecialAttribute.RangedDamage);
                break;
        }

        damageBonus += -target.Buffs.GetResistance(BasicAttribute.AttackSpeed) * caster.Stats.Values[BasicAttribute.AttackSpeed].Total;

        // Check for crit and get crit damage
        if (property.CanCrit) {
            if (property.CompulsionTypes.Contains(CompulsionType.Critical)) {
                damageType = DamageType.Critical;
            } else {
                damageType = caster.Stats.GetCriticalRate(target.Stats.Values[BasicAttribute.CriticalEvasion].Total, property.SkillId);
            }
        }

        damageBonus *= damageType == DamageType.Critical ? caster.Stats.GetCriticalDamage(target.Buffs.GetResistance(BasicAttribute.CriticalDamage)) : 1;

        // Get invoked buff values
        // TODO: Need to make this flexible to get invoke values from skills or buffs. If buff -> InvokeEffectType.IncreaseDotDamage
        (int invokeValue, float invokeRate) = caster.Buffs.GetInvokeValues(InvokeEffectType.IncreaseSkillDamage, property.SkillId, property.SkillGroup);

        double damageMultiplier = damageBonus * (1 + invokeRate) * (property.Rate + invokeValue);

        // Without piercing defense counts whole; the old term read no piercing as -100 % and halved every hit (FT-26a).
        Stat piercing = caster.Stats.Values[BasicAttribute.Piercing];
        double defensePierce = Damage.DefensePierce(Damage.Special(piercing.Total, piercing.Rate), target.Buffs.GetResistance(BasicAttribute.Piercing));
        damageMultiplier *= 1 / (Math.Max(target.Stats.Values[BasicAttribute.Defense].Total, 1) * defensePierce);

        // Check resistances
        double attackTypeAmount = 0;
        double resistance = 0;
        double finalDamage = 0;
        switch (property.AttackType) {
            case AttackType.Physical:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.PhysicalRes].Total, Special(SpecialAttribute.PhysicalPiercing));
                attackTypeAmount = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total;
                finalDamage = Special(SpecialAttribute.OffensivePhysicalDamage);
                break;
            case AttackType.Magic:
                resistance = Damage.CalculateResistance(target.Stats.Values[BasicAttribute.MagicalRes].Total, Special(SpecialAttribute.MagicalPiercing));
                attackTypeAmount = caster.Stats.Values[BasicAttribute.MagicalAtk].Total;
                finalDamage = Special(SpecialAttribute.OffensiveMagicalDamage);
                break;
            // If all, calculate the higher of the two
            case AttackType.All:
                BasicAttribute attackTypeAttribute = caster.Stats.Values[BasicAttribute.PhysicalAtk].Total >= caster.Stats.Values[BasicAttribute.MagicalAtk].Total
                    ? BasicAttribute.PhysicalAtk
                    : BasicAttribute.MagicalAtk;
                SpecialAttribute piercingAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.PhysicalPiercing
                    : SpecialAttribute.MagicalPiercing;
                SpecialAttribute finalDamageAttribute = attackTypeAttribute == BasicAttribute.PhysicalAtk
                    ? SpecialAttribute.OffensivePhysicalDamage
                    : SpecialAttribute.OffensiveMagicalDamage;
                attackTypeAmount = Math.Max(caster.Stats.Values[BasicAttribute.PhysicalAtk].Total, caster.Stats.Values[BasicAttribute.MagicalAtk].Total) * 0.5f;
                resistance = Damage.CalculateResistance(target.Stats.Values[Damage.ResistanceOf(attackTypeAttribute)].Total, Special(piercingAttribute));
                finalDamage = Special(finalDamageAttribute);
                break;
        }

        // Vengeance, +4 % per stack (BT-155): its first stack is in finalDamage with the other rates, its other stacks are added here.
        // TODO(BS-02): stacks of every effect count in the stats, which replaces this.
        Buff? vengeance = caster.Buffs.EnumerateBuffs(VengeanceEffect).FirstOrDefault(buff => buff.Enabled);
        if (vengeance != null) {
            float rate = vengeance.Metadata.Status.SpecialRates.GetValueOrDefault(SpecialAttribute.OffensivePhysicalDamage);
            finalDamage += Damage.StackedRate(rate, vengeance.Stacks) - 1 - rate;
        }
        damageMultiplier *= attackTypeAmount * resistance * (1 + finalDamage);
        attackDamage *= damageMultiplier * (caster is FieldPlayer ? Constant.AttackDamageFactor : Constant.NpcAttackDamageFactor) + property.Value;

        // DefensiveProperty rates of a player's enabled buffs (Iron Shield 10100151: 25-80 % less damage); the ingest drops them.
        if (target is FieldPlayer) {
            bool magic = property.AttackType == AttackType.Magic || (property.AttackType == AttackType.All
                && caster.Stats.Values[BasicAttribute.MagicalAtk].Total > caster.Stats.Values[BasicAttribute.PhysicalAtk].Total);
            // TODO: reductions only; debuffs that raise damage taken (Frozen +50 %, Ice Block 50003114 x6) wait for an in-game check.
            attackDamage *= Damage.DamageTakenMultiplier(target.Buffs.EnumerateBuffs().Where(buff => buff.Enabled)
                .Select(buff => Math.Max(0f, DefensiveEffects.Rate(buff.Id, buff.Level, magic))));
        }

        // Apply any shields
        foreach (Buff buff in target.Buffs.EnumerateBuffs().Where(buff => buff.Metadata.Shield != null)) {
            if (buff.ShieldHealth >= attackDamage) {
                buff.ShieldHealth -= (long) attackDamage;
                attackDamage = 0;
                target.Field.Broadcast(BuffPacket.Update(buff, BuffFlag.UpdateBuff));
                return (DamageType.Block, (long) attackDamage);
            }

            attackDamage -= buff.ShieldHealth;
            target.Buffs.Remove(buff.Id, target.ObjectId);
        }

        return (damageType, (long) Math.Max(1, attackDamage));
    }
}
