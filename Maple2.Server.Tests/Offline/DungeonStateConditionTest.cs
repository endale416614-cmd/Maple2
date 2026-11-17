using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-13e (BT-239): the script condition dungeon_state reads the room's state; 11 live maps check_state="Fail".</summary>
public class DungeonStateConditionTest {
    [Test]
    public void FailIsTrueOnceTheRunHasFailed() {
        (TriggerContext script, DungeonFieldManager field) = Build();
        Assert.That(script.DungeonState("Fail"), Is.False);
        field.Fail(); // as dungeon_fail and the room clock end a run
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.Fail));
        Assert.That(script.DungeonState("Fail"), Is.True);
    }

    [Test]
    public void AClearedRunIsNotFailedAndAFieldWithoutARunIsFalse() {
        (TriggerContext script, DungeonFieldManager field) = Build();
        field.DungeonRoomRecord.State = DungeonState.Clear;
        Assert.That(script.DungeonState("Fail"), Is.False);
        Assert.That(script.DungeonState("Clear"), Is.True);
        Assert.That(script.DungeonState(string.Empty), Is.False);

        var plain = Bare<TriggerContext>();
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", Bare<FieldManager>());
        SetField(plain, "owner", trigger);
        Assert.That(plain.DungeonState("Fail"), Is.False);
    }

    private static (TriggerContext, DungeonFieldManager) Build() {
        var field = Bare<DungeonFieldManager>();
        SetField(field, "<DungeonRoomRecord>k__BackingField", new DungeonRoomRecord(Bare<DungeonRoomMetadata>()));
        SetField(field, "<FieldTick>k__BackingField", 1000L);
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", field);
        var script = Bare<TriggerContext>();
        SetField(script, "owner", trigger);
        return (script, field);
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
