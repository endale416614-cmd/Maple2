using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>ST-02: buffs and debuffs never changed an NPC's stats, and an explicit walk speed ignored MovementSpeed (Magnetic Bomb's slow did nothing, BT-62).</summary>
[TestFixture]
public class NpcStatRebuildTest {
    private static T Bare<T>() where T : class => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static Dictionary<BasicAttribute, long> Data() => new() {
        { BasicAttribute.Health, 1000 },
        { BasicAttribute.MovementSpeed, 100 },
        { BasicAttribute.Defense, 50 },
        { BasicAttribute.PhysicalAtk, 200 },
    };

    private static Buff NpcBuff(Dictionary<BasicAttribute, long> values, Dictionary<BasicAttribute, float> rates, AdditionalEffectMetadataStatus.StatConversion? conversion = null) {
        var status = new AdditionalEffectMetadataStatus(values, rates, new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            new Dictionary<BasicAttribute, float>(), false, null, conversion, 0, false);
        var metadata = new AdditionalEffectMetadata(1, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        var owner = Bare<FieldNpc>();
        return new Buff(metadata, 1, owner, owner, 0, 0, 0);
    }

    private static StatsManager Damaged(Dictionary<BasicAttribute, long> data) {
        var manager = new StatsManager(Bare<FieldActor>());
        manager.Values.SetStaticStats(data);
        manager.Values[BasicAttribute.Health].Current = 400;
        return manager;
    }

    private static Buff[] SlowRageAndDisabled() {
        Buff slow = NpcBuff(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float> { { BasicAttribute.MovementSpeed, -0.3f } });
        Buff rage = NpcBuff(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 500 } }, new Dictionary<BasicAttribute, float> { { BasicAttribute.PhysicalAtk, 1.2f } });
        Buff off = NpcBuff(new Dictionary<BasicAttribute, long> { { BasicAttribute.Defense, 1000 } }, new Dictionary<BasicAttribute, float>());
        off.Disable();
        return [slow, rage, off];
    }

    [Test]
    public void NpcRebuildAppliesEnabledBuffsAndKeepsHealth() {
        StatsManager manager = Damaged(Data());
        manager.RebuildNpc(Data(), SlowRageAndDisabled());

        Stats stats = manager.Values;
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(70));
        Assert.That(stats[BasicAttribute.MovementSpeed].Current, Is.EqualTo(70));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(440));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1500));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(400));
        Assert.That(stats[BasicAttribute.Defense].Total, Is.EqualTo(50));
    }

    [Test]
    public void NpcRebuildWithoutBuffsReturnsToData() {
        StatsManager manager = Damaged(Data());
        manager.RebuildNpc(Data(), SlowRageAndDisabled());
        manager.RebuildNpc(Data(), []);

        Stats stats = manager.Values;
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(100));
        Assert.That(stats[BasicAttribute.MovementSpeed].Current, Is.EqualTo(100));
        Assert.That(stats[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(200));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(400));
    }

    [Test]
    public void NpcConversionCountsAfterRates() {
        var data = new Dictionary<BasicAttribute, long> { { BasicAttribute.Defense, 100 } };
        StatsManager manager = Damaged(data);
        Buff guard = NpcBuff(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, 1.0f } },
            new AdditionalEffectMetadataStatus.StatConversion(BasicAttribute.Defense, BasicAttribute.PhysicalAtk, 0.5f));
        manager.RebuildNpc(data, [guard]);

        Assert.That(manager.Values[BasicAttribute.Defense].Total, Is.EqualTo(200));
        Assert.That(manager.Values[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(100));
    }

    [Test]
    public void WalkSpeedRateScalesEveryWalk() {
        Assert.That(MovementState.WalkSpeedRate(0, new Stat(100)), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(1, new Stat(100)), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(2, new Stat(100)), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(0, new Stat(70, 100, 70)), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(1, new Stat(70, 100, 70)), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(2, new Stat(70, 100, 70)), Is.EqualTo(1.4f).Within(0.0001f));
        Assert.That(MovementState.WalkSpeedRate(0, new Stat(150)), Is.EqualTo(1.5f).Within(0.0001f));
    }
}
