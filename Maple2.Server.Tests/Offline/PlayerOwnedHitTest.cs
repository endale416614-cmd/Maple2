using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-26c (BT-243): a pet's hit takes the player factor (4) where a monster's takes the monster factor (2.8), and a stat made by a buff's
/// conversion counts toward attack. A reconstruction: no capture shows a pet's or a summon's hit, and a conversion's attack. The numbers of a
/// player's hit and a monster's hit (DamageCalculatorTest) do not change.</summary>
[TestFixture]
public class PlayerOwnedHitTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // The monster and the player of DamageCalculatorTest.AMonsterHitsAPlayer (NPC 21421103, skill 40600096 level 1, rate 1): 309 at factor 2.8.
    private static readonly Dictionary<BasicAttribute, long> Attacker = new() {
        { BasicAttribute.PhysicalAtk, 125285 }, { BasicAttribute.Accuracy, 1000 }, { BasicAttribute.Health, 100000 },
    };
    private static readonly Dictionary<BasicAttribute, long> Target = new() {
        { BasicAttribute.Defense, 1034 }, { BasicAttribute.PhysicalRes, 130 }, { BasicAttribute.Health, 20000 },
    };

    private static DamagePropertyRecord Hit() => new() {
        Rate = 1f, AttackType = AttackType.Physical, CanCrit = false, SuperArmorBreak = true, // no evasion roll, no critical roll
    };

    private static long DamageOf(IActor caster) {
        FieldPlayer target = NewPlayer(Target);
        (DamageType type, long damage) = DamageCalculator.CalculateDamage(caster, target, Hit());
        Assert.That(type, Is.EqualTo(DamageType.Normal));
        return damage;
    }

    private static FieldPlayer NewPlayer(Dictionary<BasicAttribute, long> stats) {
        var player = Bare<FieldPlayer>();
        var session = Bare<GameSession>();
        var character = Bare<Character>();
        character.Job = Job.RuneBlader;
        var model = Bare<Maple2.Model.Game.Player>();
        SetField(model, "Character", character);
        SetField(player, "<Value>k__BackingField", model);
        SetField(player, "Session", session);
        SetField(player, "<ObjectId>k__BackingField", 3);
        session.Stats = Stats(player, stats);
        session.Buffs = new BuffManager(player);
        return player;
    }

    private static FieldNpc Monster() {
        var npc = Bare<FieldNpc>();
        NpcMetadata metadata = Bare<NpcMetadata>() with { Basic = Bare<NpcMetadataBasic>() with { Friendly = 0, Class = 1 } };
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "<ObjectId>k__BackingField", 2);
        SetField(npc, "<Stats>k__BackingField", Stats(npc, Attacker));
        SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
        return npc;
    }

    private static FieldPet Pet(int ownerObjectId) {
        var pet = Bare<FieldPet>();
        NpcMetadata metadata = Bare<NpcMetadata>() with { Basic = Bare<NpcMetadataBasic>() with { Friendly = 0, Class = 1 } };
        SetField(pet, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(pet, "<ObjectId>k__BackingField", 4);
        SetField(pet, "<Stats>k__BackingField", Stats(pet, Attacker));
        SetField(pet, "<Buffs>k__BackingField", new BuffManager(pet));
        if (ownerObjectId != 0) {
            var owner = Bare<FieldPlayer>();
            SetField(owner, "<ObjectId>k__BackingField", ownerObjectId);
            SetField(pet, "owner", owner);
        }
        return pet;
    }

    [Test]
    public void AMonstersHitKeepsTheMonsterFactor() {
        Assert.That(DamageOf(Monster()), Is.EqualTo(309), "DamageCalculatorTest's number");
    }

    [Test]
    public void AWildPetKeepsTheMonsterFactor() {
        Assert.That(DamageOf(Pet(0)), Is.EqualTo(309));
    }

    [Test]
    public void APlayersPetHitsWithThePlayerFactor() {
        // factor 4 where the monster's is 2.8: 309.9 x 4 / 2.8 = 442.7, truncated.
        Assert.That(DamageOf(Pet(1)), Is.EqualTo(442));
    }

    [Test]
    public void OnlyAPlayerAndItsPetUseThePlayerFactor() {
        Assert.Multiple(() => {
            Assert.That(DamageCalculator.UsesPlayerFactor(Bare<FieldPlayer>()), Is.True);
            Assert.That(DamageCalculator.UsesPlayerFactor(Pet(1)), Is.True);
            Assert.That(DamageCalculator.UsesPlayerFactor(Pet(0)), Is.False);
            Assert.That(DamageCalculator.UsesPlayerFactor(Monster()), Is.False);
            Assert.That(DamageCalculator.UsesPlayerFactor(Bare<FieldActor>()), Is.False, "a map's or a trigger's region");
        });
    }

    // Wizard: attack from INT 0.5666 a point. A buff that converts 70 % of Strength (100) into Intelligence (10900041's shape).
    private static Buff Conversion(bool enabled) {
        var owner = Bare<FieldNpc>();
        var status = new AdditionalEffectMetadataStatus(new Dictionary<BasicAttribute, long>(), new Dictionary<BasicAttribute, float>(), new Dictionary<SpecialAttribute, float>(),
            new Dictionary<SpecialAttribute, float>(), new Dictionary<BasicAttribute, float>(), false, null,
            new AdditionalEffectMetadataStatus.StatConversion(BasicAttribute.Strength, BasicAttribute.Intelligence, 0.7f), 0, false);
        var metadata = new AdditionalEffectMetadata(10900041, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, status, null, null!, null, null, [], [], []);
        var buff = new Buff(metadata, 1, owner, owner, 0, 0, 0);
        if (!enabled) {
            buff.Disable();
        }
        return buff;
    }

    private static (StatsManager Manager, Stats Values) Rebuild(params Buff[] buffs) {
        var manager = Bare<StatsManager>();
        var values = new Stats(new Dictionary<BasicAttribute, long> {
            { BasicAttribute.Strength, 100 }, { BasicAttribute.Dexterity, 50 }, { BasicAttribute.Intelligence, 200 }, { BasicAttribute.Luck, 30 }, { BasicAttribute.Health, 1000 },
        }, JobCode.Wizard);
        SetField(manager, "<Values>k__BackingField", values);
        values[BasicAttribute.Strength].AddTotal(20); // a piece of gear: its attack counts as before
        manager.AddBuffs(buffs);
        values.Total();
        manager.ConvertThenAddStatAttack(buffs, JobCode.Wizard);
        return (manager, values);
    }

    [Test]
    public void AStatMadeByAConversionAddsAttack() {
        (_, Stats values) = Rebuild(Conversion(true));
        long baseAttack = AttackStat.MagicalAtk(JobCode.Wizard, 200);
        long made = (long) (120 * 0.7f); // 70 % of the Strength 120
        Assert.That(values[BasicAttribute.Intelligence].Total, Is.EqualTo(200 + made));
        Assert.That(values[BasicAttribute.MagicalAtk].Total, Is.EqualTo(AttackStat.MagicalAtk(JobCode.Wizard, 200 + made)));
        Assert.That(values[BasicAttribute.MagicalAtk].Total, Is.GreaterThan(baseAttack), "more than the base Intelligence gives");
    }

    [Test]
    public void AStrengthFromGearStillAddsAttackAndADisabledConversionNone() {
        (_, Stats values) = Rebuild(Conversion(false));
        Assert.That(values[BasicAttribute.MagicalAtk].Total, Is.EqualTo(AttackStat.MagicalAtk(JobCode.Wizard, 200)), "no conversion, no Intelligence");
        Assert.That(values[BasicAttribute.PhysicalAtk].Total, Is.EqualTo(AttackStat.PhysicalAtk(JobCode.Wizard, 120, 50, 30)), "the gear's Strength counts, as before");
    }

    private static StatsManager Stats(IActor actor, Dictionary<BasicAttribute, long> values) {
        var manager = new StatsManager(Bare<FieldActor>()); // empty stats of any actor
        SetField(manager, "<Actor>k__BackingField", actor);
        manager.Values.SetStaticStats(values);
        return manager;
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

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
}
