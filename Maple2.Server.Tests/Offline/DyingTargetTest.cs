using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-20b: a region skips a target an earlier hit of the same update took to 0 health (BT-68, C3). Golden: capture Port 58173, map
/// 52100012, 3 turns of 3: level 7 of 70000065 hits Alvanos and the player, never the corsairs level 6 took to 0; no region hit of the 227 GMS
/// captures lists a target already at 0 (2,159 kills).</summary>
[TestFixture]
public class DyingTargetTest {
    [Test]
    public void ACorsairAtZeroIsDyingUntilItsUpdate() {
        Assert.That(FieldSkill.Dying(Npc(0, dead: false)), Is.True); // Lobber 21421215 after level 6: 0 of 65169
    }

    [Test]
    public void ALivingNpcACorpseAndTheMapAreNot() {
        Assert.That(FieldSkill.Dying(Npc(65169, dead: false)), Is.False);
        Assert.That(FieldSkill.Dying(Npc(0, dead: true)), Is.False, "a corpse is dead already and stays a target, as before");
        Assert.That(FieldSkill.Dying(Bare<FieldActor>()), Is.False, "the map's caster has no health");
    }

    private static FieldNpc Npc(long health, bool dead) {
        FieldNpc npc = Bare<FieldNpc>();
        var stats = new StatsManager(Bare<FieldActor>());
        stats.Values[BasicAttribute.Health] = new Stat(65169, 65169, health);
        SetField(npc, "<Stats>k__BackingField", stats);
        SetField(npc, "<IsDead>k__BackingField", dead);
        return npc;
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
