using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Offline;

/// <summary>BR-01: which breakable cubes an attack vibrates and breaks, and when a broken cube is back.</summary>
[TestFixture]
public class VibrateRulesTest {
    private const float Block = FieldAccelerationStructure.BLOCK_SIZE;

    private static readonly FieldEntityId NoId = new(0, 0, string.Empty);

    /// <summary>A cube whose position is its bottom centre; Bounds follow the Z turn only.</summary>
    private static FieldVibrateEntity Cube(Vector3 position, Vector3 rotation = default, int defense = 90, int breakTick = 2000, int resetTimer = 0, int index = 0) {
        float radians = rotation.Z * MathF.PI / 180;
        float half = Block / 2 * (MathF.Abs(MathF.Cos(radians)) + MathF.Abs(MathF.Sin(radians)));

        return new FieldVibrateEntity(
            Id: NoId,
            Position: position,
            Rotation: rotation,
            Scale: 1,
            Bounds: new BoundingBox3(position - new Vector3(half, half, 0), position + new Vector3(half, half, Block)),
            BreakDefense: defense,
            BreakTick: breakTick,
            MaterialTag: string.Empty,
            ResetTimer: resetTimer,
            VibrateIndex: index);
    }

    private static SkillMetadataRange Region(SkillRegion type, float distance, float height, float width, float endWidth, Vector3 add, Vector3 offset) {
        return new SkillMetadataRange(type, distance, height, width, endWidth, 0, add, offset, IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);
    }

    /// <summary>An attack record with its Attack-packet fields set; only the skill's property and the attack's damage are real.</summary>
    private static DamageRecord Damage() {
        var property = new SkillMetadataProperty(default, default, default, default, default, default, false, false, false, false, false, false, 0, 0, string.Empty);
        var skill = new SkillMetadata(50100869, 1, null, property, null!, null!);
        var damage = new SkillMetadataDamage(0, 0, 0, 0, false, 0, 0, false, null);
        var attack = new SkillMetadataAttack(string.Empty, 0, 0, 0, 0, 0, 90, [], null, Region(SkillRegion.Box, 300, 100, 200, 0, Vector3.Zero, Vector3.Zero), null!, damage, [], []);

        return new DamageRecord(skill, attack) {
            SkillUid = 5,
            SkillId = 50100869,
            Level = 1,
            MotionPoint = 2,
            AttackPoint = 3,
            Position = new Vector3(1, 2, 3),
        };
    }

    /// <summary>Built like the server loads it: aligned cubes in their cells, the others in the tree list, serialized and read back.</summary>
    private static FieldAccelerationStructure Build(List<FieldEntity> unaligned, params FieldEntity[] aligned) {
        var grid = new Dictionary<Vector3S, List<FieldEntity>>();
        foreach (FieldEntity entity in aligned) {
            Vector3S cell = entity.Position / Block;
            if (!grid.TryGetValue(cell, out List<FieldEntity>? entities)) {
                grid[cell] = entities = [];
            }
            entities.Add(entity);
        }

        var minIndex = new Vector3S(grid.Keys.Min(cell => cell.X), grid.Keys.Min(cell => cell.Y), grid.Keys.Min(cell => cell.Z));
        var maxIndex = new Vector3S(grid.Keys.Max(cell => cell.X), grid.Keys.Max(cell => cell.Y), (short) (grid.Keys.Max(cell => cell.Z) + 1));

        var structure = new FieldAccelerationStructure();
        structure.AddEntities(grid, minIndex, maxIndex, unaligned, aligned.Concat(unaligned).Count(entity => entity is FieldVibrateEntity), new HashSet<FieldVibrateEntity>());

        var writer = new ByteWriter();
        structure.WriteTo(writer);
        var loaded = new FieldAccelerationStructure();
        loaded.ReadFrom(new ByteReader(writer.Buffer[..writer.Length]));

        return loaded;
    }

