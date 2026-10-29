using System;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

public class InteractSpawnTest {
    // Next(int) always returns the same value, so an entry is rolled when that value < its Probability.
    private sealed class FixedRandom(int value) : Random {
        public override int Next(int maxValue) => value;
    }

    private static readonly InteractObjectMetadataSpawn[] Jars = [
        new(21000990, 150, 1, 7000, 15000),
        new(21000991, 150, 1, 5000, 15000),
    ];

    private static readonly InteractObjectMetadataSpawn[] Rocks = [
        new(29000022, 75, 3, 7000, 15000),
        new(29000022, 75, 5, 3000, 15000),
    ];

    [Test]
    public void JarsRollEachEntryOnItsOwn() {
        Assert.That(InteractSpawn.Roll(Jars, new FixedRandom(4999)).Select(spawn => spawn.Id), Is.EqualTo(new[] { 21000990, 21000991 }));
        Assert.That(InteractSpawn.Roll(Jars, new FixedRandom(5000)).Select(spawn => spawn.Id), Is.EqualTo(new[] { 21000990 }));
        Assert.That(InteractSpawn.Roll(Jars, new FixedRandom(7000)), Is.Empty);
    }

    [Test]
    public void RocksRollEachEntryOnItsOwn() {
        Assert.That(InteractSpawn.Roll(Rocks, new FixedRandom(2999)).Select(spawn => spawn.Count), Is.EqualTo(new[] { 3, 5 }));
        Assert.That(InteractSpawn.Roll(Rocks, new FixedRandom(6999)).Select(spawn => spawn.Count), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void ProbabilityEdges() {
        InteractObjectMetadataSpawn[] always = [new(1, 1, 1, 10000, 0)];
        InteractObjectMetadataSpawn[] never = [new(1, 1, 1, 0, 0)];
        Assert.That(InteractSpawn.Roll(always, new FixedRandom(9999)), Has.Exactly(1).Items);
        Assert.That(InteractSpawn.Roll(never, new FixedRandom(0)), Is.Empty);
    }
}
