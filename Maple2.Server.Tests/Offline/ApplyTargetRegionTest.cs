using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>SE-03: a map's ApplyTarget 7 region hits monsters, friendly NPCs and players in its area (BT-93). Golden: captures Port 58173 (70000065 level 7, map 52100012)
/// and 12-Palace Undercroft dungeon (70000051, map 2000252). 70000569 has no capture: its numbers are the data's.</summary>
[TestFixture]
public class ApplyTargetRegionTest {
    private static readonly SkillMetadataRange ScoreBox = Range(SkillRegion.Box, 450, 450, 450, Vector3.Zero); // 70000569
    private static readonly SkillMetadataRange Blast = Range(SkillRegion.Cylinder, 6000, 1500, 0, Vector3.Zero); // 70000065 level 7
    private static readonly SkillMetadataRange Flame = Range(SkillRegion.Box, 50, 600, 100, new Vector3(0, 50, 0)); // 70000051
    private static readonly Vector3 ScoreBoxAt = new(-7425, 3300, 5175);
    private static readonly Vector3 BlastAt = new(600, -150, 975);
    private static readonly Vector3 FlameAt = new(-10950, 4800, 2850);

    private static SkillMetadataRange Range(SkillRegion type, float distance, float height, float width, Vector3 add) =>
        new(type, distance, height, width, 0, 0, add, Vector3.Zero, IncludeCasterType.Exclude, ApplyTargetType.Player4, SkillTargetType.None);

    [Test]
    public void TheScoreBoxHitsADummyAtItsPoint() {
        var scene = new Scene();
        FieldNpc dummy = scene.Mob(ScoreBoxAt, 100, 150); // 29000320
        Assert.That(scene.MapTargets(ScoreBox, ScoreBoxAt), Is.EqualTo(new IActor[] { dummy }));
    }

    [Test]
    public void TheScoreBoxHitsAPlayerOnlyInsideIt() {
        var scene = new Scene();
        scene.Player(new Vector3(-7950, 2100, 2700)); // the map's player spawn
        Assert.That(scene.MapTargets(ScoreBox, ScoreBoxAt), Is.Empty);

        FieldPlayer onThePlatform = scene.Player(new Vector3(-7425, 3200, 5175));
        Assert.That(scene.MapTargets(ScoreBox, ScoreBoxAt), Is.EqualTo(new IActor[] { onThePlatform }));
    }

    [Test]
    public void TheBlastHitsTheFriendlyNpcThenThePlayer() {
        var scene = new Scene();
        FieldPlayer player = scene.Player(new Vector3(2101, 104, 1913));
        FieldNpc alvanos = scene.Npc(1, new Vector3(-525, 958, 749), 300, 450); // 21421217
        scene.Npc(2, new Vector3(4225, -1174, 749), 30, 150); // Celine: her top, 899, is under the blast's 975
        Assert.That(scene.MapTargets(Blast, BlastAt), Is.EqualTo(new IActor[] { alvanos, player }));
    }

    [Test]
    public void TheFlameHitsAMonsterInItsTile() {
        var scene = new Scene();
        FieldNpc infiltrator = scene.Mob(new Vector3(-10919, 4815, 2849), 60, 150); // 24000301
        scene.Mob(new Vector3(-10919, 5000, 2849), 60, 150);
        Assert.That(scene.MapTargets(Flame, FlameAt), Is.EqualTo(new IActor[] { infiltrator }));
    }

    [Test]
    public void AStoryNpcStaysOut() {
        var scene = new Scene();
        scene.Npc(2, ScoreBoxAt, 100, 150);
        Assert.That(scene.MapTargets(ScoreBox, ScoreBoxAt), Is.Empty);
    }

    [Test]
    public void FromAnItemOrAnNpcItHitsNoOne() {
        var scene = new Scene();
        scene.Mob(ScoreBoxAt, 100, 150);
        scene.Npc(1, ScoreBoxAt, 100, 150);
        scene.Player(ScoreBoxAt);
        Assert.That(scene.Targets(Bare<FieldNpc>(), ScoreBox, ScoreBoxAt), Is.Empty);
        Assert.That(scene.Targets(Bare<FieldPlayer>(), ScoreBox, ScoreBoxAt), Is.Empty);
    }

    /// <summary>A field with no thread, and actors with only what the target pool reads.</summary>
    private sealed class Scene {
        private readonly FieldManager field = Bare<FieldManager>();
        private int nextId;

        public Scene() {
            SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
            SetField(field, "<Npcs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
            SetField(field, "<Mobs>k__BackingField", new ConcurrentDictionary<int, FieldNpc>());
        }

        public IActor[] MapTargets(SkillMetadataRange range, Vector3 at) => Targets(Bare<FieldActor>(), range, at);

        public IActor[] Targets(IActor caster, SkillMetadataRange range, Vector3 at) =>
            field.GetTargets(caster, [range.GetPrism(at, 0, range.ApplyTarget)], range, 50).ToArray();

        public FieldPlayer Player(Vector3 position) {
            FieldPlayer player = Placed<FieldPlayer>(position);
            field.Players[player.ObjectId] = player;
            return player;
        }

        public FieldNpc Mob(Vector3 position, float radius, float height) {
            FieldNpc mob = NewNpc(0, position, radius, height);
            field.Mobs[mob.ObjectId] = mob;
            return mob;
        }

        public FieldNpc Npc(int friendly, Vector3 position, float radius, float height) {
            FieldNpc npc = NewNpc(friendly, position, radius, height);
            field.Npcs[npc.ObjectId] = npc;
            return npc;
        }

        private FieldNpc NewNpc(int friendly, Vector3 position, float radius, float height) {
            var basic = new NpcMetadataBasic(friendly, 0, 0, 0, 0, 0, 0, 1, 0, false, 0, 0, 0, 0, [], [], 0);
            var property = new NpcMetadataProperty([], new NpcMetadataCapsule(radius, height), null);
            var metadata = new NpcMetadata(1, "", "", null!, null!, basic, null!, null!, property, null!, null!, null!, null, null!);
            FieldNpc npc = Placed<FieldNpc>(position);
            SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
            return npc;
        }

        private T Placed<T>(Vector3 position) where T : class, IActor {
            T actor = Bare<T>();
            SetField(actor, "<ObjectId>k__BackingField", ++nextId);
            SetField(actor, "<Transform>k__BackingField", new Transform { Position = position });
            return actor;
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