    private static List<Vector3> CubePositions(FieldAccelerationStructure structure, Prism prism) {
        return FieldManager.CubesIn(structure, [prism]).Select(cube => cube.Position).ToList();
    }

    [Test]
    public void BreaksNeedsAnOffenceAboveTheDefence() {
        FieldVibrateEntity cube = Cube(Vector3.Zero, defense: 90);
        Assert.That(FieldManager.Breaks(cube, 91), Is.True);
        Assert.That(FieldManager.Breaks(cube, 90), Is.False);
        Assert.That(FieldManager.Breaks(Cube(Vector3.Zero, defense: 1, breakTick: 0), 99), Is.False);
    }

    [Test]
    public void ABrokenCubeIsBackAfterAtLeastThreeSecondsAndTheResetTimer() {
        // 3000 or the brokenTick, 500 for the cube's rise, and the reset timer.
        Assert.That(FieldManager.BrokenUntil(1000, Cube(Vector3.Zero, breakTick: 2000, resetTimer: 600)), Is.EqualTo(5100));
        Assert.That(FieldManager.BrokenUntil(1000, Cube(Vector3.Zero, breakTick: 8000, resetTimer: 600)), Is.EqualTo(10100));
        Assert.That(FieldManager.BrokenUntil(0, Cube(Vector3.Zero, breakTick: 2000)), Is.EqualTo(3500));
        Assert.That(FieldManager.BrokenUntil(0, Cube(Vector3.Zero, breakTick: 8000)), Is.EqualTo(8500));
    }

    [Test]
    public void BoxReachesBackByRangeAddYAndDownByRangeAddZ() {
        SkillMetadataRange box = Region(SkillRegion.Box, 300, 100, 200, 0, new Vector3(0, 50, 40), Vector3.Zero);
        Prism cubes = box.GetCubePrism(Vector3.Zero, 0);
        Prism targets = box.GetPrism(Vector3.Zero, 0);

        Assert.That(cubes.Contains(new Vector3(0, 40, 50)), Is.True);
        Assert.That(cubes.Contains(new Vector3(0, -100, -30)), Is.True);
        Assert.That(cubes.Contains(new Vector3(0, -320, 50)), Is.False);
        Assert.That(cubes.Contains(new Vector3(0, -100, 120)), Is.False);
        Assert.That(targets.Contains(new Vector3(0, -320, 50)), Is.True);
        Assert.That(targets.Contains(new Vector3(0, -100, 120)), Is.True);
    }

    [Test]
    public void FrustumIsAsWideAsItsWidthAtItsBack() {
        SkillMetadataRange frustum = Region(SkillRegion.Frustum, 300, 100, 200, 400, new Vector3(0, 50, 0), Vector3.Zero);
        Prism cubes = frustum.GetCubePrism(Vector3.Zero, 0);

        Assert.That(cubes.Contains(new Vector3(99, 49, 50)), Is.True);
        Assert.That(cubes.Contains(new Vector3(102, 49, 50)), Is.False);
    }

    [Test]
    public void HoleCylinderDoesNotVibrateACubeInItsHole() {
        SkillMetadataRange ring = Region(SkillRegion.HoleCylinder, 0, 100, 200, 400, Vector3.Zero, Vector3.Zero);
        Prism cubes = ring.GetCubePrism(Vector3.Zero, 0);

        Assert.That(FieldManager.Vibrates(cubes, Cube(Vector3.Zero)), Is.False);
        Assert.That(FieldManager.Vibrates(cubes, Cube(new Vector3(300, 0, 0))), Is.True);
    }

    [Test]
    public void ARangeOfTypeNoneVibratesNoCube() {
        SkillMetadataRange none = Region(SkillRegion.None, 3000, 3000, 2000, 0, Vector3.Zero, Vector3.Zero);
        FieldVibrateEntity cube = Cube(Vector3.Zero);
        bool vibrates = true;

        Assert.DoesNotThrow(() => vibrates = FieldManager.Vibrates(none.GetCubePrism(Vector3.Zero, 0), cube));
        Assert.That(vibrates, Is.False);
        Assert.That(CubePositions(Build([], cube), none.GetCubePrism(Vector3.Zero, 0)), Is.Empty);
    }

