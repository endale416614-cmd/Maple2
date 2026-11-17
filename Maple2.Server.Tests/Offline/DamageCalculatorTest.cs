using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-04 (BT-243): DamageCalculator.CalculateDamage called on real actors, a player hitting a monster and a monster hitting a player, with
/// the stats of the GMS captures DamageFormulaTest cites. The earlier test recomputes the formula inside itself and never reaches the calculator.
/// The hits are held at today's numbers: a player's hit rolls between its minimum and maximum weapon attack, a monster's hit has no roll.</summary>
[TestFixture]
public class DamageCalculatorTest {
    // Golden: the Runeblader of DamageFormulaTest (physical attack 117) with the weapon of skill 10900032 level 5 (148 to 192), normal hits on
    // NPC 29000252 (Defense 141, PhysicalRes 90): the capture's hits run 271 to 346 (38 hits).
    private static readonly Dictionary<BasicAttribute, long> Runeblader = new() {
        { BasicAttribute.PhysicalAtk, 117 }, { BasicAttribute.MinWeaponAtk, 148 }, { BasicAttribute.MaxWeaponAtk, 192 },
        { BasicAttribute.Accuracy, 1000 }, { BasicAttribute.Health, 1000 },
    };
    private static readonly Dictionary<BasicAttribute, long> Corrupted = new() {
        { BasicAttribute.Defense, 141 }, { BasicAttribute.PhysicalRes, 90 }, { BasicAttribute.Health, 100000 },
    };

    // Golden: NPC 21421103 (Lv 60, attack 125285), skill 40600096 level 1, rate 1, on a player with Defense 1034 and physical resistance 130:
    // 19 captured hits of 310 to 322.
    private static readonly Dictionary<BasicAttribute, long> Monster = new() {
        { BasicAttribute.PhysicalAtk, 125285 }, { BasicAttribute.Accuracy, 1000 }, { BasicAttribute.Health, 100000 },
    };
    private static readonly Dictionary<BasicAttribute, long> Player = new() {
        { BasicAttribute.Defense, 1034 }, { BasicAttribute.PhysicalRes, 130 }, { BasicAttribute.Health, 20000 },
    };

    private static DamagePropertyRecord Hit(float rate) => new() {
        Rate = rate, AttackType = AttackType.Physical, CanCrit = false, SuperArmorBreak = true, // no evasion roll, no critical roll
    };

    [Test]
    public void APlayerHitsAMonster() {
        FieldPlayer player = NewPlayer(Runeblader);
        FieldNpc monster = NewNpc(Corrupted, friendly: 0);

        long[] hits = Enumerable.Range(0, 3000).Select(_ => DamageCalculator.CalculateDamage(player, monster, Hit(0.57f))).Select(hit => {
            Assert.That(hit.Item1, Is.EqualTo(DamageType.Normal));
            return hit.Item2;
        }).ToArray();

        // factor 4 x rate 0.57 x attack 117 x resistance (1500 - 90) / 1500 / defense 141 = 1.7783 a weapon point: 148 gives 263, 192 gives 341.
        Assert.That(hits.Min(), Is.InRange(263, 270), "the lowest rolls near the minimum weapon attack");
        Assert.That(hits.Max(), Is.InRange(335, 341), "the highest rolls near the maximum weapon attack");
        Assert.That(hits.All(hit => hit is >= 263 and <= 341), Is.True);
        Assert.That(hits.Average(), Is.EqualTo(302).Within(4), "the weapon roll is centred");
        // The capture's 38 hits (271 to 346) sit within 5 % of the calculated range, as DamageFormulaTest's own bound.
        Assert.That(hits.Min() * 0.95, Is.LessThanOrEqualTo(271));
        Assert.That(hits.Max() * 1.05, Is.GreaterThanOrEqualTo(346));
    }

    [Test]
    public void AMonsterHitsAPlayer() {
        FieldNpc monster = NewNpc(Monster, friendly: 0);
        FieldPlayer player = NewPlayer(Player);

        (DamageType type, long damage) = DamageCalculator.CalculateDamage(monster, player, Hit(1f));
        Assert.That(type, Is.EqualTo(DamageType.Normal));
        // factor 2.8 x rate 1 x attack 125285 x resistance (1500 - 130) / 1500 / defense 1034 = 309.9, truncated; the monster's weapon roll is fixed.
        Assert.That(damage, Is.EqualTo(309));
        Assert.That(damage, Is.InRange(310 * 0.95, 322 * 1.05), "the captured hits are 310 to 322");
        Assert.That(Enumerable.Range(0, 100).Select(_ => DamageCalculator.CalculateDamage(monster, player, Hit(1f)).Item2).Distinct().Single(), Is.EqualTo(309), "no roll");
    }

    // BL-01: the calculator takes the factors from damage-factors.json: a player's hit and a monster's hit follow their own factor.
    [Test]
    public void TheFactorsOfTheFileScaleTheHits() {
        DamageFactors.Config saved = DamageFactors.Current;
        try {
            FieldNpc monster = NewNpc(Monster, friendly: 0);
            FieldPlayer player = NewPlayer(Player);
            DamageFactors.Current = new DamageFactors.Config(PlayerFactor: 4, NpcFactor: 5.6);
            Assert.That(DamageCalculator.CalculateDamage(monster, player, Hit(1f)).Item2, Is.EqualTo(619), "2 x 2.8 gives 619.8, truncated");

            FieldPlayer hitter = NewPlayer(Runeblader);
            FieldNpc target = NewNpc(Corrupted, friendly: 0);
            DamageFactors.Current = new DamageFactors.Config(PlayerFactor: 4, NpcFactor: 2.8);
            long normal = DamageCalculator.CalculateDamage(hitter, target, Hit(0.57f)).Item2;
            Assert.That(normal, Is.InRange(263, 341));
            DamageFactors.Current = new DamageFactors.Config(PlayerFactor: 8, NpcFactor: 2.8);
            long doubled = Enumerable.Range(0, 200).Select(_ => DamageCalculator.CalculateDamage(hitter, target, Hit(0.57f)).Item2).Min();
            Assert.That(doubled, Is.InRange(2 * 263 - 4, 2 * 341), "the player's factor doubled doubles a player's hit");
            Assert.That(DamageCalculator.CalculateDamage(monster, player, Hit(1f)).Item2, Is.EqualTo(309), "the monster's factor is its own");
        } finally {
            DamageFactors.Current = saved;
        }
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
        SetField(player, "<ObjectId>k__BackingField", 1);

        var equips = Bare<EquipManager>();
        SetField(equips, "Gear", new ConcurrentDictionary<EquipSlot, Item>());
        SetField(equips, "Outfit", new ConcurrentDictionary<EquipSlot, Item>());
        var items = Bare<ItemManager>();
        SetField(items, "Equips", equips);
        session.Item = items;
        session.Stats = Stats(player, stats);
        session.Buffs = new BuffManager(player);
        return player;
    }

    private static FieldNpc NewNpc(Dictionary<BasicAttribute, long> stats, int friendly) {
        var npc = Bare<FieldNpc>();
        NpcMetadata metadata = Bare<NpcMetadata>() with { Basic = Bare<NpcMetadataBasic>() with { Friendly = friendly, Class = 1 } };
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "<ObjectId>k__BackingField", 2);
        SetField(npc, "<Stats>k__BackingField", Stats(npc, stats));
        SetField(npc, "<Buffs>k__BackingField", new BuffManager(npc));
        return npc;
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
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
