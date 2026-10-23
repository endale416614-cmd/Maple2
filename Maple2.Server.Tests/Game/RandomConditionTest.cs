using System;
using System.Linq;
using Maple2.Server.Game.Trigger.Helpers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class RandomConditionTest {
    private static readonly float[] SixOfTwenty = [20f, 20f, 20f, 20f, 20f, 20f];

    [Test]
    public void PickRandom_EqualWeights() {
        Assert.That(TriggerState.PickRandom(SixOfTwenty, 0.0), Is.EqualTo(0));
        Assert.That(TriggerState.PickRandom(SixOfTwenty, 0.5), Is.EqualTo(3));
        Assert.That(TriggerState.PickRandom(SixOfTwenty, 0.99), Is.EqualTo(5));
    }

    [Test]
    public void PickRandom_EveryBranchFires() {
        // The old roll gave the first of six branches 40 % and the last two never (Gigantica's spawn points 1005, 1006).
        var random = new Random(1);
        int[] counts = new int[SixOfTwenty.Length];
        for (int i = 0; i < 60_000; i++) {
            counts[TriggerState.PickRandom(SixOfTwenty, random.NextDouble())]++;
        }
        Assert.That(counts, Has.All.InRange(9_000, 11_000));
    }

    [Test]
    public void PickRandom_RelativeWeights() {
        Assert.That(TriggerState.PickRandom([1f, 1f, 1f, 1f], 0.74), Is.EqualTo(2));
        Assert.That(TriggerState.PickRandom(Enumerable.Repeat(50f, 20).ToArray(), 0.999), Is.EqualTo(19), "melody puzzle: 20 patterns of 50");
        Assert.That(TriggerState.PickRandom([33f], 0.0), Is.EqualTo(0));
        Assert.That(TriggerState.PickRandom([33f], 0.99), Is.EqualTo(0), "a lone branch always fires");
    }

    [Test]
    public void PickRandom_ZeroWeightsAndRounding() {
        Assert.That(TriggerState.PickRandom([0f, 5f], 0.0), Is.EqualTo(1));
        Assert.That(TriggerState.PickRandom([], 0.5), Is.EqualTo(-1));
        Assert.That(TriggerState.PickRandom([0f, 0f], 0.5), Is.EqualTo(-1));
        Assert.That(TriggerState.PickRandom([0.1f, 0.2f], 0.9999999999), Is.EqualTo(1));
    }
}
