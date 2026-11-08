using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Tools.Collision;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-22a, NB-27 (BT-68): an NPC's region counts its caster only for an attack on his own side, and only while he stands in it;
/// a boss's own sensor sees only him, once he has been off it. Golden: the GMS captures (build 12),
/// Lazy Stumpy 24000409's 60036032 (IncludeCaster 1, Hostile) in "23-Cherry Blossom Forest dungeon": 25 hits, 2 on the player and none
/// on him; Pink Bean 23502021's speakers 50002207 level 2 (IncludeCaster 1, Friendly): 4 hits, each on him.</summary>
[TestFixture]
public class RegionCasterTest {
    // 60036032's range: a block's tile.
    private static SkillMetadataRange Range(IncludeCasterType includeCaster, ApplyTargetType applyTarget) =>
        new(SkillRegion.Box, 40, 140, 80, 0, 0, new Vector3(0, 50, 0), Vector3.Zero, includeCaster, applyTarget, SkillTargetType.Owner);

    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Hostile, true, IncludeCasterType.Exclude)] // 60036032, 40153022, 60081042, 49148012
    [TestCase(IncludeCasterType.Last, ApplyTargetType.Hostile, true, IncludeCasterType.Exclude)]
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Player1, true, IncludeCasterType.Exclude)] // 49250002: no pool, so only him, for his whole health
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Friendly, true, IncludeCasterType.Priority)] // the speakers, Pamokan's pillars, with him on them
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Friendly, false, IncludeCasterType.Exclude)] // ... and off them (NB-27)
    [TestCase(IncludeCasterType.Last, ApplyTargetType.Friendly, false, IncludeCasterType.Exclude)]
    [TestCase(IncludeCasterType.Exclude, ApplyTargetType.Friendly, true, IncludeCasterType.Exclude)]
    public void AnNpcsRegionCountsItsCasterOnlyOnItsSideAndInIt(IncludeCasterType includeCaster, ApplyTargetType applyTarget, bool holdsCaster,
        IncludeCasterType expected) {
        Assert.That(FieldSkill.CasterRange(Range(includeCaster, applyTarget), npcCaster: true, holdsCaster).IncludeCaster, Is.EqualTo(expected));
    }

    // NB-29: a player's region counts him only while he stands in it, on either side; FindTargets passes holdsCaster true for the map's.
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Friendly, true, IncludeCasterType.Priority)] // 99900462, Honing Runes 10900162, with him in it
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Friendly, false, IncludeCasterType.Exclude)] // ... and out of it
    [TestCase(IncludeCasterType.Last, ApplyTargetType.Friendly, false, IncludeCasterType.Exclude)]
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Hostile, true, IncludeCasterType.Priority)]
    [TestCase(IncludeCasterType.Priority, ApplyTargetType.Hostile, false, IncludeCasterType.Exclude)]
    public void APlayersRegionCountsHimOnlyInIt(IncludeCasterType includeCaster, ApplyTargetType applyTarget, bool holdsCaster, IncludeCasterType expected) {
        Assert.That(FieldSkill.CasterRange(Range(includeCaster, applyTarget), npcCaster: false, holdsCaster).IncludeCaster, Is.EqualTo(expected));
    }

    // 99900462 (item 18200005's 99900461: a cylinder of 300, 180 high and 50 more, IncludeCaster 1, Friendly) in "Ishura": the region at
    // (2017, 5285, 2851) hit its player at (2193, 5475, 2851), 259 from its middle; the one at (6006, 6092, 2401) did not hit him at
    // (6307, 5962, 2401), 328 from it. The player's shape is FieldPlayer's: a circle of 10, 100 high.
    [Test]
    public void APlayerStandsInHisRegionByItsPrism() {
        var range = new SkillMetadataRange(SkillRegion.Cylinder, 300, 180, 0, 0, 0, new Vector3(0, 0, 50), Vector3.Zero, IncludeCasterType.Priority,
            ApplyTargetType.Friendly, SkillTargetType.Owner);
        Prism Player(float x, float y, float z) => new(new Circle(new Vector2(x, y), 10), z, 100);
        Assert.That(FieldSkill.HoldsCaster([range.GetPrism(new Vector3(2017, 5285, 2851), 0, range.ApplyTarget)], Player(2193, 5475, 2851)), Is.True);
        Assert.That(FieldSkill.HoldsCaster([range.GetPrism(new Vector3(6006, 6092, 2401), 0, range.ApplyTarget)], Player(6307, 5962, 2401)), Is.False);
    }

    // Pink Bean 23502021 (radius 100, height 250) and his speaker 5338747 (50002207 level 2 at (825, -525, 1350), 110 wide and 250 high;
    // "Port_54344"): it hit him at (748, -707, 1349), 197 from its middle and 1 under it; he cast it from (1560, 1260, 1705).
    [Test]
    public void TheCasterStandsInARegionByHisShape() {
        Prism[] speaker = [new Prism(new Circle(new Vector2(825, -525), 110), 1350, 250)];
        var onIt = new Vector3(748, -707, 1349);
        Assert.That(FieldSkill.HoldsCaster(speaker, new Prism(new Circle(new Vector2(onIt.X, onIt.Y), 100), onIt.Z, 250)), Is.True);
        Assert.That(FieldSkill.SensesCaster(IncludeCasterType.Priority, speaker, onIt), Is.False, "by his position he is off it");
        Assert.That(FieldSkill.HoldsCaster(speaker, new Prism(new Circle(new Vector2(1560, 1260), 100), 1705, 250)), Is.False);
    }

    // The speakers' sensor (50002207 level 2: no side) and attack (Friendly, IncludeCaster 1); Eupheria's mines (60300013 level 2) sense
    // the players (Hostile); the sensing regions that borrow a Hostile side count no caster; the Wizard's 10300199 is a player's.
    [Test]
    public void ABossesOwnSensorNamesNoSideWhileItsAttackCountsHim() {
        SkillMetadataSensor speaker = Sensor(ApplyTargetType.None, forceInvoke: false);
        SkillMetadataRange friendly = Range(IncludeCasterType.Priority, ApplyTargetType.Friendly);
        Assert.That(FieldSkill.OwnSensor(speaker, friendly, npcCaster: true), Is.True);
        Assert.That(FieldSkill.OwnSensor(speaker, friendly, npcCaster: false), Is.False);
        Assert.That(FieldSkill.OwnSensor(Sensor(ApplyTargetType.Hostile, forceInvoke: false), Range(IncludeCasterType.Exclude, ApplyTargetType.Hostile), npcCaster: true), Is.False);
        Assert.That(FieldSkill.OwnSensor(speaker, Range(IncludeCasterType.Exclude, ApplyTargetType.Hostile), npcCaster: true), Is.False);
        Assert.That(FieldSkill.OwnSensor(Sensor(ApplyTargetType.None, forceInvoke: true), friendly, npcCaster: true), Is.False);
        Assert.That(FieldSkill.OwnSensor(null, friendly, npcCaster: true), Is.False);
    }

    // A boss's own trap fires when he steps on it, never at once where it lands on him.
    [Test]
    public void AnOwnSensorWaitsForItsCasterToStepOnIt() {
        bool left = false;
        Assert.That(FieldSkill.OwnSensorSees(ref left, holdsCaster: true), Is.False, "it landed on him");
        Assert.That(FieldSkill.OwnSensorSees(ref left, holdsCaster: false), Is.False);
        Assert.That(FieldSkill.OwnSensorSees(ref left, holdsCaster: true), Is.True, "he stepped back on it");

        bool away = false;
        Assert.That(FieldSkill.OwnSensorSees(ref away, holdsCaster: false), Is.False, "it landed away from him");
        Assert.That(FieldSkill.OwnSensorSees(ref away, holdsCaster: true), Is.True);
    }

    private static SkillMetadataSensor Sensor(ApplyTargetType applyTarget, bool forceInvoke) => new(new SkillMetadataRange(SkillRegion.Cylinder,
        110, 250, 0, 0, 0, default, default, IncludeCasterType.Exclude, applyTarget, SkillTargetType.None), 0, forceInvoke);
}
