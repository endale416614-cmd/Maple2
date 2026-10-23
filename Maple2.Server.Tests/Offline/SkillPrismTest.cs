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
}
