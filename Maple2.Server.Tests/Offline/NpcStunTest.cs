using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-24 (BT-68, C5): a stun (MotionProperty stun) reaches an NPC only when its abnormalImmuneBreak is above the NPC's
/// abnormalImmune, and holds it in state Stun until the buff ends. Goldens: the GMS build 12 captures' 209 stun adds on NPCs, every one
/// above the immunity, and the hits whose stun never came (Surnuny's 50000022 on Erda, 0 of 5; 50000004 on Pipe Zombie and its kind,
/// 0 of 46).</summary>
[TestFixture]
public class NpcStunTest {
    private const int Stun = 50000022, Debuff = 10500282;

    [TestCase(90, 85, true)] // Surnuny's 50000022 on Frostguard Brute 21421342
    [TestCase(90, 99, false)] // the same on Erda 21421347, 0 of 5
    [TestCase(85, 80, true)] // Erda's 50004504 on Surnuny 29000195, 3 of 3
    [TestCase(85, 98, false)] // the same on Bjorn 21421345
    [TestCase(99, 90, true)] // 51200002 on Mason 24120001 (HitImmune 99), 3 of 3
    [TestCase(99, 98, true)] // 50170012 on Pink Bean 23502021, 3 of 3
    [TestCase(5, 1, true)] // Allon's 70000007 on Pipe Zombie 24000206
    [TestCase(1, 1, false)] // 50000004 on Pipe Zombie and its kind, 0 of 46
    [TestCase(999, 99, true)] // 70000055 on Croc 24003011
    public void AStunReachesAnNpcOnlyAboveItsAbnormalImmunity(int immuneBreak, int abnormalImmune, bool reaches) {
        FieldNpc npc = Npc(abnormalImmune, (Stun, immuneBreak, 1));
        npc.Buffs.AddBuff(npc, npc, Stun, 1, 1000);
        Assert.That(npc.Buffs.HasBuff(Stun), Is.EqualTo(reaches));
    }

    [Test]
    public void ADebuffWithoutAStunIgnoresTheImmunity() {
        FieldNpc npc = Npc(99, (Debuff, 5, 0)); // 1,158 captured adds of such debuffs on Tin Training Dummy 52000005 (AbnormalImmune 99)
        npc.Buffs.AddBuff(npc, npc, Debuff, 1, 1000);
        Assert.Multiple(() => {
            Assert.That(npc.Buffs.HasBuff(Debuff), Is.True);
            Assert.That(npc.Buffs.HasStun(1000), Is.False);
        });
    }

    [Test]
    public void AStunHoldsTheNpcAndStopsItsCastUntilItEnds() {
        FieldNpc npc = Npc(1, (Stun, 90, 1));
        MovementState movement = npc.MovementState;
        var cast = new MovementState.NpcSkillCastTask(npc.TaskState, movement, 60200004, 2, 0, Vector3.Zero, 1);
        SetField(movement, "<CastTask>k__BackingField", cast);
        npc.Buffs.AddBuff(npc, npc, Stun, 1, 1000);

        Assert.Multiple(() => {
            Assert.That(npc.Buffs.HasStun(1000), Is.True);
            Assert.That(movement.Stun(npc.Buffs.HasStun(1000)), Is.True);
            Assert.That(movement.State, Is.EqualTo(ActorState.Stun));
            Assert.That(cast.Status, Is.EqualTo(NpcTaskStatus.Cancelled), "the cast does not resume");
            Assert.That(movement.CastTask, Is.Null);
            Assert.That(movement.Stun(true), Is.True, "it stays held");
            Assert.That(movement.State, Is.EqualTo(ActorState.Stun));
        });

        Assert.Multiple(() => { // the buff (8 s from tick 1000) ends at 9000; the server removes it only 1000 ms later
            Assert.That(npc.Buffs.HasStun(9000), Is.True);
            Assert.That(movement.Stun(npc.Buffs.HasStun(9001)), Is.False);
            Assert.That(npc.Buffs.HasBuff(Stun), Is.True, "the buff itself is still held");
            Assert.That(movement.State, Is.EqualTo(ActorState.Idle), "it goes idle when the buff ends");
        });
    }

    /// <summary>A bare NPC of that abnormal immunity on a bare field at FieldTick 1000 that knows the given effects (id, break, stun).</summary>
    private static FieldNpc Npc(int abnormalImmune, params (int Id, int ImmuneBreak, int Stun)[] effects) {
        var field = Bare<FieldManager>();
        SetField(field, "<SkillMetadata>k__BackingField", new Effects(effects));
        SetField(field, "<FieldTick>k__BackingField", 1000L);
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        var basic = Bare<NpcMetadataBasic>() with { HitImmune = abnormalImmune, AbnormalImmune = abnormalImmune };
        var metadata = new NpcMetadata(21421342, "Frostguard Brute", "", null!, null!, basic, null!, null!, null!, null!, null!, null!, null, null!);
        FieldNpc npc = Bare<FieldNpc>();
        SetField(npc, "<ObjectId>k__BackingField", 1);
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "<Field>k__BackingField", field);
        SetField(npc, "<Transform>k__BackingField", new Transform());
        SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
        SetField(npc, "BattleState", new BattleState(npc));
        SetField(npc, "TaskState", new TaskState(npc));
        MovementState movement = Bare<MovementState>();
        SetField(movement, "actor", npc);
        SetField(npc, "MovementState", movement);
        return npc;
    }

    private sealed class Effects : SkillMetadataStorage {
        public Effects((int Id, int ImmuneBreak, int Stun)[] effects) : base(null!) {
            foreach ((int id, int immuneBreak, int stun) in effects) {
                var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
                    new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, false);
                EffectCache.AddReplace((id, 1), new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(),
                    Bare<AdditionalEffectMetadataProperty>() with { Type = BuffType.Debuff, DurationTick = 8000, MaxCount = 1, ImmuneBreak = immuneBreak, Stun = stun },
                    Bare<AdditionalEffectMetadataConsume>(), Bare<AdditionalEffectMetadataReflect>(), new AdditionalEffectMetadataUpdate(null, [], [], [], []),
                    status, null, Bare<AdditionalEffectMetadataDot>(), null, null, [], [], []));
            }
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
