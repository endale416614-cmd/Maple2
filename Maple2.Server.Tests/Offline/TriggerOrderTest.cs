using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools.Scheduler;
using NUnit.Framework;
using Script = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>TG-02: a field's scripts all enter their new states before any of them checks a condition (BT-164).</summary>
public class TriggerOrderTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Run(Action action) : IAction {
        public void Execute(TriggerContext context) => action();
    }

    private sealed class When(Func<bool> check, string next) : ICondition {
        public string Name => "when";
        public string? NextState { get; set; } = next;
        public LinkedList<IAction> Actions { get; set; } = [];
        public bool Evaluate(TriggerContext context) => check();
    }

#pragma warning disable NUnit1032 // The bare FieldManager owns nothing to dispose, and its Dispose would throw.
    private FieldManager field = null!;
#pragma warning restore NUnit1032
    private ConcurrentDictionary<string, FieldTrigger> triggers = null!;

    [SetUp]
    public void SetUp() {
        field = Bare<FieldManager>();
        SetField(field, "Metadata", Bare<MapMetadata>());
        triggers = new ConcurrentDictionary<string, FieldTrigger>();
        SetField(field, "fieldTriggers", triggers);
    }

    private FieldTrigger Add(string name, params Script.State[] states) {
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", field);
        SetField(trigger, "<Value>k__BackingField", new TriggerModel(0, name, default, default));
        var context = Bare<TriggerContext>();
        SetField(context, "Events", new EventQueue(Serilog.Log.Logger));
        SetField(trigger, "Context", context);
        SetField(trigger, "nextState", new TriggerState(new Script(states.ToList()), context));
        triggers[name] = trigger;
        return trigger;
    }

    private static Script.State State(string name, Script.State.OnEnter? enter, params ICondition[] conditions) {
        return new Script.State(name, new LinkedList<ICondition>(conditions), enter, null);
    }

    private static string? Next(FieldTrigger trigger) {
        object? next = typeof(FieldTrigger).GetField("nextState", Any)!.GetValue(trigger);
        return ((TriggerState?) next)?.Name;
    }

    // Both orders, so that one of them runs the waiter first, as scene02 before bossSpawn in 02010055.
    [TestCase("a", "b")]
    [TestCase("b", "a")]
    public void AWaiterSeesWhatASiblingDoesOnEntering(string waiterName, string spawnerName) {
        bool spawned = false;
        FieldTrigger waiter = Add(waiterName, State("wait", null, new When(() => !spawned, "fired")), State("fired", null));
        Add(spawnerName, State("spawn", new Script.State.OnEnter(new LinkedList<IAction>([new Run(() => spawned = true)]), null)));

        field.UpdateTriggers();

        Assert.That(Next(waiter), Is.Null);
    }

    [Test]
    public void ATrueConditionStillMovesOnInTheSameStep() {
        FieldTrigger trigger = Add("a", State("wait", null, new When(() => true, "fired")), State("fired", null));

        field.UpdateTriggers();

        Assert.That(Next(trigger), Is.EqualTo("fired"));
    }

    [Test]
    public void EnteringWithANextStateChecksNoCondition() {
        int checks = 0;
        FieldTrigger trigger = Add("a",
            State("start", new Script.State.OnEnter(null, "after"), new When(() => { checks++; return false; }, "fired")),
            State("after", null),
            State("fired", null));

        field.UpdateTriggers();

        Assert.That(Next(trigger), Is.EqualTo("after"));
        Assert.That(checks, Is.EqualTo(0));
    }

    [Test]
    public void BeforeItsNextTickAScriptChecksNothing() {
        int checks = 0;
        FieldTrigger trigger = Add("a", State("wait", null, new When(() => { checks++; return false; }, "fired")), State("fired", null));
        field.UpdateTriggers();
        Assert.That(checks, Is.EqualTo(1));

        SetField(trigger, "nextTick", long.MaxValue);
        field.UpdateTriggers();

        Assert.That(checks, Is.EqualTo(1));
    }

    [Test]
    public void ConditionsRunOncePerUpdate() {
        int checks = 0;
        FieldTrigger trigger = Add("a", State("wait", null, new When(() => { checks++; return false; }, "fired")), State("fired", null));

        field.UpdateTriggers();
        trigger.CheckConditions();

        Assert.That(checks, Is.EqualTo(1));
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

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
