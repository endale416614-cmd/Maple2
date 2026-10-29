using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Offline;

/// <summary>A field handed to a player who is loading into it is not disposed as empty (FD-01, BT-130).</summary>
public class FieldDisposeTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int SylvanWoodsTrail = 2000017;

    [Test]
    public void Get_RestartsTheEmptyTimer() {
        // The player left the map 10 min 10 s ago and comes back: the dispose loop's next pass would dispose the field during the load.
        var field = Bare<FieldManager>();
        SetField(field, "fieldEmptySince", DateTime.UtcNow - TimeSpan.FromSeconds(610));
        var tables = Bare<ServerTableMetadataStorage>();
        SetField(tables, "instanceFieldTable", new Lazy<InstanceFieldTable>(new InstanceFieldTable(new Dictionary<int, InstanceFieldMetadata>())));
        var factory = Bare<FieldManager.Factory>(); // no dispose loop thread
        SetField(factory, "<ServerTableMetadata>k__BackingField", tables);
        SetField(factory, "mapLocks", new ConcurrentDictionary<int, SemaphoreSlim>());
        SetField(factory, "fields", new ConcurrentDictionary<int, ConcurrentDictionary<int, FieldManager>> {
            [SylvanWoodsTrail] = new ConcurrentDictionary<int, FieldManager> { [field.RoomId] = field },
        });

        Assert.That(factory.Get(SylvanWoodsTrail), Is.SameAs(field));
        Assert.That(GetField(field, "fieldEmptySince"), Is.Null, "the field still counts as empty for 10 minutes while the player loads");
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static FieldInfo FindField(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) => FindField(target, name).SetValue(target, value);

    private static object? GetField(object target, string name) => FindField(target, name).GetValue(target);
}
