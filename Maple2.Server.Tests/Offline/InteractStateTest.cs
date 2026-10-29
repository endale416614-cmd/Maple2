using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>IA-01: interact-object states as map scripts see them (BT-71), Katramus Gardens 02000293's glass doors.</summary>
public class InteractStateTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Plain = 10000504; // set01, parked in state 0 by the main script's start state
    private const int Answer = 10000523; // set01_answer, made reactable by the main script's arming state

    private ConcurrentDictionary<string, FieldInteract> interacts = null!;
    private TriggerContext context = null!;

    [SetUp]
    public void SetUp() {
        var field = Bare<FieldManager>();
        interacts = new ConcurrentDictionary<string, FieldInteract>();
        SetField(field, "fieldInteracts", interacts);
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", field);
        context = Bare<TriggerContext>();
        SetField(context, "owner", trigger);
        Add(field, Plain);
        Add(field, Answer);
    }

    private void Add(FieldManager field, int id) {
        // The glass doors' row: resetTime 3000000, reactTime 4000, hideTime 0.
        var metadata = new InteractObjectMetadata(id, InteractType.Mesh, 0, 0, 0, 0, 0, new InteractObjectMetadataItem(0, 0, 0, 0, 0),
            new InteractObjectMetadataTime(3000000, 4000, 0), new InteractObjectMetadataDrop(0, [], [], 0, 0), new InteractObjectMetadataEffect([], [], 0, 0), []);
        string entityId = id.ToString();
        interacts[entityId] = new FieldInteract(field, id, entityId, metadata, new InteractMeshObject(entityId, new Ms2InteractMesh(id, Vector3.Zero, Vector3.Zero)));
    }

    private FieldInteract Get(int id) => interacts[id.ToString()];

    [Test]
    public void UntouchedObject_BecomesReactableAtStart() {
        Get(Plain).Update(long.MaxValue);
        Assert.That(Get(Plain).State, Is.EqualTo(InteractState.Reactable));
    }

    [Test]
    public void ScriptNormal_WinsOverStartupTimer() {
        context.SetInteractObject([Plain, Answer], 0, false, false);
        context.SetInteractObject([Answer], 1, false, false);
        Get(Plain).Update(long.MaxValue);
        Get(Answer).Update(long.MaxValue);
        Assert.That(Get(Plain).State, Is.EqualTo(InteractState.Normal));
        Assert.That(Get(Answer).State, Is.EqualTo(InteractState.Reactable));
    }

    [Test]
    public void ScriptNormal_CancelsPendingReset() {
        context.SetInteractObject([Plain, Answer], 1, false, false);
        Assert.That(Get(Plain).React(), Is.True);
        Assert.That(Get(Answer).React(), Is.True);
        context.SetInteractObject([Plain], 0, false, false);
        Get(Plain).Update(long.MaxValue);
        Get(Answer).Update(long.MaxValue);
        Assert.That(Get(Plain).State, Is.EqualTo(InteractState.Normal));
        Assert.That(Get(Answer).State, Is.EqualTo(InteractState.Reactable), "without a script's state the reset still comes");
    }

    [Test]
    public void WaitForNormal_MatchesOnlyAUsedObject() {
        // No Update here: the start-up promotion would make Plain reactable, and the old rule would pass too.
        context.SetInteractObject([Plain, Answer], 0, false, false);
        context.SetInteractObject([Answer], 1, false, false);
        Assert.That(context.ObjectInteracted([Answer, Plain], 0), Is.False, "door01 must not open before a use");
        Assert.That(context.ObjectInteracted([Answer], 1), Is.True);
        Assert.That(Get(Answer).React(), Is.True);
        Assert.That(context.ObjectInteracted([Answer, Plain], 0), Is.True);
        Assert.That(context.ObjectInteracted([Answer], 2), Is.True, "a wait for 2 still sees a use (Tria Battlefield 52000068)");
        context.SetInteractObject([Answer], 0, false, false);
        Assert.That(context.ObjectInteracted([Answer], 0), Is.True, "a used object a script parks stays used");
        context.SetInteractObject([Answer], 1, false, false);
        Assert.That(context.ObjectInteracted([Answer], 0), Is.False, "made reactable again: not used");
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
