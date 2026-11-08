using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-10a: a Hidden Realm portal opens in each cycle of its timeEventData row and leads to a room of the row's room_random group,
/// with the room's nif, for the row's 29 s, as the GMS capture of Suffering Wasteland 2000148 shows (room 15, Kumbawanka's Vault 80000006).</summary>
[TestFixture]
public class HiddenRealmPortalTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Row 10000 of timeEventData.xml; 212 of the 213 rows of room group 1 have its times (12000, the test map's, opens every minute).
    private static readonly HiddenRealmPortalMetadata Row = new(10000, 80, new DateTime(2014, 1, 1, 12, 0, 0), new DateTime(2099, 12, 31, 12, 0, 0),
        TimeSpan.FromMinutes(7), new TimeSpan(0, 6, 30), TimeSpan.FromSeconds(29), [2000014], ["보너스맵"], RoomId: 1, RoomCount: 1);

    [Test]
    public void NextCycle_CountsWholeCyclesFromTheStart() {
        Assert.That(Row.NextCycle(new DateTime(2026, 11, 4, 9, 30, 0)), Is.EqualTo(new DateTime(2026, 11, 4, 9, 33, 0)), "the cycle under way began at 9:26");
        Assert.That(Row.NextCycle(new DateTime(2025, 12, 24, 12, 0, 0)), Is.EqualTo(new DateTime(2025, 12, 24, 12, 0, 0)), "a cycle that begins then counts");
        Assert.That(Row.NextCycle(new DateTime(2013, 6, 1)), Is.EqualTo(Row.StartTime), "before the start");
        Assert.That(Row.NextCycle(new DateTime(2099, 12, 31, 11, 55, 0)), Is.EqualTo(new DateTime(2099, 12, 31, 11, 55, 0)), "the last cycle");
        Assert.That(Row.NextCycle(new DateTime(2099, 12, 31, 11, 55, 1)), Is.Null, "the next would begin after the end");
        Assert.That((Row with { CycleTime = TimeSpan.Zero }).NextCycle(new DateTime(2026, 11, 4)), Is.Null, "no cycle");
    }

    [Test]
    public void SpawnHiddenRealmPortal_LeadsToTheRoomForTheRowsLife() {
        var field = Bare<FieldManager>();
        SetField(field, "fieldPortals", new ConcurrentDictionary<int, FieldPortal>());
        SetProperty(field, nameof(FieldManager.FieldTick), 1000L);
        var room = new RandomRoomTable.Room(15, 100, 80000006, 35000, "Eff_event_portal_A01");
        var spawn = new Ms2RegionSpawn(1001, false, new Vector3(-2400, -750, 1050), default);

        FieldPortal portal = field.SpawnHiddenRealmPortal(room, 7, spawn, Row.LifeTime);

        Assert.That((portal.Value.TargetMapId, portal.RoomId, portal.Model), Is.EqualTo((80000006, 7, "Eff_event_portal_A01")));
        Assert.That(portal.EndTick, Is.EqualTo(30000), "29 s after it opens");
        Assert.That((portal.Position, portal.Value.Dimension), Is.EqualTo((new Vector3(-2400, -750, 1050), new Vector3(200, 200, 250))));
        Assert.That((portal.Value.Type, portal.Value.ActionType, portal.Visible, portal.Enabled, portal.MinimapVisible),
            Is.EqualTo((PortalType.Event, PortalActionType.Interact, true, true, false)));
        Assert.That(field.GetPortals(), Is.EqualTo(new[] {portal}));
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

    private static void SetProperty(object target, string name, object? value) {
        target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
