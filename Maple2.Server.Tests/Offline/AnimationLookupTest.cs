using System;
using System.Collections.Generic;
using Maple2.Model.Metadata;
using Maple2.Database.Storage;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class AnimationLookupTest {
    [Test]
    public void TestIgnoreCase() {
        // Create a model with sequences that differ only by case
        var sequences = new Dictionary<string, AnimationSequenceMetadata>(StringComparer.Ordinal) {
            ["ChatUp_A"] = new AnimationSequenceMetadata("ChatUp_A", 1, 1.0f, []),
            ["Idle_A"] = new AnimationSequenceMetadata("Idle_A", 2, 1.0f, [])
        };

        var animation = new AnimationMetadata("TestModel", sequences);

        // Test the IgnoreCase method
        var result = NpcMetadataStorage.IgnoreCase(animation);

        // Verify that case-insensitive lookup works
        Assert.IsTrue(result.Sequences.TryGetValue("ChatUP_A", out var sequence));
        Assert.AreEqual(1, sequence.Id);

        Assert.IsTrue(result.Sequences.TryGetValue("chatup_a", out sequence));
        Assert.AreEqual(1, sequence.Id);

        Assert.IsTrue(result.Sequences.TryGetValue("IDLE_A", out sequence));
        Assert.AreEqual(2, sequence.Id);

        // Verify that non-existent sequences still don't exist
        Assert.IsFalse(result.Sequences.TryGetValue("Walk_A", out _));

        // Test with sequences that have the same name but different case (should keep first one)
        var sequences2 = new Dictionary<string, AnimationSequenceMetadata>(StringComparer.Ordinal) {
            ["Bore_A"] = new AnimationSequenceMetadata("Bore_A", 3, 1.0f, []),
            ["bore_A"] = new AnimationSequenceMetadata("bore_A", 4, 1.0f, [])
        };

        var animation2 = new AnimationMetadata("TestModel2", sequences2);
        var result2 = NpcMetadataStorage.IgnoreCase(animation2);

        // Should find "BORE_A" and get the first one (Bore_A with id 3)
        Assert.IsTrue(result2.Sequences.TryGetValue("BORE_A", out sequence));
        Assert.AreEqual(3, sequence.Id);

        // Model name should be preserved
        Assert.AreEqual("TestModel", result.Model);
        Assert.AreEqual("TestModel2", result2.Model);
    }
}