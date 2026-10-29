extern alias ingest;

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ingest::Maple2.File.Ingest.Mapper;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.PacketLib.Tools;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Offline;

/// <summary>IG-02: map-data keeps a vibrate entity's material tag and reset time, and makes spawn tiles on every cube floor a mob can stand on.</summary>
[TestFixture]
public class MapDataEntityTest {
    private const float Block = FieldAccelerationStructure.BLOCK_SIZE;
    private const float Half = FieldAccelerationStructure.HALF_BLOCK;

    private static readonly FieldEntityId NoId = new(0, 0, string.Empty);

    private static Vector3 Center(short x, short y, short z) => Block * new Vector3(x, y, z);

    private static BoundingBox3 CellBox(short x, short y, short z, float height = Block) {
        Vector3 center = Center(x, y, z);

        return new BoundingBox3(center - new Vector3(Half, Half, 0), center + new Vector3(Half, Half, height));
    }

    private static FieldBoxColliderEntity Box(short x, short y, short z) {
        return new FieldBoxColliderEntity(
            Id: NoId,
            Position: Center(x, y, z),
            Rotation: Vector3.Zero,
            Scale: 1,
            Bounds: CellBox(x, y, z),
            Size: new Vector3(Block, Block, Block),
            IsWhiteBox: false,
            IsFluid: false,
            MapAttribute: MapAttribute.none);
    }

    private static FieldMeshColliderEntity Mesh(short x, short y, short z, float height = Block) {
        return new FieldMeshColliderEntity(
            Id: NoId,
            Position: Center(x, y, z),
            Rotation: Vector3.Zero,
            Scale: 1,
            Bounds: CellBox(x, y, z, height),
            MeshLlid: 0,
            MapAttribute: MapAttribute.none);
    }

    private static FieldFluidEntity Fluid(short x, short y, short z) {
        return new FieldFluidEntity(
            Id: NoId,
            Position: Center(x, y, z),
            Rotation: Vector3.Zero,
            Scale: 1,
            LiquidType: LiquidType.water,
            Bounds: CellBox(x, y, z),
            MeshLlid: 0,
            IsShallow: false,
            IsSurface: true,
            MapAttribute: MapAttribute.none);
    }

    private static FieldVibrateEntity Vibrate(short x, short y, short z, float height = Block, FieldEntityId? id = null, int index = 0, string tag = "", int resetTimer = 0) {
        return new FieldVibrateEntity(
            Id: id ?? NoId,
            Position: Center(x, y, z),
            Rotation: Vector3.Zero,
            Scale: 1,
            Bounds: CellBox(x, y, z, height),
            BreakDefense: 90,
            BreakTick: 5,
            MaterialTag: tag,
            ResetTimer: resetTimer,
            VibrateIndex: index);
    }

    /// <summary>Entities in a cell go in the order given; the structure is serialized and read back as the server loads it.</summary>
    private static FieldAccelerationStructure Build(List<FieldEntity> unaligned, HashSet<FieldVibrateEntity> cubeVibrates, params FieldEntity[] aligned) {
        var grid = new Dictionary<Vector3S, List<FieldEntity>>();
        foreach (FieldEntity entity in aligned) {
            Vector3S cell = entity.Position / Block;
            if (!grid.TryGetValue(cell, out List<FieldEntity>? entities)) {
                grid[cell] = entities = [];
            }
            entities.Add(entity);
        }

        var minIndex = new Vector3S(grid.Keys.Min(cell => cell.X), grid.Keys.Min(cell => cell.Y), grid.Keys.Min(cell => cell.Z));
        // the ingest leaves a row above the highest cell for spawn tiles
        var maxIndex = new Vector3S(grid.Keys.Max(cell => cell.X), grid.Keys.Max(cell => cell.Y), (short) (grid.Keys.Max(cell => cell.Z) + 1));

        var structure = new FieldAccelerationStructure();
        structure.AddEntities(grid, minIndex, maxIndex, unaligned, aligned.Concat(unaligned).Count(entity => entity is FieldVibrateEntity), cubeVibrates);

        var writer = new ByteWriter();
        structure.WriteTo(writer);
        var loaded = new FieldAccelerationStructure();
        loaded.ReadFrom(new ByteReader(writer.Buffer[..writer.Length]));

        return loaded;
    }

    /// <summary>Every vibrate counts as a cube prop, the way IG-02's rules had it.</summary>
    private static FieldAccelerationStructure Build(List<FieldEntity> unaligned, params FieldEntity[] aligned) {
        return Build(unaligned, aligned.Concat(unaligned).OfType<FieldVibrateEntity>().ToHashSet(), aligned);
    }

    private static bool HasTile(FieldAccelerationStructure structure, short x, short y, short z) {
        return structure.QuerySpawnsList(Center(x, y, z) + new Vector3(0, 0, Half), 10).Count > 0;
    }

