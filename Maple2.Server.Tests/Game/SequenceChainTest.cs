using System.Collections.Generic;
using Maple2.Server.Game.Trigger;
using Maple2.Model.Metadata;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class SequenceChainTest {
    [Test]
    public void TestSequenceChain() {
        var sequences = new Dictionary<string, AnimationSequenceMetadata>(System.StringComparer.OrdinalIgnoreCase) {
            ["Attack_01_B"] = new AnimationSequenceMetadata("Attack_01_B", 1, 1.5f, []),
            ["Attack_Idle_A"] = new AnimationSequenceMetadata("Attack_Idle_A", 2, 2.0f, []),
            ["Talk_A"] = new AnimationSequenceMetadata("Talk_A", 3, 1.0f, [])
        };

        // Test case 1: Normal chain
        var result1 = TriggerContext.SequenceChain("Attack_01_B,Attack_Idle_A", sequences);
        Assert.That(result1.Count, Is.EqualTo(2));
        Assert.That(result1[0].Name, Is.EqualTo("Attack_01_B"));
        Assert.That(result1[0].Delay, Is.EqualTo(0.0f));
        Assert.That(result1[1].Name, Is.EqualTo("Attack_Idle_A"));
        Assert.That(result1[1].Delay, Is.EqualTo(1.5f));

        // Test case 2: Single sequence
        var result2 = TriggerContext.SequenceChain("Talk_A", sequences);
        Assert.That(result2.Count, Is.EqualTo(1));
        Assert.That(result2[0].Name, Is.EqualTo("Talk_A"));
        Assert.That(result2[0].Delay, Is.EqualTo(0.0f));

        // Test case 3: Unknown sequence
        var result3 = TriggerContext.SequenceChain("Unknown_A, Attack_Idle_A", sequences);
        Assert.That(result3.Count, Is.EqualTo(2));
        Assert.That(result3[0].Name, Is.EqualTo("Unknown_A"));
        Assert.That(result3[0].Delay, Is.EqualTo(0.0f));
        Assert.That(result3[1].Name, Is.EqualTo("Attack_Idle_A"));
        Assert.That(result3[1].Delay, Is.EqualTo(0.0f));

        // Test case 4: Empty string
        var result4 = TriggerContext.SequenceChain("", sequences);
        Assert.That(result4.Count, Is.EqualTo(0));
    }
}