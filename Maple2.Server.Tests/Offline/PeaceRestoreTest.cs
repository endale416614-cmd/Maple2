using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-11: an NPC of a spawn point with PeaceRestore heals to full when its battle ends, and its next attacker tags it anew.
/// Golden: build 12 captures where such an NPC, left at part health, shows full health at its next update: Chomp Blossom (24000404,
/// Cherry Blossom Hill 2000290) 1,860 of 3,772, then 3,772 116 s later; Wind-up Soldier (24001502, 2000336) 24,519 of 38,449, then full.</summary>
[TestFixture]
public class PeaceRestoreTest {
    private const long MaxHealth = 3772; // Chomp Blossom
    private const long Left = 1860;
    private const int Player = 7;

    [Test]
    public void ABattleEndHealsToFullAndClearsTheTag() {
        FieldNpc npc = Npc(peaceRestore: true);
        Hit(npc, MaxHealth - Left);
        Idle(npc, inBattle: true);
        Assert.That(Health(npc), Is.EqualTo(Left), "no heal while the battle lasts");

        Idle(npc, inBattle: false); // the battle ended: it walks back
        Assert.Multiple(() => {
            Assert.That(Health(npc), Is.EqualTo(MaxHealth));
            Assert.That(npc.DamageTaken, Is.Zero);
            Assert.That(Get<int>(npc, "firstAttackerObjectId"), Is.Zero, "the next attacker tags it");
        });
    }

    [Test]
    public void WithoutPeaceRestoreABattleEndKeepsHealthAndTag() {
        FieldNpc npc = Npc(peaceRestore: false); // Watchtower Rampart's 42 spawn points, region spawns, summons
        Hit(npc, MaxHealth - Left);
        Idle(npc, inBattle: true);
        Idle(npc, inBattle: false);
        Assert.Multiple(() => {
            Assert.That(Health(npc), Is.EqualTo(Left));
            Assert.That(npc.DamageTaken, Is.EqualTo(MaxHealth - Left));
            Assert.That(Get<int>(npc, "firstAttackerObjectId"), Is.EqualTo(Player));
        });
    }

    [Test]
    public void AnAddedSpawnPointHasTheClientsDefault() {
        // offline-spawns.json (QF-06): a spawn point the data lacks takes spawnpointnpc.flat's default, so it heals with today's data.
        var spawn = new OfflineSpawns.Spawn(2000017, 29000022, 4, 6, 30); // Wiggler, Sylvan Woods Trail
        Assert.That(OfflineSpawns.ToSpawnPoint(spawn, new Ms2RegionSpawn(6, false, Vector3.Zero, Vector3.Zero)).PeaceRestore, Is.True);
    }

    // Maple2.Database's options for map entities (PropertyBuilderExtensions): a default value is not written.
    private static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new Vector3Converter() },
    };

    // Rows of the 1.2.2 data (02000001_tw_tria, 02000290_bf): data built before the map-entity re-import reads without the flag and
    // writes the same row; with the flag the row gains it.
    [TestCase("""{"!":207007606,"EntityId":"03549ef0d1a64c52a55dae28b89dedd3","SpawnOnFieldCreate":true,"NpcList":[{"NpcId":11000700,"Count":1}],"RegenCheckTime":10,"Position":{"X":-150,"Y":4950,"Z":3900},"Rotation":{"Z":270},"Visible":true}""",
        "\"RegenCheckTime\":10,")]
    [TestCase("""{"!":2038856760,"NpcCount":8,"SpawnAnimation":"Regen_A","EntityId":"5f8f84a99e4d4f68a576b9118bde5537","SpawnOnFieldCreate":true,"SpawnRadius":300,"NpcList":[{"NpcId":24000401,"Count":7},{"NpcId":24000402,"Count":3},{"NpcId":24000404,"Count":2}],"RegenCheckTime":10,"PatrolData":"","SpawnPointId":1004,"Position":{"X":-1800,"Y":-3000,"Z":1650},"Visible":true}""",
        "\"PatrolData\":\"\",")]
    public void RowWithoutPeaceRestore_ReadsFalse(string row, string before) {
        var spawn = (SpawnPointNPC) JsonSerializer.Deserialize<MapBlock>(row, Options)!;
        Assert.Multiple(() => {
            Assert.That(spawn.PeaceRestore, Is.False);
            Assert.That(JsonSerializer.Serialize<MapBlock>(spawn, Options), Is.EqualTo(row));
            Assert.That(JsonSerializer.Serialize<MapBlock>(spawn with { PeaceRestore = true }, Options),
                Is.EqualTo(row.Replace(before, before + "\"PeaceRestore\":true,")));
        });
    }

    /// <summary>A bare NPC with what DoIdleBehavior and RestorePeace read: its health, damage dealers, battle state, task queue, and a field
    /// with no thread.</summary>
    private static FieldNpc Npc(bool peaceRestore) {
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = MaxHealth }, [], [], [], []);
        var metadata = new NpcMetadata(24000404, "Chomp Blossom", "", null!, stat, null!, null!, null!, null!, null!, null!, null!, null, null!);
        FieldNpc npc = Bare<FieldNpc>();
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "<Field>k__BackingField", Bare<FieldManager>());
        SetField(npc, "<Stats>k__BackingField", new StatsManager(npc));
        SetField(npc, "DamageDealers", new ConcurrentDictionary<int, DamageRecordTarget>());
        SetField(npc, "<PeaceRestore>k__BackingField", peaceRestore);
        SetField(npc, "BattleState", new BattleState(npc));
        SetField(npc, "TaskState", new TaskState(npc));
        MovementState movement = Bare<MovementState>();
        SetField(movement, "actor", npc);
        SetField(npc, "MovementState", movement);
        return npc;
    }

    // What a player's hit leaves on the NPC (Actor.ApplyDamage, FieldNpc.OnDamageReceived): less health, a damage record, the drop tag.
    private static void Hit(FieldNpc npc, long amount) {
        var record = new DamageRecordTarget(npc);
        record.AddDamage(DamageType.Normal, amount);
        Get<ConcurrentDictionary<int, DamageRecordTarget>>(npc, "DamageDealers")[Player] = record;
        npc.Stats.Values[BasicAttribute.Health].Add(-amount);
        SetField(npc, "firstAttackerObjectId", Player);
    }

    // FieldNpc.Update's idle step, in battle or out of it (BattleState.InBattle).
    private static void Idle(FieldNpc npc, bool inBattle) {
        npc.BattleState.KeepBattle = inBattle;
        typeof(FieldNpc).GetMethod("DoIdleBehavior", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(npc, [0L]);
    }

    private static long Health(FieldNpc npc) => npc.Stats.Values[BasicAttribute.Health].Current;

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static FieldInfo Field(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) => Field(target, name).SetValue(target, value);

    private static T Get<T>(object target, string name) => (T) Field(target, name).GetValue(target)!;
}