    [Test]
    public void AnUnturnedCubeFootprintIsItsBounds() {
        FieldVibrateEntity cube = Cube(new Vector3(300, -150, 0));
        Prism footprint = FieldManager.CubeFootprint(cube);

        (float minX, float maxX) = footprint.Polygon.AxisProjection(Vector2.UnitX);
        (float minY, float maxY) = footprint.Polygon.AxisProjection(Vector2.UnitY);
        Assert.That(minX, Is.EqualTo(cube.Bounds.Min.X).Within(0.01f));
        Assert.That(maxX, Is.EqualTo(cube.Bounds.Max.X).Within(0.01f));
        Assert.That(minY, Is.EqualTo(cube.Bounds.Min.Y).Within(0.01f));
        Assert.That(maxY, Is.EqualTo(cube.Bounds.Max.Y).Within(0.01f));
        Assert.That(footprint.Height.Min, Is.EqualTo(cube.Bounds.Min.Z).Within(0.01f));
        Assert.That(footprint.Height.Max, Is.EqualTo(cube.Bounds.Max.Z).Within(0.01f));
    }

    [Test]
    public void ATurnedCubeFootprintLeavesTheCornersOfItsBounds() {
        FieldVibrateEntity cube = Cube(Vector3.Zero, new Vector3(0, 0, 45));
        Prism footprint = FieldManager.CubeFootprint(cube);
        var corner = new Vector3(100, 100, 50);

        Assert.That(cube.Bounds.Contains(corner), Is.True);
        Assert.That(footprint.Contains(corner), Is.False);
        Assert.That(footprint.Contains(new Vector3(0, 100, 50)), Is.True);
        Assert.That(FieldManager.Vibrates(new Prism(new Circle(new Vector2(100, 100), 5), 0, Block), cube), Is.False);
        Assert.That(FieldManager.Vibrates(new Prism(new Circle(new Vector2(0, 100), 5), 0, Block), cube), Is.True);
    }

    [Test]
    public void TheFootprintTurnsCounterClockwise() {
        // Turned 30 degrees, the square's corner (75, -75) lands at (102.4, -27.4).
        Prism footprint = FieldManager.CubeFootprint(Cube(Vector3.Zero, new Vector3(0, 0, 30)));

        Assert.That(footprint.Contains(new Vector3(100, -27, 50)), Is.True);
        Assert.That(footprint.Contains(new Vector3(100, 27, 50)), Is.False);
    }

    [Test]
    public void ACubeTurnedOnXKeepsItsBounds() {
        FieldVibrateEntity cube = Cube(Vector3.Zero, new Vector3(180, 0, 45));

        Assert.That(FieldManager.CubeFootprint(cube).Contains(new Vector3(100, 100, 50)), Is.True);
    }

    [Test]
    public void ACubeBarelyOffTheVerticalIsStillTurnedOnZOnly() {
        FieldVibrateEntity cube = Cube(Vector3.Zero, new Vector3(0.2f, 0.2f, 45));

        Assert.That(FieldManager.CubeFootprint(cube).Contains(new Vector3(100, 100, 50)), Is.False);
    }

    [Test]
    public void ABreakIsInForceUntilItsEnd() {
        DamageRecord record = Damage();
        var broken = new BrokenVibrates();
        broken.Add("a", record, 77, 5000);
        broken.Add("b", record, 78, 9000);

        Assert.That(broken.InForce("a", 4999), Is.True);
        Assert.That(broken.InForce("a", 5001), Is.False);
        Assert.That(broken.InForce("c", 0), Is.False);
        Assert.That(broken.InForceAt(4999).Select(entry => (entry.Id, entry.Tick)), Is.EquivalentTo(new[] { ("a", 77), ("b", 78) }));
        Assert.That(broken.InForceAt(6000).Select(entry => (entry.Id, entry.Tick)), Is.EquivalentTo(new[] { ("b", 78) }));
        Assert.That(broken.InForceAt(10000), Is.Empty);
    }

