using System;
using System.Collections.Generic;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Game;

/// <summary>NB-01: AI rules as the data expects them (distance conditions, counters that add, values set on the NPCs in range).</summary>
public class AiRulesTest {
    [Test]
    public void Distance_OverMeansFartherLessMeansCloser() {
        Assert.That(AiState.IsFarther(400 * 400, 300), Is.True);
        Assert.That(AiState.IsFarther(200 * 200, 300), Is.False);
        Assert.That(AiState.IsCloser(200 * 200, 300), Is.True);
        Assert.That(AiState.IsCloser(400 * 400, 300), Is.False);
        Assert.That(AiState.IsFarther(300 * 300, 300), Is.False, "exactly at the distance is neither");
        Assert.That(AiState.IsCloser(300 * 300, 300), Is.False, "exactly at the distance is neither");
    }

    [Test]
    public void CombatTime_WindowInEveryLoop() {
        // Test cases from task spec
        Assert.That(AiState.InCombatWindow(0, 0, 150000, 90000), Is.True);
        Assert.That(AiState.InCombatWindow(89_999, 0, 150000, 90000), Is.True);
        Assert.That(AiState.InCombatWindow(90_000, 0, 150000, 90000), Is.False);
        Assert.That(AiState.InCombatWindow(150_000 + 1000, 0, 150000, 90000), Is.True); // next loop
        Assert.That(AiState.InCombatWindow(12_000, 10000, 20000, 15000), Is.True);
        Assert.That(AiState.InCombatWindow(9_000, 10000, 20000, 15000), Is.False);
        Assert.That(AiState.InCombatWindow(32_000, 10000, 20000, 15000), Is.True);
        Assert.That(AiState.InCombatWindow(8_000, 7000, 10000, 0), Is.True); // no end: the loop's end
        Assert.That(AiState.InCombatWindow(9_999, 7000, 10000, 0), Is.True);
        Assert.That(AiState.InCombatWindow(10_500, 7000, 10000, 0), Is.False);
        Assert.That(AiState.InCombatWindow(5_000, 1000, 0, 0), Is.True); // no loop, no end
        Assert.That(AiState.InCombatWindow(-1, 0, 0, 0), Is.False);
    }

    [Test]
    public void ModifyUserValue_Adds() {
        var values = new Dictionary<string, int>();
        AiState.AddUserValue(values, "PhaseSumTotal", 1);
        AiState.AddUserValue(values, "PhaseSumTotal", 1);
        AiState.AddUserValue(values, "PhaseSumTotal", 1);
        Assert.That(values["PhaseSumTotal"], Is.EqualTo(3));

        AiState.AddUserValue(values, "PhaseSumTotal", -1);
        Assert.That(values["PhaseSumTotal"], Is.EqualTo(2));

        AiState.AddUserValue(values, "OtherKey", 5);
        Assert.That(values["OtherKey"], Is.EqualTo(5), "a new key starts from 0");
    }

    [Test]
    public void SetAiValue_SetsOrAdds() {
        var data = new Dictionary<string, int>();
        AiState.SetAiValue(data, "key1", 5, false);
        AiState.SetAiValue(data, "key1", 7, false);
        Assert.That(data["key1"], Is.EqualTo(7), "without modify the value replaces");

        AiState.SetAiValue(data, "key1", 3, true);
        Assert.That(data["key1"], Is.EqualTo(10), "with modify it adds");

        AiState.SetAiValue(data, "key2", 3, true);
        Assert.That(data["key2"], Is.EqualTo(3), "with modify a missing value is set");
    }

    [Test]
    public void SlaveTargets_AllOrOneAtRandom() {
        // With [1, 2, 3] and isRandom false the result is [1, 2, 3]
        var all = new List<int> {1, 2, 3};
        Assert.That(AiState.SlaveTargets(all, false, Random.Shared), Is.EquivalentTo(new List<int> {1, 2, 3}));

        // With isRandom true and new Random(1) it should be one element that's in [1, 2, 3]
        var random = new Random(1);
        var result = AiState.SlaveTargets(all, true, random);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0], Is.InRange(1, 3));

        // Test that it works with a specific seed for reproducibility
        var seededRandom = new Random(42);
        var result2 = AiState.SlaveTargets(all, true, seededRandom);
        Assert.That(result2.Count, Is.EqualTo(1));
        Assert.That(result2[0], Is.InRange(1, 3));

        // Empty list gives empty result either way
        var empty = new List<int>();
        Assert.That(AiState.SlaveTargets(empty, false, Random.Shared), Is.Empty);
        Assert.That(AiState.SlaveTargets(empty, true, Random.Shared), Is.Empty);

        // [7] with isRandom gives [7]
        var single = new List<int> {7};
        Assert.That(AiState.SlaveTargets(single, true, Random.Shared), Is.EquivalentTo(new List<int> {7}));
    }

    [Test]
    public void Provoke_PassiveMonstersFightBack() {
        // A passive monster (no target) fights back when hit
        Assert.That(BattleState.TakesHitAsTarget(false, false), Is.True);

        // An NPC busy with an ally, barrier or gate turns on the player who hits it
        Assert.That(BattleState.TakesHitAsTarget(true, true), Is.True);

        // A monster already targeting a player does not change target when hit
        Assert.That(BattleState.TakesHitAsTarget(true, false), Is.False);
    }

    [Test]
    public void SelectWeights_UseNpcProb() {
        // AI_Default's battle select: Prob [100] over the skill 1, 2 and 3 entries, useNpcProb.
        Assert.That(AiState.SelectWeights([100], false, [50, 50], 2), Is.EqualTo(new[] { 100 }), "no useNpcProb");
        Assert.That(AiState.SelectWeights([100], true, null, 1), Is.EqualTo(new[] { 100 }), "one skill: not listed");
        Assert.That(AiState.SelectWeights([100], true, [50, 50], 2), Is.EqualTo(new[] { 50, 50 }), "21000101: two skills at 50/50");
        Assert.That(AiState.SelectWeights([70, 30], true, [0, 0], 2), Is.EqualTo(new[] { 70, 30 }), "all 0");
        Assert.That(AiState.SelectWeights([100], true, [], 0), Is.EqualTo(new[] { 100 }));
        Assert.That(AiState.SelectWeights([100], true, [50, 50], 3), Is.EqualTo(new[] { 100 }), "not one per skill");
    }
}
