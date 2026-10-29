using System.Collections.Generic;
using System.Text.Json;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>AI-08: an NPC skill motion with a loopCount plays the loop section of its sequence that many more times.</summary>
[TestFixture]
public class MotionLoopCountTest {
    private static AnimationRecord Record(int loopCount) {
        var sequence = new AnimationSequenceMetadata("Attack_01_B", 1, 1f, new List<AnimationKey>());
        return new AnimationRecord(sequence, 1f, AnimationType.Skill, loopCount: loopCount);
    }

    [Test]
    public void LoopCountTwoLoopsTwice() {
        AnimationRecord record = Record(2);
        Assert.That(record.IsLooping, Is.True);

        record.LoopPassed();
        Assert.That(record.IsLooping, Is.True);
        Assert.That(record.LoopCount, Is.EqualTo(1));

        record.LoopPassed();
        Assert.That(record.IsLooping, Is.False);
        Assert.That(record.LoopCount, Is.EqualTo(0));
    }

    [Test]
    public void LoopCountZeroDoesNotLoop() {
        Assert.That(Record(0).IsLooping, Is.False);
    }

    [Test]
    public void LoopOnlyOnceStopsAfterOnePass() {
        AnimationRecord record = Record(0);
        record.IsLooping = true;
        record.LoopOnlyOnce = true;

        record.LoopPassed();

        Assert.That(record.IsLooping, Is.False);
        Assert.That(record.LoopOnlyOnce, Is.False);
    }

    [Test]
    public void LoopingWithoutLimitKeepsLooping() {
        AnimationRecord record = Record(0);
        record.IsLooping = true;

        for (int i = 0; i < 3; i++) {
            record.LoopPassed();
        }

        Assert.That(record.IsLooping, Is.True);
    }

    [Test]
    public void LoopCountMissingFromOlderDataIsZero() {
        const string json = """{"SequenceName":"Attack_01_B","SequenceSpeed":1,"MoveDistance":0,"MoveAngle":0,"FaceTarget":true}""";

        SkillMetadataMotionProperty? property = JsonSerializer.Deserialize<SkillMetadataMotionProperty>(json);

        Assert.That(property, Is.Not.Null);
        Assert.That(property!.LoopCount, Is.EqualTo(0));
    }
}
