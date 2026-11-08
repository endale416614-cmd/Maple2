using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>TG-03 (BT-219): add_buff with ignore_player and remove_buff name monsters by spawn id, as captured (Mason Phases: 02020027_bf's
/// 62000002 reaches Mason, spawn 201, and its Cancel ends his 62000001 at that tick, 3 of 3). The adds in SpawnBuffsOff stay off.</summary>
[TestFixture]
public class ScriptBuffSpawnIdTest {
    [Test]
    public void AnAddBySpawnIdReachesThatSpawnsLiveNpcsOnly() {
        var scene = new Scene();
        FieldNpc mason = scene.Npc(1, 201), add = scene.Npc(2, 301), dead = scene.Npc(3, 201);
        SetField(dead, "<IsDead>k__BackingField", true);
        mason.Buffs.AddBuff(mason, mason, 62000001, 1, 1000); // his own 20 s window (skill 60200009)
        scene.Context.AddBuff([201], 62000002, 1, true, false, string.Empty);
        Assert.That(mason.Buffs.HasBuff(62000002), Is.True);
        Assert.That(mason.Buffs.HasBuff(62000001), Is.False, "62000002's Cancel ends the window");
        Assert.That(add.Buffs.EnumerateBuffs(), Is.Empty);
        Assert.That(dead.Buffs.EnumerateBuffs(), Is.Empty);
    }

    [Test]
    public void ARemoveBySpawnIdTakesItOff() {
        var scene = new Scene();
        FieldNpc npc = scene.Npc(1, 103, friendly: true); // in Field.Npcs, as Allon
        npc.Buffs.AddBuff(npc, npc, 62000001, 1, 1000);
        scene.Context.RemoveBuff(103, 62000001, false); // as 52000099_qd writes it: no is_player
        Assert.That(npc.Buffs.EnumerateBuffs(), Is.Empty);
    }

    [Test]
    public void TheExcludedAddsStayOff() {
        var scene = new Scene();
        FieldNpc zakum = scene.Npc(1, 2011), fighter = scene.Npc(2, 101);
        scene.Context.AddBuff([2011], 50000265, 1, true, false, string.Empty);
        scene.Context.AddBuff([101], 69000501, 1, true, false, string.Empty); // a Colosseum round's 7 s window goes on
        Assert.That(zakum.Buffs.EnumerateBuffs(), Is.Empty);
        Assert.That(fighter.Buffs.HasBuff(69000501), Is.True);
    }

    /// <summary>A bare field at FieldTick 1000 with one script, no trigger box and the effects below.</summary>
    private sealed class Scene {
        private readonly FieldManager field = Bare<FieldManager>();
        public readonly TriggerContext Context = Bare<TriggerContext>();

        public Scene() {
            SetField(field, "<SkillMetadata>k__BackingField", new Effects());
            SetField(field, "<FieldTick>k__BackingField", 1000L);
            SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
            SetField(field, "<Npcs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
            SetField(field, "<Mobs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
            SetField(field, "FieldActor", Bare<FieldActor>());
            var objects = Bare<TriggerCollection>();
            SetField(objects, "Boxes", new Dictionary<int, TriggerBox>());
            SetField(field, "TriggerObjects", objects);
            var trigger = Bare<FieldTrigger>();
            SetField(trigger, "<Field>k__BackingField", field);
            SetField(Context, "owner", trigger);
        }

        public FieldNpc Npc(int objectId, int spawnId, bool friendly = false) {
            var npc = Bare<FieldNpc>();
            SetField(npc, "<ObjectId>k__BackingField", objectId);
            SetField(npc, "<Field>k__BackingField", field);
            SetField(npc, "<Transform>k__BackingField", new Transform());
            SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
            npc.SpawnPointId = spawnId;
            (friendly ? field.Npcs : field.Mobs)[objectId] = npc;
            return npc;
        }
    }

    private sealed class Effects : SkillMetadataStorage {
        public Effects() : base(null!) {
            Add(62000001, 20000, null); // Mason's invincible window
            Add(62000002, 1000, new AdditionalEffectMetadataUpdate.CancelEffect(false, false, [62000001], []));
            Add(50000265, 99999000, null); // Zakum's arm phase
            Add(69000501, 7000, null);
        }

        private void Add(int id, int duration, AdditionalEffectMetadataUpdate.CancelEffect? cancel) {
            var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(),
                new Dictionary<SpecialAttribute, float>(), new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null, null, 0, true);
            EffectCache.AddReplace((id, 1), new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(),
                Bare<AdditionalEffectMetadataProperty>() with { DurationTick = duration, MaxCount = 1 }, Bare<AdditionalEffectMetadataConsume>(),
                Bare<AdditionalEffectMetadataReflect>(), new AdditionalEffectMetadataUpdate(cancel, [], [], [], []), status, null,
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
