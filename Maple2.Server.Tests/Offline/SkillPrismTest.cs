using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class SkillPrismTest {
    // Pirate Cannon shot 50100511 level 2 (Moonlight Fortress): a box 2500 long, 1200 wide, 800 high, starting 500 ahead.
    private static readonly SkillMetadataRange CannonShot = new(SkillRegion.Box, 2500, 800, 1200, 0, 0, Vector3.Zero,
        new Vector3(0, 500, 0), IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);

    [Test]
    public void OffsetTurnsWithTheCaster() {
        // Cannon 502 faces 90: the ledge monsters 217-219 stand ahead of it.
        var prism = CannonShot.GetPrism(new Vector3(-6300, 150, 4500), 90);
        Assert.That(prism.Contains(new Vector3(-5700, -300, 4800)), Is.True);
        Assert.That(prism.Contains(new Vector3(-5550, -150, 4800)), Is.True);
        Assert.That(prism.Contains(new Vector3(-5550, 150, 4800)), Is.True);
    }

    [Test]
    public void FacingOneEightyIsUnchanged() {
        // Cannon 501 faces 180, where world and caster frames agree: the ledge monsters 114-116.
        var prism = CannonShot.GetPrism(new Vector3(-12000, 1050, 4500), 180);
        Assert.That(prism.Contains(new Vector3(-11700, 1650, 4800)), Is.True);
        Assert.That(prism.Contains(new Vector3(-12000, 1650, 4800)), Is.True);
        Assert.That(prism.Contains(new Vector3(-12150, 1800, 4800)), Is.True);
        Assert.That(SkillUtils.Origin(new Vector3(10, 20, 0), new Vector3(3, 500, 0), 0), Is.EqualTo(new Vector2(13, 520)));
    }

    // Pamokan (23100041) faces 90, so his forward is +X. His whirlwind ring is four boxes, each 300 deep and 1000 wide.
    private static readonly Vector3 Pamokan = new(150, -600, 4350);

    private static SkillMetadataRange RingBox(float x, float y, float rotateZ) => new(SkillRegion.Box, 300, 950, 1000, 0, rotateZ, Vector3.Zero,
        new Vector3(x, y, 30), IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);

    private static int[] RingHits(SkillMetadataRange[] ring, Vector3 point) =>
        Enumerable.Range(0, ring.Length).Where(i => ring[i].GetPrism(Pamokan, 90).Contains(point)).ToArray();

    [Test]
    public void WhirlwindRingHitsItsSidesNotItsCentre() {
        // 50100867 level 2: back, right, front and left bands 550 to 850 out. The side boxes turn by RotateZDegree about their own origin.
        SkillMetadataRange[] ring = [RingBox(0, -850, 0), RingBox(550, 0, -90), RingBox(0, 550, 0), RingBox(-550, 0, 90)];
        Assert.That(RingHits(ring, new Vector3(-150, -700, 4450)), Is.Empty);
        Assert.That(RingHits(ring, new Vector3(150, -1300, 4450)), Is.EqualTo(new[] { 1 }));
        Assert.That(RingHits(ring, new Vector3(150, 100, 4450)), Is.EqualTo(new[] { 3 }));
        Assert.That(RingHits(ring, new Vector3(850, -600, 4450)), Is.EqualTo(new[] { 2 }));
        Assert.That(RingHits(ring, new Vector3(-550, -600, 4450)), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void SmallerWhirlwindRingHitsItsSidesNotItsCentre() {
        // 50100871 level 2: the same ring at 500 and -800.
        SkillMetadataRange[] ring = [RingBox(0, -800, 0), RingBox(500, 0, -90), RingBox(0, 500, 0), RingBox(-500, 0, 90)];
        Assert.That(RingHits(ring, new Vector3(-150, -700, 4450)), Is.Empty);
        Assert.That(RingHits(ring, new Vector3(150, -1250, 4450)), Is.EqualTo(new[] { 1 }));
        Assert.That(RingHits(ring, new Vector3(150, 50, 4450)), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void CubePrismTurnsLikeTheTargetPrism() {
        var prism = RingBox(550, 0, -90).GetCubePrism(Pamokan, 90);
        Assert.That(prism.Contains(new Vector3(150, -1300, 4450)), Is.True);
        Assert.That(prism.Contains(new Vector3(-150, -700, 4450)), Is.False);
    }

    [Test]
    public void CubePrismReachesBackAlongTheShape() {
        // 50100127 level 1: a beam 6000 long turned 165 degrees, starting 1900 ahead, RangeAdd.Y 75.
        var beam = new SkillMetadataRange(SkillRegion.Box, 6000, 150, 110, 0, 165, new Vector3(0, 75, 0),
            new Vector3(0, 1900, 0), IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None);
        Assert.That(beam.GetPrism(Vector3.Zero, 180).Contains(new Vector3(-776, -998, 50)), Is.True);
        Assert.That(beam.GetCubePrism(Vector3.Zero, 180).Contains(new Vector3(10, 1939, 50)), Is.True);
        Assert.That(beam.GetPrism(Vector3.Zero, 180).Contains(new Vector3(10, 1939, 50)), Is.False);
    }
}
