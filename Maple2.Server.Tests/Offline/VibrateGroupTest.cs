using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-18: the AI's ShowVibrate and HideVibrateAll on the map's hide groups (the xblocks' MS2Vibrate hideGroupId). Temple of Immortals 02000037
/// has groups 1 to 9 of 3 or 4 rocks for Balrog's ShowVibrate 1 to 9. No capture has the two packets: the AI files are the only evidence.</summary>
[TestFixture]
public class VibrateGroupTest {
    private const float Block = FieldAccelerationStructure.BLOCK_SIZE;

    /// <summary>A cube in cell (x, 0, 0) with vibrate index x.</summary>
    private static FieldVibrateEntity Cube(int x, int hideGroupId) {
        var position = new Vector3(x * Block, 0, 0);
        return new FieldVibrateEntity(
            Id: FieldEntityId.FromString((x + 1).ToString("x32")),
            Position: position,
            Rotation: Vector3.Zero,
            Scale: 1,
            Bounds: new BoundingBox3(position - new Vector3(Block / 2, Block / 2, 0), position + new Vector3(Block / 2, Block / 2, Block)),
            BreakDefense: 85,
            BreakTick: 11000,
            MaterialTag: string.Empty,
            ResetTimer: 0,
            VibrateIndex: x,
            HideGroupId: hideGroupId);
    }

    /// <summary>Built like the server loads it: the cubes in their cells, serialized and read back.</summary>
    private static FieldAccelerationStructure Build(params FieldVibrateEntity[] cubes) {
        var grid = new Dictionary<Vector3S, List<FieldEntity>>();
        foreach (FieldVibrateEntity cube in cubes) {
            grid[cube.Position / Block] = [cube];
        }

        var structure = new FieldAccelerationStructure();
        structure.AddEntities(grid, new Vector3S(0, 0, 0), new Vector3S((short) (cubes.Length - 1), 0, 1), [], cubes.Length, new HashSet<FieldVibrateEntity>());

        var writer = new ByteWriter();
        structure.WriteTo(writer);
        var loaded = new FieldAccelerationStructure();
        loaded.ReadFrom(new ByteReader(writer.Buffer[..writer.Length]));
        return loaded;
    }

    /// <summary>An attack with brokenOffence 90, as VibrateRulesTest's.</summary>
    private static DamageRecord Damage() {
        var property = new SkillMetadataProperty(default, default, default, default, default, default, false, false, false, false, false, false, 0, 0, string.Empty);
        var skill = new SkillMetadata(50100869, 1, null, property, null!, null!);
        var damage = new SkillMetadataDamage(0, 0, 0, 0, false, 0, 0, false, null);
        var range = new SkillMetadataRange(SkillRegion.Box, 300, 100, 200, 0, 0, Vector3.Zero, Vector3.Zero, IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);
        var attack = new SkillMetadataAttack(string.Empty, 0, 0, 0, 0, 0, 90, [], null, range, null!, damage, [], []);
        return new DamageRecord(skill, attack);
    }

    /// <summary>A field never initialized (Broadcast sends nothing) with the structure and an empty cube state.</summary>
    private static FieldManager Field(FieldAccelerationStructure structure) {
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        typeof(FieldManager).GetField("<AccelerationStructure>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, structure);
        typeof(FieldManager).GetField("brokenVibrates", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, new BrokenVibrates());
        return field;
    }

    [Test]
    public void TheHideGroupRoundTripsAndACubeWithoutOneWritesNoMore() {
        var structure = new FieldAccelerationStructure();
        int Written(FieldVibrateEntity cube, out FieldEntity read) {
            var writer = new ByteWriter();
            structure.WriteTo(cube, writer);
            read = structure.ReadEntity(new ByteReader(writer.Buffer[..writer.Length]));
            return writer.Length;
        }

        int grouped = Written(Cube(0, 7), out FieldEntity withGroup);
        int none = Written(Cube(0, 0), out FieldEntity without);
        Assert.That(((FieldVibrateEntity) withGroup).HideGroupId, Is.EqualTo(7));
        Assert.That(((FieldVibrateEntity) without).HideGroupId, Is.EqualTo(0));
        Assert.That(grouped - none, Is.EqualTo(sizeof(int)), "only a cube of a hide group writes it, so map-data written before reads as before");

        FieldAccelerationStructure loaded = Build(Cube(0, 7), Cube(1, 0));
        Assert.That(loaded.VibrateEntities.ToArray().Select(cube => cube.HideGroupId), Is.EqualTo(new[] { 7, 0 }));
    }

    [Test]
    public void ShowVibrateNamesItsGroupAndHideVibrateAllEveryGroup() {
        FieldAccelerationStructure structure = Build(Cube(0, 1), Cube(1, 2), Cube(2, 1), Cube(3, 0));

        Assert.That(FieldManager.HideGroupCubes(structure, 1).Select(cube => cube.VibrateIndex), Is.EqualTo(new[] { 0, 2 }));
        Assert.That(FieldManager.HideGroupCubes(structure, 9), Is.Empty);
        Assert.That(FieldManager.HideGroupCubes(structure, 0).Select(cube => cube.VibrateIndex), Is.EqualTo(new[] { 0, 1, 2 }), "never a cube of no group");
    }

    [Test]
    public void AHiddenCubeNeitherVibratesNorBreaksUntilItIsShown() {
        FieldVibrateEntity rock = Cube(0, 3);
        FieldVibrateEntity floor = Cube(1, 0);
        FieldManager field = Field(Build(rock, floor));

        field.SetVibrateShown(0, false); // HideVibrateAll
        Assert.That(field.Vibrate(rock, Damage()), Is.False);
        Assert.That(field.Vibrate(floor, Damage()), Is.True, "a cube of no hide group still breaks");

        field.SetVibrateShown(3, true); // ShowVibrate groupID 3
        Assert.That(field.Vibrate(rock, Damage()), Is.True);
    }

    [Test]
    public void AShownOrHiddenCubeLosesItsBreakAndAPlayerEnteringLaterIsToldItsState() {
        var cubes = new BrokenVibrates();
        cubes.Add("a", Damage(), 77, long.MaxValue);
        cubes.Add("b", Damage(), 78, long.MaxValue);
        Assert.That(cubes.IsHidden("a"), Is.False, "a cube the AI never named is as the map has it");
        Assert.That(cubes.Visibility(), Is.Empty);

        cubes.SetShown("a", false);
        cubes.SetShown("b", true);
        Assert.That(cubes.IsHidden("a"), Is.True);
        Assert.That(cubes.IsHidden("b"), Is.False);
        Assert.That(cubes.InForceAt(0), Is.Empty, "showing or hiding a cube ends its break");
        Assert.That(cubes.Visibility(), Is.EquivalentTo(new[] { ("a", false), ("b", true) }));

        cubes.SetShown("a", true);
        Assert.That(cubes.IsHidden("a"), Is.False);
    }
}
