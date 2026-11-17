using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Offline;

/// <summary>RN-01 (BT-238): a field's live regions are capped at 1000 for one caster and 2000 in all; at a cap the oldest goes.
/// Before, nothing capped the list, and a region can live for days.</summary>
[TestFixture]
public class RegionCapTest {
    private sealed class Lines : ILogEventSink {
        public readonly List<string> Items = [];
        public void Emit(LogEvent logEvent) => Items.Add(logEvent.RenderMessage());
    }

    private static readonly SkillMetadata Skill = new(70000001, 1, null, Bare<SkillMetadataProperty>(), null!, null!);

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void Set(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    /// <summary>A field never initialized (Broadcast sends nothing) with an empty region list and its own actor.</summary>
    private static FieldManager Field(out ConcurrentDictionary<int, FieldSkill> regions, out FieldActor map, out Lines lines) {
        var field = Bare<FieldManager>();
        regions = new ConcurrentDictionary<int, FieldSkill>();
        map = Bare<FieldActor>();
        lines = new Lines();
        Set(field, "fieldSkills", regions);
        Set(field, "FieldActor", map);
        Set(field, "logger", new LoggerConfiguration().WriteTo.Sink(lines).CreateLogger());
        return field;
    }

    private static void Add(FieldManager field, int objectId, IActor caster) {
        var region = Bare<FieldSkill>();
        Set(region, "<ObjectId>k__BackingField", objectId);
        Set(region, "<Value>k__BackingField", Skill);
        Set(region, "<Caster>k__BackingField", caster);
        typeof(FieldManager).GetMethod("AddRegion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, [region]);
    }

    [Test]
    public void ACastersOldestRegionGoesAtItsCap() {
        FieldManager field = Field(out ConcurrentDictionary<int, FieldSkill> regions, out _, out Lines lines);
        IActor boss = Bare<FieldNpc>(), other = Bare<FieldNpc>();
        Add(field, 1, other);
        for (int id = 2; id <= 1001; id++) {
            Add(field, id, boss);
        }
        Assert.That(regions.Count, Is.EqualTo(1001), "1000 of one caster and 1 of another");
        Assert.That(lines.Items, Is.Empty);

        Add(field, 1002, boss);
        Assert.That(regions.Count, Is.EqualTo(1001));
        Assert.That((regions.ContainsKey(1), regions.ContainsKey(2), regions.ContainsKey(1002)), Is.EqualTo((true, false, true)), "the caster's own oldest went");
        Add(field, 1003, boss);
        Assert.That(lines.Items, Has.Count.EqualTo(1), "one warning per field");
    }

    [Test]
    public void TheMapsOwnRegionsCountTowardTheFieldsCapOnly() {
        FieldManager field = Field(out ConcurrentDictionary<int, FieldSkill> regions, out FieldActor map, out _);
        for (int id = 1; id <= 1999; id++) {
            Add(field, id, map);
        }
        Add(field, 2000, Bare<FieldNpc>());
        Assert.That(regions.Count, Is.EqualTo(2000), "the caster's cap does not hold the map's own regions");

        Add(field, 2001, map);
        Assert.That(regions.Count, Is.EqualTo(2000));
        Assert.That((regions.ContainsKey(1), regions.ContainsKey(2000), regions.ContainsKey(2001)), Is.EqualTo((true, false, true)), "an actor's region goes before the map's own");
        Add(field, 2002, map);
        Assert.That((regions.ContainsKey(1), regions.ContainsKey(2002)), Is.EqualTo((false, true)), "with none of an actor left, the field's oldest");
    }
}
