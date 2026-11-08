using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>A stat rebuild set health, spirit and stamina to their totals (Feinting Guard, Shield Wall and Warhorn refilled them) and dropped the allocated points.</summary>
[TestFixture]
public class StatRebuildTest {
    private static Dictionary<BasicAttribute, long> Metadata(long health) {
        return new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Health, health },
            { BasicAttribute.Spirit, 100 },
            { BasicAttribute.Stamina, 120 },
        };
    }

    private static Stats Damaged() {
        var stats = new Stats(Metadata(1000), JobCode.Newbie);
        stats[BasicAttribute.Health].Current = 500;
        stats[BasicAttribute.Spirit].Current = 40;
        stats[BasicAttribute.Stamina].Current = 60;
        return stats;
    }

    [Test]
    public void PoolsSurviveRebuild() {
        Stats stats = Damaged();
        long[] saved = StatsManager.SavePools(stats);

        stats.Reset(Metadata(1000), JobCode.Newbie);
        stats[BasicAttribute.Health].AddTotal(90);
        stats.Total();
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1090));
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(1090));

        StatsManager.RestorePools(stats, saved);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(500));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(40));
        Assert.That(stats[BasicAttribute.Stamina].Current, Is.EqualTo(60));
    }

    [Test]
    public void PoolsAreClampedToTheNewTotals() {
        Stats stats = Damaged();
        long[] saved = StatsManager.SavePools(stats);

        stats.Reset(Metadata(400), JobCode.Newbie);
        stats.Total();

        StatsManager.RestorePools(stats, saved);
        Assert.That(stats[BasicAttribute.Health].Current, Is.EqualTo(400));
        Assert.That(stats[BasicAttribute.Spirit].Current, Is.EqualTo(40));
        Assert.That(stats[BasicAttribute.Stamina].Current, Is.EqualTo(60));
    }

    [Test]
    public void StatAttributeBonus() {
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.Health, 5), Is.EqualTo((BasicAttribute.Health, 50L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.Strength, 3), Is.EqualTo((BasicAttribute.Strength, 3L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.CriticalRate, 2), Is.EqualTo((BasicAttribute.CriticalRate, 6L)));
        Assert.That(ConfigManager.StatAttributeBonus(BasicAttribute.PhysicalAtk, 4).Amount, Is.EqualTo(0));
    }

    // ST-04 (BT-79). Golden from the data: Striker level 60 in userStat (Health 2952, Defense 60); Ariel's Wings 11800091 carries 90051110 (no stats),
    // 90051112 (weapon attack rate -0.1) and 90051113 (Defense rate 0.1), no condition, KeepCondition 99, gear score 63. The other pieces and the 5 Health points are round values.
    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static Buff ItemBuff(int id, Dictionary<BasicAttribute, float> rates) {
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), rates, new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            new Dictionary<BasicAttribute, float>(), false, null, null, 0, false);
        var metadata = new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        var owner = Bare<FieldNpc>();
        return new Buff(metadata, id, owner, owner, 0, 0, 0);
    }

    private static Buff NoStatBuff() => ItemBuff(90051110, new Dictionary<BasicAttribute, float>());

    private static Buff[] ArielsWings() => [
        NoStatBuff(),
        ItemBuff(90051112, new Dictionary<BasicAttribute, float> { { BasicAttribute.MinWeaponAtk, -0.1f }, { BasicAttribute.MaxWeaponAtk, -0.1f } }),
        ItemBuff(90051113, new Dictionary<BasicAttribute, float> { { BasicAttribute.Defense, 0.1f } }),
    ];

    /// <summary>The steps of RebuildPlayer and AddEquips in their order, on real stats and buffs: the pieces as Gear walks the slots (a weapon, the mantle with the buffs, a shirt, pants), the points, the buffs. A buff new to the player asks for a rebuild while the equips are counted, as BuffManager.AddBuff does.</summary>
    private sealed class TestPlayer(params Buff[] itemBuffs) {
        public readonly StatsManager Manager = new(Bare<FieldActor>());
        public readonly List<Buff> Buffs = [];
        public int Passes;

        public void Refresh(bool fill = false) => Manager.RunRebuild(fill, Pass);

        private void Pass(long[] pools) {
            Passes++;
            Stats stats = Manager.Values;
            stats.Reset(new Dictionary<BasicAttribute, long> { { BasicAttribute.Health, 2952 }, { BasicAttribute.Spirit, 100 }, { BasicAttribute.Stamina, 120 }, { BasicAttribute.Defense, 60 } }, JobCode.Striker);
            stats.GearScore = 0;

            stats[BasicAttribute.MaxWeaponAtk].AddTotal(1000);
            stats.GearScore += 50;

            foreach (Buff buff in itemBuffs) {
                if (!Buffs.Contains(buff)) {
                    Buffs.Add(buff);
                    if (buff.HasStats) {
                        Refresh();
                    }
                }
            }
            stats.GearScore += 63;

            stats[BasicAttribute.Defense].AddTotal(50);
            stats.GearScore += 50;
            stats[BasicAttribute.Defense].AddTotal(100);
            stats.GearScore += 50;

            (BasicAttribute attribute, long amount) = ConfigManager.StatAttributeBonus(BasicAttribute.Health, 5);
            stats[attribute].AddTotal(amount);

            Manager.AddBuffs(Buffs);
            stats.Total();
            Manager.StatConversion(Buffs);
            StatsManager.RestorePools(stats, pools);
        }
    }

    private static void AssertTotals(TestPlayer player, long defense, long weaponAttack) {
        Stats stats = player.Manager.Values;
        Assert.That(stats[BasicAttribute.Defense].Total, Is.EqualTo(defense));
        Assert.That(stats[BasicAttribute.MaxWeaponAtk].Total, Is.EqualTo(weaponAttack));
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(3002));
        Assert.That(stats.GearScore, Is.EqualTo(213));
    }

    [Test]
    public void AnEquipWithNewStatBuffsMatchesARelog() {
        var equip = new TestPlayer(ArielsWings());
        equip.Refresh(fill: true);

        Buff[] wings = ArielsWings();
        var relog = new TestPlayer(wings);
        relog.Buffs.AddRange(wings);
        relog.Refresh(fill: true);

        Assert.That(equip.Passes, Is.EqualTo(2), "two new stat buffs ask for one more pass between them");
        Assert.That(relog.Passes, Is.EqualTo(1));
        AssertTotals(equip, 231, 900);
        AssertTotals(relog, 231, 900);
        Assert.That(equip.Manager.Values[BasicAttribute.Health].Current, Is.EqualTo(3002), "a fill stays a fill on the second pass");
    }

    [Test]
    public void AnItemWithNoStatBuffTakesOnePass() {
        var none = new TestPlayer();
        none.Refresh();
        var noStats = new TestPlayer(NoStatBuff());
        noStats.Refresh();

        Assert.That(none.Passes, Is.EqualTo(1));
        Assert.That(noStats.Passes, Is.EqualTo(1));
        AssertTotals(none, 210, 1000);
        AssertTotals(noStats, 210, 1000);
    }

    [Test]
    public void TheSecondPassIsTheLastAndLeavesNothingBehind() {
        var manager = new StatsManager(Bare<FieldActor>());
        int passes = 0;
        manager.RunRebuild(false, _ => {
            passes++;
            manager.RunRebuild(false, _ => Assert.Fail("ran inside a pass"));
        });
        Assert.That(passes, Is.EqualTo(2));

        manager.RunRebuild(false, _ => passes++);
        Assert.That(passes, Is.EqualTo(3));
    }

    [Test]
    public void APoolClampedByTheFirstPassComesBack() {
        var manager = new StatsManager(Bare<FieldActor>());
        manager.Values[BasicAttribute.Health].Current = 500;
        int passes = 0;
        manager.RunRebuild(false, pools => {
            bool first = ++passes == 1;
            manager.Values.Reset(Metadata(first ? 400 : 1000), JobCode.Newbie);
            manager.Values.Total();
            StatsManager.RestorePools(manager.Values, pools);
            if (first) {
                Assert.That(manager.Values[BasicAttribute.Health].Current, Is.EqualTo(400));
                manager.RunRebuild(false, _ => Assert.Fail("ran inside a pass"));
            }
        });
        Assert.That(manager.Values[BasicAttribute.Health].Current, Is.EqualTo(500));
    }

    [Test]
    public void ARebuildOnAnotherThreadIsNotSwallowed() {
        var manager = new StatsManager(Bare<FieldActor>());
        int passes = 0;
        manager.RunRebuild(false, _ => {
            if (++passes == 1) {
                var other = new Thread(() => manager.RunRebuild(false, _ => passes += 10));
                other.Start();
                other.Join();
            }
        });
        Assert.That(passes, Is.EqualTo(11));
    }

    [Test]
    public void AFailedPassDoesNotBlockTheNextRebuild() {
        var manager = new StatsManager(Bare<FieldActor>());
        Assert.Throws<InvalidOperationException>(() => manager.RunRebuild(false, _ => throw new InvalidOperationException()));

        int passes = 0;
        manager.RunRebuild(false, _ => passes++);
        Assert.That(passes, Is.EqualTo(1));
    }
}
