using System;
using System.Numerics;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Game;

/// <summary>NB-10: AI summon rules (index, cap, position, lifetime).</summary>
public class SummonRulesTest {
    [Test]
    public void Resolve_IsOneBased() {
        int[] list = [11, 12, 13];
        Assert.That(SummonRules.Resolve(list, 1), Is.EqualTo(11));
        Assert.That(SummonRules.Resolve(list, 3), Is.EqualTo(13));
        Assert.That(SummonRules.Resolve(list, 0), Is.Null);
        Assert.That(SummonRules.Resolve(list, 4), Is.Null);
        Assert.That(SummonRules.Resolve(null, 1), Is.Null);
    }

    [Test]
    public void Count_FillsUpToTheCap() {
        Assert.That(SummonRules.Count(1, 3, 0, true), Is.EqualTo(1));
        Assert.That(SummonRules.Count(1, 3, 2, true), Is.EqualTo(1));
        Assert.That(SummonRules.Count(1, 3, 3, true), Is.EqualTo(0), "the group's slots are full");
        Assert.That(SummonRules.Count(4, 3, 1, true), Is.EqualTo(2), "fills the empty slots only");
        Assert.That(SummonRules.Count(5, 0, 100, true), Is.EqualTo(5), "no max: no cap");
        Assert.That(SummonRules.Count(0, 3, 0, true), Is.EqualTo(1), "npcCount 0 counts as 1");
        Assert.That(SummonRules.Count(2, 3, 0, false), Is.EqualTo(2));
        Assert.That(SummonRules.Count(2, 3, SummonRules.UnlinkedCap - 1, false), Is.EqualTo(1));
        Assert.That(SummonRules.Count(2, 3, SummonRules.UnlinkedCap, false), Is.EqualTo(0));
    }

    [Test]
    public void Position_AnchorAndScatter() {
        Vector3 none = Vector3.Zero;
        var random = new Random(1);
        Assert.That(SummonRules.Position(none, null, new Vector3(100, 200, 300), none, none, none, random), Is.EqualTo(new Vector3(100, 200, 300)), "absolute");
        Assert.That(SummonRules.Position(new Vector3(1, 2, 3), null, none, new Vector3(10, 0, 0), none, none, random), Is.EqualTo(new Vector3(11, 2, 3)), "from the summoner");
        Assert.That(SummonRules.Position(none, new Vector3(50, 50, 0), none, none, new Vector3(0, 5, 0), none, random), Is.EqualTo(new Vector3(50, 55, 0)), "from the target");
        Assert.That(SummonRules.Position(new Vector3(10, 10, 10), null, none, none, new Vector3(0, 5, 0), none, random), Is.EqualTo(new Vector3(10, 10, 10)), "no target: the summoner's position");

        for (int i = 0; i < 200; i++) {
            Vector3 scattered = SummonRules.Position(none, null, none, none, none, new Vector3(0, 0, 250), random);
            Assert.That(scattered.X, Is.InRange(-250f, 250f));
            Assert.That(scattered.Y, Is.InRange(-250f, 250f));
            Assert.That(scattered.Z, Is.EqualTo(0), "a radius in Z scatters X and Y, not Z");
        }
    }

    [Test]
    public void LifeTime_MixedUnits() {
        Assert.That(SummonRules.LifeTime(14), Is.EqualTo(TimeSpan.FromSeconds(14)));
        Assert.That(SummonRules.LifeTime(3000), Is.EqualTo(TimeSpan.FromSeconds(3000)));
        Assert.That(SummonRules.LifeTime(6000), Is.EqualTo(TimeSpan.FromSeconds(6)));
        Assert.That(SummonRules.LifeTime(15600), Is.EqualTo(TimeSpan.FromSeconds(15.6)));
        Assert.That(SummonRules.LifeTime(180000), Is.EqualTo(TimeSpan.FromSeconds(180)));
    }
}
