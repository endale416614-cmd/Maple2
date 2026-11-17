using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-04 (BT-253, BT-261, BT-266, BT-272): a buff with interval 0 acts once at its start plus its delay, acts again when it is
/// applied again, and is gone at its end, as the GMS captures show: Cunning Tactics' 10700141 (100 ms) heals at the tick it is added and
/// is removed 151 ms later; the Knight's 10100164 (100 ms) is removed 102 ms after its add. Before, the single proc came a duration plus
/// a second late and the buff ended with it, and a second application never acted again.</summary>
[TestFixture]
public class OneShotBuffTest {
    private const int CunningTactics = 10700141, Held = 10100164, Ticking = 70000001, Delayed = 70000002, Unlimited = 70000003;

    [Test]
    public void AOneShotActsWhenItIsAddedAndIsGoneAtItsEnd() {
        FieldNpc npc = Scene();
        npc.Buffs.AddBuff(npc, npc, CunningTactics, 1, 1000);
        Buff buff = npc.Buffs.EnumerateBuffs(CunningTactics).Single();
        buff.Update(1000);
        Assert.That(buff.ProcCount, Is.EqualTo(1), "at the add");
        buff.Update(1100);
        Assert.That(npc.Buffs.HasBuff(CunningTactics), Is.True, "at its end tick");
        buff.Update(1101);
        Assert.That(npc.Buffs.HasBuff(CunningTactics), Is.False, "past its end tick, not a second later");
        Assert.That(buff.ProcCount, Is.EqualTo(1));
    }

    [Test]
    public void ADelayMovesTheActionAndTheEnd() {
        FieldNpc npc = Scene();
        npc.Buffs.AddBuff(npc, npc, Delayed, 1, 1000); // 1000 ms, delay 100
        Buff buff = npc.Buffs.EnumerateBuffs(Delayed).Single();
        buff.Update(1099);
        Assert.That(buff.ProcCount, Is.EqualTo(0), "before its delay");
        buff.Update(1100);
        Assert.That(buff.ProcCount, Is.EqualTo(1), "at start plus delay");
        buff.Update(2100);
        Assert.That(npc.Buffs.HasBuff(Delayed), Is.True, "end tick plus delay");
        buff.Update(2101);
        Assert.That(npc.Buffs.HasBuff(Delayed), Is.False);
    }

    [Test]
    public void ASecondApplicationActsAgain() {
        FieldNpc npc = Scene();
        npc.Buffs.AddBuff(npc, npc, Held, 1, 1000); // 100 ms
        Buff buff = npc.Buffs.EnumerateBuffs(Held).Single();
        buff.Update(1000);
        npc.Buffs.AddBuff(npc, npc, Held, 1, 1050); // Fount of Renewal's next fire
        buff.Update(1050);
        Assert.That(buff.ProcCount, Is.EqualTo(2), "once per application");
        buff.Update(1150);
        Assert.That(buff.ProcCount, Is.EqualTo(2));
        Assert.That(npc.Buffs.HasBuff(Held), Is.True, "its end moved with the second application");
        buff.Update(1151);
        Assert.That(npc.Buffs.HasBuff(Held), Is.False);
    }

    [Test]
    public void ABuffWithAnIntervalAndAnUnlimitedOneKeepTheirRules() {
        FieldNpc npc = Scene();
        npc.Buffs.AddBuff(npc, npc, Ticking, 1, 1000); // 2000 ms, interval 500
        npc.Buffs.AddBuff(npc, npc, Unlimited, 1, 1000);
        Buff ticking = npc.Buffs.EnumerateBuffs(Ticking).Single(), unlimited = npc.Buffs.EnumerateBuffs(Unlimited).Single();
        ticking.Update(1499);
        Assert.That(ticking.ProcCount, Is.EqualTo(0), "its first action is after one interval");
        ticking.Update(1500);
        Assert.That(ticking.ProcCount, Is.EqualTo(1));
        npc.Buffs.AddBuff(npc, npc, Ticking, 1, 1600);
        ticking.Update(1600);
        Assert.That(ticking.ProcCount, Is.EqualTo(1), "a second application does not act early");
        unlimited.Update(1000);
        unlimited.Update(5000);
        Assert.That(unlimited.ProcCount, Is.EqualTo(0));
        Assert.That(npc.Buffs.HasBuff(Unlimited), Is.True);
    }

    private static FieldNpc Scene() {
        var field = Bare<FieldManager>();
        SetField(field, "<SkillMetadata>k__BackingField", new Effects());
        SetField(field, "<FieldTick>k__BackingField", 1000L);
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        var npc = Bare<FieldNpc>();
        SetField(npc, "<ObjectId>k__BackingField", 1);
        SetField(npc, "<Field>k__BackingField", field);
        SetField(npc, "<Transform>k__BackingField", new Transform());
        SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
        return npc;
    }

    private sealed class Effects : SkillMetadataStorage {
        public Effects() : base(null!) {
            Add(CunningTactics, 100, 0, 0, BuffKeepCondition.TimerDuration);
            Add(Held, 100, 0, 0, BuffKeepCondition.TimerDuration);
            Add(Ticking, 2000, 0, 500, BuffKeepCondition.TimerDuration);
            Add(Delayed, 1000, 100, 0, BuffKeepCondition.TimerDuration);
            Add(Unlimited, 0, 0, 0, BuffKeepCondition.UnlimitedDuration);
        }

        private void Add(int id, int duration, int delay, int interval, BuffKeepCondition keep) {
            var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
                new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, true);
            EffectCache.AddReplace((id, 1), new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(),
                Bare<AdditionalEffectMetadataProperty>() with { DurationTick = duration, DelayTick = delay, IntervalTick = interval, MaxCount = 1, KeepCondition = keep },
                Bare<AdditionalEffectMetadataConsume>(), Bare<AdditionalEffectMetadataReflect>(), new AdditionalEffectMetadataUpdate(null, [], [], [], []), status, null,
                Bare<AdditionalEffectMetadataDot>(), null, null, [], [], []));
        }
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
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
}