    [Test]
    public void VibrateMaterialTagAndResetTimerSurviveTheRoundTrip() {
        var wood = FieldEntityId.FromString("00000000000000010000000000000001");
        var plain = FieldEntityId.FromString("00000000000000010000000000000002");

        FieldAccelerationStructure loaded = Build([],
            Vibrate(0, 0, 0, id: wood, index: 0, tag: "wood", resetTimer: 12000000),
            Vibrate(1, 0, 0, id: plain, index: 1));

        FieldVibrateEntity? woodEntity = loaded.GetVibrateEntity(wood.Id);
        Assert.That(woodEntity, Is.Not.Null);
        Assert.That(woodEntity!.MaterialTag, Is.EqualTo("wood"));
        Assert.That(woodEntity.ResetTimer, Is.EqualTo(12000000));
        Assert.That(woodEntity.BreakDefense, Is.EqualTo(90));
        Assert.That(woodEntity.BreakTick, Is.EqualTo(5));
        Assert.That(woodEntity.VibrateIndex, Is.EqualTo(0));

        FieldVibrateEntity? plainEntity = loaded.GetVibrateEntity(plain.Id);
        Assert.That(plainEntity, Is.Not.Null);
        Assert.That(plainEntity!.MaterialTag, Is.Empty);
        Assert.That(plainEntity.ResetTimer, Is.EqualTo(0));
        Assert.That(plainEntity.VibrateIndex, Is.EqualTo(1));
    }

    [Test]
    public void SpawnTileOnTopOfAFullCellMesh() {
        Assert.That(HasTile(Build([], Mesh(0, 0, 0)), 0, 0, 1), Is.True);
    }

    [Test]
    public void SpawnTileOnTopOfAFullCellVibrateEntity() {
        Assert.That(HasTile(Build([], Vibrate(0, 0, 0)), 0, 0, 1), Is.True);
    }

    [Test]
    public void SpawnTileOnTopOfABoxFollowedByAMesh() {
        Assert.That(HasTile(Build([], Box(0, 0, 0), Mesh(0, 0, 0)), 0, 0, 1), Is.True);
    }

    [Test]
    public void NoSpawnTileOnTopOfFluid() {
        Assert.That(HasTile(Build([], Fluid(0, 0, 0)), 0, 0, 1), Is.False);
    }

    [Test]
    public void NoSpawnTileOnTopOfAMeshOrVibrateEntityThatDoesNotFillItsCell() {
        FieldAccelerationStructure structure = Build([], Mesh(0, 0, 0, Half), Vibrate(1, 0, 0, Half));

        Assert.That(HasTile(structure, 0, 0, 1), Is.False);
        Assert.That(HasTile(structure, 1, 0, 1), Is.False);
    }

    [Test]
    public void UnalignedEntityOccupiesTheCellsItCovers() {
        // covers cells -1..1 in x and y at z 1, above the boxes at x 0 and 1
        var roof = Mesh(0, 0, 1) with { Bounds = new BoundingBox3(new Vector3(-100, -100, 160), new Vector3(100, 100, 290)) };

        FieldAccelerationStructure structure = Build([roof], Box(0, 0, 0), Box(1, 0, 0), Box(3, 0, 0));

        Assert.That(HasTile(structure, 0, 0, 1), Is.False);
        Assert.That(HasTile(structure, 1, 0, 1), Is.False);
        Assert.That(HasTile(structure, 3, 0, 1), Is.True);
    }

    [Test]
    public void UnalignedEntityEndingOnACellEdgeDoesNotReachTheNextCell() {
        // x from the left edge of cell -1 to the right edge of cell 0
        var wall = Mesh(0, 0, 1) with { Bounds = new BoundingBox3(new Vector3(-Block - Half, -Half, 160), new Vector3(Half, Half, 290)) };

        FieldAccelerationStructure structure = Build([wall], Box(0, 0, 0), Box(1, 0, 0));

        Assert.That(HasTile(structure, 0, 0, 1), Is.False);
        Assert.That(HasTile(structure, 1, 0, 1), Is.True);
    }

    [Test]
    public void UnalignedEntityFarFromTheOriginLeavesTheTileAtTheOrigin() {
        var tower = Mesh(0, 0, 1) with { Bounds = new BoundingBox3(new Vector3(0, 0, 23000), new Vector3(100, 100, 23100)) };

        FieldAccelerationStructure structure = Build([tower], Box(0, 0, 0));

        Assert.That(HasTile(structure, 0, 0, 1), Is.True);
    }

    [Test]
    public void WhiteboxBoundsAreHalfTheShapeDimensionsOnEveryAxis() {
        BoundingBox3 bounds = MapDataMapper.WhiteboxBounds(new Vector3(600, 300, 450));

        Assert.That(bounds.Min, Is.EqualTo(new Vector3(-300, -150, -225)));
        Assert.That(bounds.Max, Is.EqualTo(new Vector3(300, 150, 225)));
    }

    [Test]
    public void UnalignedVibrateLeavesTheTileInTheCellsItCovers() {
        // a tree: its bounds cover the cell above the box, but a vibrate is no wall
        var tree = Vibrate(0, 0, 1) with { Bounds = new BoundingBox3(new Vector3(-100, -100, 160), new Vector3(100, 100, 290)) };

        Assert.That(HasTile(Build([tree], [tree], Box(0, 0, 0)), 0, 0, 1), Is.True);
    }

    [Test]
    public void VibrateOutsideTheSetOccupiesNoCellAndIsNoGround() {
        FieldVibrateEntity grass = Vibrate(0, 0, 1);

        FieldAccelerationStructure structure = Build([], [], Box(0, 0, 0), grass);

        Assert.That(HasTile(structure, 0, 0, 1), Is.True);
        Assert.That(HasTile(structure, 0, 0, 2), Is.False);
    }

    [Test]
    public void VibrateInTheSetOccupiesItsCellAndIsGround() {
        FieldVibrateEntity cube = Vibrate(0, 0, 1);

        FieldAccelerationStructure structure = Build([], [cube], Box(0, 0, 0), cube);

        Assert.That(HasTile(structure, 0, 0, 1), Is.False);
        Assert.That(HasTile(structure, 0, 0, 2), Is.True);
    }
}
