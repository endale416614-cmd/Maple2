using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>QP-01 (BT-153): a quest portal summoned at an NPC always faces -45 and counts as started 0.6 s after the summon, as in the GMS captures.</summary>
[TestFixture]
public class QuestPortalTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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

    private static void SetProperty(object target, string name, object? value) {
        target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    [Test]
    public void SpawnPortal_MatchesTheOriginalPacket() {
        var field = Bare<FieldManager>();
        SetField(field, "fieldPortals", new ConcurrentDictionary<int, FieldPortal>());
        var storage = Bare<ServerTableMetadataStorage>();
        SetField(storage, "constantsTable", new Lazy<ConstantsTable>(() => new ConstantsTable(
            QuestPortalKeepTime: 300, QuestPortalKeepNif: "Eff_Com_Portal_E_Quest", QuestPortalDimensionY: 50, QuestPortalDimensionZ: 350,
            QuestPortalSummonTime: 600, QuestPortalDistanceFromNpc: 200)));
        SetProperty(field, nameof(FieldManager.ServerTableMetadata), storage);
        SetProperty(field, nameof(FieldManager.FieldTick), 1000L);

        // An NPC at the origin facing 270: the portal stands 200 along that rotation, but faces -45 whatever the NPC does.
        var npc = Bare<FieldNpc>();
        SetField(npc, "<Transform>k__BackingField", new Transform());
        npc.Rotation = new Vector3(0, 0, 270);

        FieldPortal portal = field.SpawnPortal(new QuestSummonPortal(2000128, 1), npc, Bare<FieldPlayer>());

        Assert.That(portal.Rotation.X, Is.EqualTo(0f).Within(0.01f));
        Assert.That(portal.Rotation.Y, Is.EqualTo(0f).Within(0.01f));
        Assert.That(portal.Rotation.Z, Is.EqualTo(-45f).Within(0.01f), "not the NPC's rotation");
        Assert.That(portal.Position.X, Is.EqualTo(-200f).Within(0.01f));
        Assert.That(portal.Position.Y, Is.EqualTo(0f).Within(0.01f));
        Assert.That(portal.Position.Z, Is.EqualTo(0f).Within(0.01f));
        Assert.That(portal.Value.Dimension.X, Is.EqualTo(200f).Within(0.01f));
        Assert.That(portal.Value.Dimension.Y, Is.EqualTo(50f).Within(0.01f));
        Assert.That(portal.Value.Dimension.Z, Is.EqualTo(350f).Within(0.01f));
        Assert.That(portal.StartTick, Is.EqualTo(1600), "0.6 s after the summon");
        Assert.That(portal.EndTick, Is.EqualTo(301000), "300 s after the summon");
    }
}
