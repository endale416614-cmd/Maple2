using NUnit.Framework;
using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class RoutineSequenceTest {
    [Test]
    public void TestWalkOrRun() {
        // Test with only Run_A (23 models have only Run_A, Nutritious Larva 32001102)
        var animations = new Dictionary<string, AnimationSequenceMetadata> {
            ["Run_A"] = new AnimationSequenceMetadata("Run_A", 1, 1f, []),
        };
        Assert.That(FieldNpc.WalkOrRun(animations), Is.EqualTo(animations["Run_A"]));

        // Test with Walk_A and Run_A - should prefer Walk_A
        animations = new Dictionary<string, AnimationSequenceMetadata> {
            ["Walk_A"] = new AnimationSequenceMetadata("Walk_A", 1, 1f, []),
            ["Run_A"] = new AnimationSequenceMetadata("Run_A", 1, 1f, []),
        };
        Assert.That(FieldNpc.WalkOrRun(animations), Is.EqualTo(animations["Walk_A"]));

        // Test with neither - should return null
        animations = new Dictionary<string, AnimationSequenceMetadata>();
        Assert.That(FieldNpc.WalkOrRun(animations), Is.Null);
    }

    [Test]
    public void TestRoutineSequence() {
        var animations = new Dictionary<string, AnimationSequenceMetadata> {
            ["Idle_A"] = new AnimationSequenceMetadata("Idle_A", 1, 1f, []),
            ["Run_A"] = new AnimationSequenceMetadata("Run_A", 1, 1f, []),
            ["Attack_A"] = new AnimationSequenceMetadata("Attack_A", 1, 1f, []),
        };

        // Walk_A with walk sequence (Run_A) should return Run_A
        var walkSequence = new AnimationSequenceMetadata("Run_A", 1, 1f, []);
        Assert.That(FieldNpc.RoutineSequence(animations, "Walk_A", walkSequence), Is.EqualTo(walkSequence));

        // Idle_A should return Idle_A directly
        Assert.That(FieldNpc.RoutineSequence(animations, "Idle_A", walkSequence), Is.EqualTo(animations["Idle_A"]));

        // Bore_A should return null since it's not in animations
        Assert.That(FieldNpc.RoutineSequence(animations, "Bore_A", walkSequence), Is.Null);

        // Walk_A with null walk sequence should return null
        Assert.That(FieldNpc.RoutineSequence(animations, "Walk_A", null), Is.Null);
    }
}
