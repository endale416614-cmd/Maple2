using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-22, NB-23 (BT-68): which regions an NPC's cube path places, and where: around its caster, or around each target the
/// cast takes. Goldens: the GMS captures' RegionSkill adds, against the caster's facing and the anchor's position at the attack
/// (SkillDamage, NpcControl, the client's UserSync;
/// Pink Bean's 209.1 is SkillUse's 209.096, which NpcControl truncates to 209.0).</summary>
[TestFixture]
public class CubePointsTest {
    // A magic path move; only Rotate, FireOffset and FireFixed are read.
    private static MagicPath Move(float x, float y, bool rotate = true, Vector3 fireFixed = default) =>
        new(false, 0, rotate, false, true, false, false, false, default, new Vector3(x, y, 0), fireFixed, default, default, default,
            false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SkillEffectMetadataSplash Splash(int delay, bool sensing) => new(0, delay, 0, false, false, false, sensing, false, false, null);

    private static void AssertPoints(Vector3[] actual, params Vector3[] captured) {
        Assert.That(actual, Has.Length.EqualTo(captured.Length));
        for (int i = 0; i < captured.Length; i++) {
            Assert.That(Vector3.Distance(actual[i], captured[i]), Is.LessThan(1f), $"point {i}: {actual[i]}, captured {captured[i]}");
        }
    }

    // Vine Wave 40011001 (Toxic Mushming 24001101, "28-Toxic Garden dungeon"): at (1166, 866, 1799) facing -29.1, the paths of its
    // four attack points (40011001 to 40011004) turn by the facing + 180 and snap; FireOffset.Rotate puts p1 at (1200, 600).
    [Test]
    public void ARotatedPathTurnsByTheFacingAndSnaps() {
        var anchor = new Vector3(1166, 866, 1799);
        AssertPoints(SkillRecord.CubePoints([Move(0, 150)], anchor, -29.1f, true), new Vector3(1050, 750, 1800));
        AssertPoints(SkillRecord.CubePoints([Move(150, 300)], anchor, -29.1f, true), new Vector3(900, 750, 1800));
        AssertPoints(SkillRecord.CubePoints([Move(0, 450)], anchor, -29.1f, true), new Vector3(900, 450, 1800));
        AssertPoints(SkillRecord.CubePoints([Move(150, 600)], anchor, -29.1f, true), new Vector3(750, 450, 1800));
    }

    // Skill 50100075 p0 of 24001803 (path 50000463, "Port 54329"): offsets that do not rotate stay as they are, on the caster's
    // floor (2099 is 1 below the block top at 2100).
    [Test]
    public void AFixedOffsetStaysUnturned() {
        AssertPoints(SkillRecord.CubePoints([Move(-150, 0, false), Move(0, 150, false), Move(0, -150, false), Move(150, 0, false)], new Vector3(-308, 1050, 2099), -16.7f, true),
            new Vector3(-450, 1050, 2100), new Vector3(-300, 1200, 2100), new Vector3(-300, 900, 2100), new Vector3(-150, 1050, 2100));
    }

    // Pink Bean 23502021, skill 50002202 p1 (path 7526, "Port_54344"): its cylinder region keeps the turned point off the grid,
    // 3 above the caster.
    [Test]
    public void AnUnalignedPointKeepsItsPlace() {
        AssertPoints(SkillRecord.CubePoints([Move(0, 850)], new Vector3(1369, -1082, 1349), 209.1f, false), new Vector3(955.86f, -339.46f, 1352));
    }

    // Pink Bean 23502021, skill 50002207 p0 and p1 (paths 7541 and 7542; p1's speaker waits for its sensor): a FireFixed move is a map
    // position, wherever the caster stands.
    [Test]
    public void FireFixedIsAMapPosition() {
        var anchor = new Vector3(1560, 1260, 1705);
        AssertPoints(SkillRecord.CubePoints([Move(0, 0, true, new Vector3(900, -300, 1350)), Move(0, 0, true, new Vector3(600, -150, 1350))], anchor, -45f, true),
            new Vector3(900, -300, 1350), new Vector3(600, -150, 1350));
        AssertPoints(SkillRecord.CubePoints([Move(0, 0, true, new Vector3(825, -525, 1350))], anchor, -45f, false), new Vector3(825, -525, 1350));
    }

    // A cast on another target lays the path around it, turned by the caster's facing (NB-23: 248 of 312 captured adds of such casts,
    // within 2 units). Rex 21502202's 50002455 (path 7565, "Port_54344"), facing -90 at (2150, 215, 1499), lays its eight tiles around
    // the player at (1048, -721, 1351); Necromancer Ralph 32002801's 49156061 (path 600160612, "Dark_Descent_1-30") turns its four
    // cylinders by its facing -46.3 around the player at (-646, -178, 2551), 3 above him.
    [Test]
    public void APathAnchorsOnItsTarget() {
        AssertPoints(SkillRecord.CubePoints([Move(-300, 0, false), Move(-150, 0, false), Move(-150, -150, false), Move(-150, -300, false),
                Move(0, 150, false), Move(0, 0, false), Move(0, -150, false), Move(150, -150, false)], new Vector3(1048, -721, 1351), -90f, true),
            new Vector3(750, -750, 1350), new Vector3(900, -750, 1350), new Vector3(900, -900, 1350), new Vector3(900, -1050, 1350),
            new Vector3(1050, -600, 1350), new Vector3(1050, -750, 1350), new Vector3(1050, -900, 1350), new Vector3(1200, -900, 1350));
        AssertPoints(SkillRecord.CubePoints([Move(450, 450), Move(-450, 450), Move(450, -450), Move(-450, -450)], new Vector3(-646, -178, 2551), -46.3f, false),
            new Vector3(-1282.21f, -162.62f, 2554), new Vector3(-661.38f, -814.21f, 2554), new Vector3(-630.62f, 458.21f, 2554), new Vector3(-9.79f, -193.38f, 2554));
    }

    // A missile lays its path around the target it hits, when it hits (NB-26): Cyber-Balrog 24100003's 42010201 (missile 5678, path
    // 50000657, "Cyber Gladiator Cyber Balrog attacks") hits the player at (482, 1982, 1501), and its box region 99999795 lands in the
    // hit's tick on nine tiles; the path does not rotate. Of 574 captured hits of NPC missiles with a cube path, the path around the
    // target fits 357 (either align) and around the hit's impact point 471, which the server does not have.
    [Test]
    public void AMissilesPathAnchorsOnTheTargetItHits() {
        AssertPoints(SkillRecord.CubePoints([Move(-150, 150, false), Move(-150, 0, false), Move(-150, -150, false), Move(0, 150, false), Move(0, 0, false),
                Move(0, -150, false), Move(150, 150, false), Move(150, 0, false), Move(150, -150, false)], new Vector3(482, 1982, 1501), 131.6f, true),
            new Vector3(300, 2100, 1500), new Vector3(300, 1950, 1500), new Vector3(300, 1800, 1500), new Vector3(450, 2100, 1500), new Vector3(450, 1950, 1500),
            new Vector3(450, 1800, 1500), new Vector3(600, 2100, 1500), new Vector3(600, 1950, 1500), new Vector3(600, 1800, 1500));
    }

    // Pink Bean's speakers (50002207 level 2, a sensor 110 wide) have a splash that does not sense (Delay 300 s): laid as it is, it
    // would hit him at once, so the path lays it to wait for its sensor (NB-27). Vine Splash 40011002 has no sensor; a sensing splash
    // keeps its sensor's wait.
    [Test]
    public void ARegionWithItsOwnSensorWaitsForIt() {
        var speaker = new SkillMetadataSensor(new SkillMetadataRange(SkillRegion.Cylinder, 110, 250, 0, 0, 0, default, default,
            IncludeCasterType.Exclude, ApplyTargetType.None, SkillTargetType.None), 0, false);
        SkillEffectMetadataSplash speakers = Splash(300_000, false), vineSplash = Splash(500, false), sensing = Splash(0, true);
        Assert.That(SkillRecord.PlacesCubeRegion(speaker, speakers), Is.False);
        Assert.That(SkillRecord.PlacesCubeRegion(null, vineSplash), Is.True);
        Assert.That(SkillRecord.PlacesCubeRegion(speaker, sensing), Is.True);
        Assert.That(SkillRecord.CubeRegionSplash(speaker, speakers), Is.EqualTo(speakers with { OnlySensingActive = true }));
        Assert.That(SkillRecord.CubeRegionSplash(null, vineSplash), Is.SameAs(vineSplash));
        Assert.That(SkillRecord.CubeRegionSplash(speaker, sensing), Is.SameAs(sensing));
    }

    // Eupheria 23501101's 60300007 (paths 6113 to 6117, "Port 57951") lays its five mines around her in all 6 captured casts, each
    // with no target; a region with no sensor goes around each target the cast takes (NB-23), and with none, nowhere.
    [Test]
    public void AMineIsLaidAroundItsCaster() {
        var mine = new SkillMetadataSensor(new SkillMetadataRange(SkillRegion.Cylinder, 50, 50, 0, 0, 0, default, default,
            IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.Owner), 0, false);
        Assert.That(SkillRecord.CubeAnchors<int>([], 7, mine), Is.EqualTo(new[] { 7 }));
        Assert.That(SkillRecord.CubeAnchors<int>([8, 9], 7, mine), Is.EqualTo(new[] { 7 }));
        Assert.That(SkillRecord.CubeAnchors<int>([8, 9], 7, null), Is.EqualTo(new[] { 8, 9 }));
        Assert.That(SkillRecord.CubeAnchors<int>([], 7, null), Is.Empty);
    }

    // Vine Splash 40011002 is a box, a block's tile: its points snap. Pink Bean's 99999738 is a cylinder: its points keep their place.
    [TestCase(SkillRegion.Box, true)]
    [TestCase(SkillRegion.Cylinder, false)]
    [TestCase(null, false)]
    public void ABoxRegionSnapsToTheBlocks(SkillRegion? firstRange, bool expected) {
        Assert.That(SkillRecord.AlignsCubeRegion(firstRange), Is.EqualTo(expected));
    }
}