    [Test]
    public void AStoredBreakKeepsWhatTheAttackPacketWritesAndNoTargets() {
        DamageRecord live = Damage();
        live.Targets.TryAdd(1, new DamageRecordTarget(null!));
        var broken = new BrokenVibrates();
        broken.Add("a", live, 77, 5000);

        DamageRecord stored = broken.InForceAt(0).Single().Record;
        Assert.That(stored, Is.Not.SameAs(live));
        Assert.That(stored.Targets, Is.Empty);
        Assert.That(stored.SkillUid, Is.EqualTo(5L));
        Assert.That(stored.SkillId, Is.EqualTo(50100869));
        Assert.That(stored.Level, Is.EqualTo((short) 1));
        Assert.That(stored.MotionPoint, Is.EqualTo((byte) 2));
        Assert.That(stored.AttackPoint, Is.EqualTo((byte) 3));
        Assert.That(stored.Position, Is.EqualTo(new Vector3(1, 2, 3)));
    }

    [Test]
    public void ABoxWhoseBaseLiesOnACubesTopVibratesIt() {
        FieldVibrateEntity cube = Cube(Vector3.Zero);
        FieldAccelerationStructure structure = Build([], cube);
        SkillMetadataRange box = Region(SkillRegion.Box, 300, 100, 200, 0, Vector3.Zero, Vector3.Zero);

        Prism prism = box.GetCubePrism(new Vector3(0, 0, Block), 0);
        Assert.That(CubePositions(structure, prism), Is.EqualTo(new[] { cube.Position }));
        Assert.That(FieldManager.CubesIn(structure, [prism, prism]), Has.Count.EqualTo(1));
    }

    [Test]
    public void UnseenThreatBlastReachesItsOwnGateOnly() {
        // Root of Darkness 52000076: 70000068 L1 at the bottom stem of gate 1; the top stem of the next gate is 600 away but 650 lower.
        FieldVibrateEntity stem = Cube(new Vector3(3600, -1500, 5250), index: 0);
        FieldVibrateEntity nextGate = Cube(new Vector3(3000, -1500, 4500), index: 1);
        FieldAccelerationStructure structure = Build([], stem, nextGate);
        SkillMetadataRange blast = Region(SkillRegion.Cylinder, 900, 950, 0, 0, Vector3.Zero, new Vector3(0, 0, -50));

        Assert.That(CubePositions(structure, blast.GetCubePrism(new Vector3(3600, -1500, 5100), 0)), Is.EqualTo(new[] { stem.Position }));
    }

    [Test]
    public void PamokansJumpOpensTheWayDownAndLeavesThePillars() {
        // Ignicore 2000317: 50100869 L1 at Pamokan (23100041), facing his FacePos (-1650, -3600).
        FieldVibrateEntity strip = Cube(new Vector3(150, -1050, 4200), index: 0);
        FieldVibrateEntity far = Cube(new Vector3(-1522, -3387, 4200), index: 1);
        FieldVibrateEntity pillar = Cube(new Vector3(-1500, -3000, 2100), index: 2);
        FieldAccelerationStructure structure = Build([far], strip, pillar);
        SkillMetadataRange jump = Region(SkillRegion.Box, 3000, 3000, 2000, 0, new Vector3(0, 0, 120), new Vector3(0, 350, 0));
        var pamokan = new Vector3(150, -600, 4350);

        List<Vector3> facing = CubePositions(structure, jump.GetCubePrism(pamokan, -30.96f));
        Assert.That(facing, Is.EquivalentTo(new[] { strip.Position, far.Position }));
        Assert.That(CubePositions(structure, jump.GetCubePrism(pamokan, 0)), Is.EqualTo(new[] { strip.Position }));
    }
}
