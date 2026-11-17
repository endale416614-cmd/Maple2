using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Offline;

/// <summary>GU-01 (BT-230): an exception in one update of the field loop is logged once and counted; the other entities, the next
/// tick and the field thread go on. Before, the first exception ended the thread and with it the Game server.</summary>
[TestFixture]
public class FieldUpdateGuardTest {
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private sealed class Lines : ILogEventSink {
        public readonly List<string> Items = [];
        public void Emit(LogEvent logEvent) => Items.Add(logEvent.RenderMessage());
    }

    private sealed class Entity(FieldManager field, int objectId, bool throws) : FieldEntity<int>(field, objectId, 0) {
        public int Updates;

        public override void Update(long tickCount) {
            Updates++;
            if (throws) {
                throw new InvalidOperationException("test");
            }
        }
    }

    /// <summary>A field never initialized: no thread, no players. Only what the guard touches is set.</summary>
    private static FieldManager Field(out Lines lines) {
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        lines = new Lines();
        Set(field, "logger", new LoggerConfiguration().WriteTo.Sink(lines).CreateLogger());
        Set(field, "updateFaults", new Dictionary<int, int>());
        Set(field, "fieldSkills", new ConcurrentDictionary<int, FieldSkill>());
        Set(field, "cubeSkills", new ConcurrentDictionary<int, FieldSkill>());
        return field;
    }

    private static void Set(FieldManager field, string name, object value) => typeof(FieldManager).GetField(name, Private)!.SetValue(field, value);

    private static T Get<T>(FieldManager field, string name) => (T) typeof(FieldManager).GetField(name, Private)!.GetValue(field)!;

    private static void UpdateEach(FieldManager field, Entity[] entities, int dropAfter) {
        MethodInfo method = typeof(FieldManager).GetMethod("UpdateEach", Private)!.MakeGenericMethod(typeof(Entity));
        method.Invoke(field, [entities, "npc", (Func<Entity, int>) (_ => 21000001), dropAfter]);
    }

    [Test]
    public void OneFaultyEntityDoesNotStopTheOthers() {
        FieldManager field = Field(out Lines lines);
        var bad = new Entity(field, 7, true);
        var good = new Entity(field, 8, false);
        for (int tick = 0; tick < 1000; tick++) {
            UpdateEach(field, [bad, good], 0);
        }

        Assert.That(good.Updates, Is.EqualTo(1000), "the entity after the faulty one is updated every tick");
        Assert.That(bad.Updates, Is.EqualTo(1000), "an NPC is tried again every tick");
        Assert.That(Get<int>(field, "updateFaultCount"), Is.EqualTo(1000));
        Assert.That(lines.Items, Has.Count.EqualTo(2), "an entity's first fault and its 1000th are logged");
        Assert.That(lines.Items[0], Does.Contain("21000001").And.Contain("object 7"));
    }

    [Test]
    public void ARegionIsRemovedAtItsThirdFault() {
        FieldManager field = Field(out _);
        var skills = Get<ConcurrentDictionary<int, FieldSkill>>(field, "fieldSkills");
        var cubes = Get<ConcurrentDictionary<int, FieldSkill>>(field, "cubeSkills");
        skills[7] = (FieldSkill) RuntimeHelpers.GetUninitializedObject(typeof(FieldSkill));
        cubes[9] = (FieldSkill) RuntimeHelpers.GetUninitializedObject(typeof(FieldSkill));
        Entity[] bad = [new Entity(field, 7, true), new Entity(field, 9, true)];
        UpdateEach(field, bad, 3);
        UpdateEach(field, bad, 3);
        Assert.That((skills.ContainsKey(7), cubes.ContainsKey(9)), Is.EqualTo((true, true)), "two faults");
        UpdateEach(field, bad, 3);
        Assert.That((skills.ContainsKey(7), cubes.ContainsKey(9)), Is.EqualTo((false, false)), "the third fault, a map's own region too");
    }

    // A field never initialized has no packet queue, so every Update throws: the loop must go on until it is cancelled.
    [Test]
    public void AnExceptionInUpdateDoesNotEndTheLoop() {
        FieldManager field = Field(out Lines lines);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Set(field, "cancel", cancel);
        typeof(FieldManager).GetMethod("UpdateLoop", Private)!.Invoke(field, null);

        Assert.That(Get<int>(field, "updateFaultCount"), Is.GreaterThan(1), "the loop went on after the first exception");
        Assert.That(lines.Items, Has.Count.EqualTo(1), "the field's first fault is logged, the rest counted");
        Assert.That(lines.Items[0], Does.Contain("object 0"));
    }
}
