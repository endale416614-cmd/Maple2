using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Game;

/// <summary>BT-69: skillRange, trace and runaway read one skill distance.</summary>
public class AiSkillDistanceTest {
    private static SkillMetadataLevel Level(float detect, params SkillMetadataMotion[] motions) =>
        new(null!, null, null, null!, new SkillMetadataDetect(false, detect), null, null!, [], motions);

    private static SkillMetadataMotion Motion(params float[] ranges) =>
        new(null!, ranges.Select(Attack).ToArray(), new Dictionary<string, byte>());

    private static SkillMetadataAttack Attack(float distance) =>
        new("p0", 0, 0, 0, 0, 0, 0, [], null, new SkillMetadataRange(SkillRegion.Box, distance, 350, 800, 0, 0, Vector3.Zero, Vector3.Zero,
            IncludeCasterType.Exclude, ApplyTargetType.Hostile, SkillTargetType.None), null!, null!, [], []);

    [Test]
    public void DetectDistanceWhenAboveZero() {
        Assert.That(AiState.SkillDistance(Level(50, Motion(900))), Is.EqualTo(50));
    }

    [Test]
    public void FirstAttackRangeWithoutDetect() {
        Assert.That(AiState.SkillDistance(Level(0, Motion(900), Motion(750, 750, 750, 750), Motion(900))), Is.EqualTo(900));
        Assert.That(AiState.SkillDistance(Level(0, Motion(500), Motion(550, 550))), Is.EqualTo(500));
        Assert.That(AiState.SkillDistance(Level(0, Motion(), Motion(250))), Is.EqualTo(250));
    }

    [Test]
    public void ZeroWithoutDetectOrAttack() {
        Assert.That(AiState.SkillDistance(Level(0)), Is.EqualTo(0));
        Assert.That(AiState.SkillDistance(Level(0, Motion())), Is.EqualTo(0));
    }
}
