using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Database.Storage;
using Maple2.Tools;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

// PI-09 (BT-264, BT-266): FinalStatusProperty rates of additional effects and the effects that apply only in PvP zones.
[TestFixture]
public class FinalStatusEffectsTest {
    [Test]
    public void Parse() {
        var config = FinalStatusEffects.Parse("""{"Effects": {"70000027": {"1": {"Rates": {"Health": 11.5}}}}, "PvpOnly": {"10200052": [1, 2]}}""");
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects[70000027][1].Rates!["Health"], Is.EqualTo(11.5f));
        Assert.That(config.PvpOnly![10200052], Is.EqualTo(new short[] { 1, 2 }));
    }

    // The shipped file holds the NA client's 44 effects with a final rate or value and 42 PvP-only effects.
    [Test]
    public void ShippedFile_CountsAndShadowPower() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "final-status-effects.json");
        Assert.That(File.Exists(path), Is.True, "final-status-effects.json ships next to the solution");
        var config = FinalStatusEffects.Parse(File.ReadAllText(path));
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects.Count, Is.EqualTo(44));
        Assert.That(config.PvpOnly!.Count, Is.EqualTo(42));
        Assert.That(FinalStatusEffects.Get(70000027, 1)!.Rates!["Health"], Is.EqualTo(11.5f), "Shadow Power");
        Assert.That(FinalStatusEffects.Attributes(70000027, 1), Is.EqualTo(new[] { (BasicAttribute.Health, 11.5f, 0f) }));
        Assert.That(FinalStatusEffects.Get(10100151, 1), Is.Null, "an effect without a final rate (Iron Shield)");
        Assert.That(FinalStatusEffects.IsPvpOnly(10200052, 1), Is.True, "Bloodlust's PvP copy");
        Assert.That(FinalStatusEffects.IsPvpOnly(10200051, 1), Is.False, "Bloodlust");
        Assert.That(FinalStatusEffects.IsPvpOnly(70000027, 1), Is.False, "Shadow Power");
    }

    [Test]
    public void AddFinalStats_ShadowPowerRaisesMaxHealthTo12AndAHalfTimes() {
        Stats stats = Totals(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000, [BasicAttribute.MovementSpeed] = 100 });
        StatsManager.AddFinalStats(stats, [Effect(70000027)]);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(12500));
        Assert.That(stats[BasicAttribute.MovementSpeed].Total, Is.EqualTo(100), "other stats stay");
    }

    [Test]
    public void AddFinalStats_AnEffectWithoutAFinalRateChangesNothing() {
        Stats stats = Totals(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000 });
        StatsManager.AddFinalStats(stats, [Effect(10100151)]);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
    }

    [Test]
    public void AddFinalStats_ADisabledBuffChangesNothing() {
        Stats stats = Totals(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000 });
        Buff buff = Effect(70000027);
        buff.Disable();
        StatsManager.AddFinalStats(stats, [buff]);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000));
    }

    [Test]
    public void AddFinalStats_AHugeTotalKeepsItsDigits() {
        Stats stats = Totals(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 100_000_001 });
        StatsManager.AddFinalStats(stats, [Effect(70000027)]);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1_250_000_012), "100,000,001 x 12.5 = 1,250,000,012.5; a float product gives 1,250,000,000");
    }

    [Test]
    public void HasStats_IsTrueForAFinalRate() {
        Assert.That(Effect(70000027).HasStats, Is.True, "Shadow Power has only a final rate");
        Assert.That(Effect(10100151).HasStats, Is.False, "no stats at all");
    }

    [Test]
    public void IsPvpZone_PvpAndShadowMaps() {
        Assert.That(FinalStatusEffects.IsPvpZone(MapType.Pvp), Is.True);
        Assert.That(FinalStatusEffects.IsPvpZone(MapType.Shadow), Is.True, "Calamitysnare 3000101, Savage Crossroads 3000003");
        Assert.That(FinalStatusEffects.IsPvpZone(MapType.None), Is.False);
    }

    [Test]
    public void KeepShare_FullStaysFullHalfStaysHalf() {
        Assert.That(StatsManager.KeepShare(1000, 1000, 12500), Is.EqualTo(12500));
        Assert.That(StatsManager.KeepShare(500, 1000, 12500), Is.EqualTo(6250));
        Assert.That(StatsManager.KeepShare(1, 1000, 12500), Is.EqualTo(12), "12.5 rounds to even");
        Assert.That(StatsManager.KeepShare(1, 100000, 100), Is.EqualTo(1), "never 0 for a living owner");
        Assert.That(StatsManager.KeepShare(0, 1000, 12500), Is.EqualTo(0), "a dead owner stays dead");
        Assert.That(StatsManager.KeepShare(2000, 1000, 12500), Is.EqualTo(12500), "never above the maximum");
    }

    // The real add and remove paths of a BuffManager on a bare monster: the added effect rebuilds the stats, and so does its removal.
    [Test]
    public void BuffManager_AddRebuildsAndRemoveRestoresMaxHealth() {
        (FieldNpc npc, Stats stats) = Monster(1000);
        npc.Buffs.AddBuff(npc, npc, 70000027, 1, 1000);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(12500), "added: the stats were rebuilt");
        npc.Buffs.Remove(70000027, npc.ObjectId);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1000), "removed: max health is back");
    }

    [Test]
    public void BuffManager_ABuffWithoutStatsRebuildsNothing() {
        (FieldNpc npc, Stats stats) = Monster(1000);
        stats[BasicAttribute.Health].AddTotal(7); // a rebuild would reset this
        npc.Buffs.AddBuff(npc, npc, 10100151, 1, 1000);
        Assert.That(stats[BasicAttribute.Health].Total, Is.EqualTo(1007));
    }

    private static (FieldNpc, Stats) Monster(long health) {
        var field = Bare<FieldManager>();
        SetField(field, "<SkillMetadata>k__BackingField", new Effects());
        SetField(field, "<FieldTick>k__BackingField", 1000L);
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        var npc = Bare<FieldNpc>();
        SetField(npc, "<ObjectId>k__BackingField", 1);
        SetField(npc, "<Field>k__BackingField", field);
        SetField(npc, "<Transform>k__BackingField", new Transform());
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = health }, [], [], [], []);
        var value = Bare<Npc>();
        SetField(value, "Metadata", Bare<NpcMetadata>() with { Stat = stat });
        SetField(npc, "<Value>k__BackingField", value);
        var stats = new Stats(stat.Stats, JobCode.None);
        var manager = Bare<StatsManager>();
        SetField(manager, "<Values>k__BackingField", stats);
        SetField(manager, "<Actor>k__BackingField", npc);
        SetField(npc, "<Stats>k__BackingField", manager);
        SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
        manager.Refresh();
        return (npc, stats);
    }

    // Shadow Power and Iron Shield, each at level 1 and 1 s long, with no stats in the ingested status (as in the data).
    private sealed class Effects : SkillMetadataStorage {
        public Effects() : base(null!) {
            foreach (int id in new[] { 70000027, 10100151 }) {
                var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
                    new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, true);
                EffectCache.AddReplace((id, 1), new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(),
                    Bare<AdditionalEffectMetadataProperty>() with { DurationTick = 99_000_000, MaxCount = 1 }, Bare<AdditionalEffectMetadataConsume>(),
                    Bare<AdditionalEffectMetadataReflect>(), new AdditionalEffectMetadataUpdate(null, [], [], [], []), status, null,
                    Bare<AdditionalEffectMetadataDot>(), null, null, [], [], []));
            }
        }
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static Stats Totals(Dictionary<BasicAttribute, long> values) => new(values, JobCode.None);

    // Effect id at level 1, held from tick 0 (no end).
    private static Buff Effect(int id) {
        var owner = Bare<FieldNpc>();
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, true);
        var metadata = new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        return new Buff(metadata, id, owner, owner, 0, 0, 0);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
