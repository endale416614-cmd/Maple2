using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>SE-05: a trigger skill placed with count 0 repeats every Interval of its map placement until set_skill turns it off. Golden: the captures
/// (70000500 every 1 s in Bastei Peak 2000313, 70000572 and 70000573 every 2 s in Terminus of Time 2020120, Interval 0 every 0.1 s for 70000051,
/// 70000055, 70000065 level 3, 70000069 and 70000072) and the client's xblocks (MS2TriggerSkill Interval).</summary>
[TestFixture]
public class TriggerRepeatTest {
    private static readonly Vector3 At = new(900, -3000, 13050); // trigger 301 of 02000313

    [TestCase(0, int.MaxValue)] // 70000500, 70000569, 70000051's 5 tiles, Root of Darkness 70000068
    [TestCase(1, 1)] // 70000065 level 3's count 1 placements in 02000335, 70000051's 452 other tiles
    public void TriggerFireCount(int count, int expected) {
        Assert.That(FieldManager.TriggerFireCount(count), Is.EqualTo(expected));
    }

    [TestCase(1000, 1000)] // 70000500, 70000501
    [TestCase(2000, 2000)] // 70000572, 70000573
    [TestCase(5000, 5000)] // 70000022: one hit in its 3 s life
    [TestCase(150, 150)] // 70000567
    [TestCase(0, 100)] // 0.1 s in the captures
    [TestCase(10, 100)] // Electric AoE 70000042: no capture, the same floor
    public void TriggerInterval(int interval, int expected) {
        Assert.That(FieldManager.TriggerInterval(interval), Is.EqualTo(expected));
    }

    [Test]
    public void ACountZeroRegionRepeatsAtItsIntervalUntilTurnedOff() {
        var scene = new Scene();
        FieldSkill region = scene.Place(70000500, 0, 1000);
        Assert.That(region.NextTick, Is.EqualTo(1000), "the first cast comes at the add, as captured (NB-20b)");
        Assert.That(scene.Tick(region, 1000), Is.EqualTo(2000));
        Assert.That(scene.Tick(region, 1999), Is.EqualTo(2000));
        Assert.That(scene.Tick(region, 2000), Is.EqualTo(3000));
        Assert.That(scene.Tick(region, 3010), Is.EqualTo(4000));
        Assert.That(scene.Regions, Is.EqualTo(new[] { region }));

        scene.Field.RemoveSkillByTriggerId(301);
        Assert.That(scene.Regions, Is.Empty);
    }

    [Test]
    public void IntervalZeroRepeatsEveryTenthOfASecond() {
        var scene = new Scene();
        FieldSkill region = scene.Place(70000051, 0, 0);
        Assert.That(new[] { scene.Tick(region, 1000), scene.Tick(region, 1100), scene.Tick(region, 1200) }, Is.EqualTo(new long[] { 1100, 1200, 1300 }));
    }

    [Test]
    public void CountOneCastsOnce() {
        var scene = new Scene();
        FieldSkill region = scene.Place(70000500, 1, 1000);
        Assert.That(scene.Tick(region, 1150), Is.EqualTo(long.MaxValue));
        scene.Tick(region, 2150);
        Assert.That(scene.Regions, Is.Empty);
    }

    [Test]
    public void TurnedOnAgainWhileOnItIsReplaced() {
        var scene = new Scene();
        FieldSkill first = scene.Place(70000500, 0, 1000);
        Assert.That(scene.Place(70000500, 0, 1000), Is.Not.SameAs(first), "the captures: removed and added at once, not stacked");
    }

    /// <summary>A field with no thread at FieldTick 1000, whose skills have no motion: a region casts and hits no one.</summary>
    private sealed class Scene {
        public readonly FieldManager Field = Bare<FieldManager>();

        public Scene() {
            SetField(Field, "fieldSkills", new ConcurrentDictionary<int, FieldSkill>());
            SetField(Field, "<SkillMetadata>k__BackingField", new Skills());
            SetField(Field, "<FieldTick>k__BackingField", 1000L);
        }

        public FieldSkill[] Regions => Field.DebugFieldSkills.ToArray();

        // As set_skill places it (TriggerContext.SetSkill).
        public FieldSkill Place(int skillId, int count, int interval) {
            var placement = new Ms2TriggerSkill(skillId, 1, At, Vector3.Zero, 301, true, count, interval);
            Field.AddSkill(placement, placement.Position, placement.Rotation, placement.TriggerId);
            return Regions.Single();
        }

        public long Tick(FieldSkill region, long tick) {
            SetField(Field, "<FieldTick>k__BackingField", tick);
            region.Update(tick);
            return region.NextTick;
        }
    }

    private sealed class Skills : SkillMetadataStorage {
        public Skills() : base(null!) {
            foreach (int id in new[] { 70000051, 70000500 }) {
                Cache.AddReplace((id, 1), new SkillMetadata(id, 1, null, null!, null!, new SkillMetadataLevel(null!, null, null, null!, null!, null, null!, [], [])));
            }
        }
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
