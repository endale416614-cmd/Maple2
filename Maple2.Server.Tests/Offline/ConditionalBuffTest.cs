using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>ST-03: a stat rebuild counted disabled buffs (Centered Mind's three tiers at once), and a buff turning on or off asked for no rebuild.</summary>
[TestFixture]
public class ConditionalBuffTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static StatsManager StatsOf(Stats values) {
        var manager = Bare<StatsManager>();
        SetField(manager, "<Values>k__BackingField", values);
        return manager;
    }

    private static Stats Fresh() => new(new Dictionary<BasicAttribute, long> { { BasicAttribute.MovementSpeed, 100 }, { BasicAttribute.Spirit, 100 } }, JobCode.Striker);

    // Centered Mind 11000151-11000153 level 1: the Spirit share each tier needs, and what it gives.
    private static Buff Tier(FieldNpc owner, int id, long speed, float damage, params BeginConditionTarget.BeginConditionStat[] spirit) {
        var target = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.Activate, false, [], []), spirit, [], [], new Dictionary<MasteryType, int>(), [], []);
        BeginCondition condition = Bare<BeginCondition>() with { Owner = target };
        var status = new AdditionalEffectMetadataStatus(
            new Dictionary<BasicAttribute, long> { { BasicAttribute.MovementSpeed, speed } },
            new Dictionary<BasicAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(),
            new Dictionary<SpecialAttribute, float> { { SpecialAttribute.OffensivePhysicalDamage, damage } },
            new Dictionary<BasicAttribute, float>(), false, null, null, 0, false);
        var metadata = new AdditionalEffectMetadata(id, 1, condition, Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        return new Buff(metadata, id, owner, owner, 0, 0, 0);
    }

    private static BeginConditionTarget.BeginConditionStat Spirit(float share, CompareType compare) => new(BasicAttribute.Spirit, share, compare, CompareStatValueType.CurrentPercentage);

    private static (BuffManager Buffs, Stats Owner) CenteredMind() {
        var owner = Bare<FieldNpc>();
        Stats ownerStats = Fresh();
        SetField(owner, "<Field>k__BackingField", Bare<FieldManager>());
        SetField(owner, "<Stats>k__BackingField", StatsOf(ownerStats));
        var buffs = new BuffManager(owner);
        var store = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", Any)!.GetValue(buffs)!;
        store[11000151] = [Tier(owner, 11000151, 10, 0.03f, Spirit(1, CompareType.Equals))];
        store[11000152] = [Tier(owner, 11000152, 6, 0.018f, Spirit(0.8f, CompareType.GreaterEquals), Spirit(1, CompareType.Less))];
        store[11000153] = [Tier(owner, 11000153, 2, 0.006f, Spirit(0.5f, CompareType.GreaterEquals), Spirit(0.8f, CompareType.Less))];
        return (buffs, ownerStats);
    }

    private static (long Speed, float Damage) Rebuild(BuffManager buffs) {
        Stats values = Fresh();
        StatsManager manager = StatsOf(values);
        manager.AddBuffs(buffs.EnumerateBuffs());
        values.Total();
        manager.StatConversion(buffs.EnumerateBuffs());
        return (values[BasicAttribute.MovementSpeed].Total, values[SpecialAttribute.OffensivePhysicalDamage].Rate);
    }

    [Test]
    public void RebuildCountsOnlyTheEnabledTier() {
        (BuffManager buffs, _) = CenteredMind();

        (long speed, float damage) = Rebuild(buffs);
        Assert.That(speed, Is.EqualTo(110));
        Assert.That(damage, Is.EqualTo(0.03f).Within(0.0001f));
    }

    [Test]
    public void TierChangeAsksForOneRebuild() {
        (BuffManager buffs, Stats owner) = CenteredMind();
        Assert.That(buffs.UpdateEnabled(), Is.False, "no tier changed");

        owner[BasicAttribute.Spirit].Current = 90;
        Assert.That(buffs.UpdateEnabled(), Is.True, "tier 1 off, tier 2 on");
        Assert.That(Rebuild(buffs).Speed, Is.EqualTo(106));

        owner[BasicAttribute.Spirit].Current = 40;
        Assert.That(buffs.UpdateEnabled(), Is.True, "tier 2 off");
        Assert.That(Rebuild(buffs).Speed, Is.EqualTo(100));
    }

    [Test]
    public void DisabledConversionAddsNothing() {
        var owner = Bare<FieldNpc>();
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null,
            new AdditionalEffectMetadataStatus.StatConversion(BasicAttribute.Spirit, BasicAttribute.MovementSpeed, 0.5f), 0, false);
        var metadata = new AdditionalEffectMetadata(90051195, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        var buff = new Buff(metadata, 1, owner, owner, 0, 0, 0);
        Assert.That(buff.HasStats, Is.True, "a conversion changes the totals too");
        Stats values = Fresh();
        values.Total();

        StatsOf(values).StatConversion([buff]);
        Assert.That(values[BasicAttribute.MovementSpeed].Total, Is.EqualTo(150));

        buff.Disable();
        values = Fresh();
        values.Total();
        StatsOf(values).StatConversion([buff]);
        Assert.That(values[BasicAttribute.MovementSpeed].Total, Is.EqualTo(100));
    }
}
