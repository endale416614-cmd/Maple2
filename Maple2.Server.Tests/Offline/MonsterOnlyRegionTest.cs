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

/// <summary>SE-04: a ship turn's first blast (70000065 level 6) hits the monsters and not the player; the second (level 7) hits the friendly NPC and the player (BT-195).
/// Golden: capture Port 58173, map 52100012, the first turn: 7 corsairs go to 0 health and the player's 2352 does not move, then Alvanos and the player take 250.</summary>
[TestFixture]
public class MonsterOnlyRegionTest {
    private static readonly Vector3 BlastAt = new(600, -150, 975);
    private static readonly Vector3 PlayerAt = new(2101, 104, 1913);
    private static readonly Vector3 AnglerAt = new(139, -251, 1499); // 21421213
    private static readonly Vector3 LobberAt = new(217, -282, 1499); // 21421215
    private static readonly Vector3 AlvanosAt = new(-525, 958, 749); // 21421217
    private static readonly Vector3 CelineAt = new(4225, -1174, 749); // 21421219: her top, 899, is under the blast's 975

    [TestCase(70000096, 1, true)] // Soul Binder intro, 63000028 and 63000029
    [TestCase(70000065, 6, true)] // the ship turn's first blast
    [TestCase(70000065, 7, false)] // the second: hits the player in the capture
    [TestCase(70000065, 2, false)] // breaks cubes
    [TestCase(70002001, 1, false)] // Blue Lapenta's wipe: 100 % too, and no capture says monsters only
    [TestCase(70000500, 1, false)] // hits the player in Bastei Peak's capture
    public void TheRuleIsBySkillAndLevel(int id, short level, bool expected) {
        Assert.That(FieldSkill.MonsterOnly(id, level), Is.EqualTo(expected));
    }

    [Test]
    public void LevelSixHitsTheCorsairsNotThePlayer() {
        var scene = new Scene();
        FieldNpc angler = scene.Mob(AnglerAt);
        FieldNpc lobber = scene.Mob(LobberAt);
        scene.Player(PlayerAt);
        scene.Npc(1, AlvanosAt, 300, 450);
        scene.Npc(2, CelineAt, 30, 150);
        Assert.That(scene.Targets(70000065, 6, ApplyTargetType.RegionBuff), Is.EqualTo(new IActor[] { angler, lobber }));
    }

    [Test]
    public void LevelSevenStillHitsAlvanosThenThePlayer() {
        var scene = new Scene();
        FieldPlayer player = scene.Player(PlayerAt);
        FieldNpc alvanos = scene.Npc(1, AlvanosAt, 300, 450);
        scene.Npc(2, CelineAt, 30, 150);
        Assert.That(scene.Targets(70000065, 7, ApplyTargetType.Player4), Is.EqualTo(new IActor[] { alvanos, player }));
    }

    [Test]
    public void AnyOtherApplyTargetFiveRegionStillHitsThePlayer() {
        var scene = new Scene();
        scene.Mob(AnglerAt);
        FieldPlayer player = scene.Player(PlayerAt);
        Assert.That(scene.Targets(70002001, 1, ApplyTargetType.RegionBuff), Is.EqualTo(new IActor[] { player }));
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

        public IActor[] Targets(int id, short level, ApplyTargetType applyTarget) {
            var range = new SkillMetadataRange(SkillRegion.Cylinder, 6000, 1500, 0, 0, 0, Vector3.Zero, Vector3.Zero, IncludeCasterType.Exclude, applyTarget, SkillTargetType.None);
            var skill = new FieldSkill(field, 1, Bare<FieldActor>(), new SkillMetadata(id, level, null, null!, null!, null!), 150, 1, BlastAt);
            return skill.FindTargets([range.GetPrism(BlastAt, 0, applyTarget)], range, 50).ToArray();
        }

        public FieldPlayer Player(Vector3 position) {
            FieldPlayer player = Placed<FieldPlayer>(position);
            field.Players[player.ObjectId] = player;
            return player;
        }

        public FieldNpc Mob(Vector3 position) {
            FieldNpc mob = NewNpc(0, position, 30, 150);
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
