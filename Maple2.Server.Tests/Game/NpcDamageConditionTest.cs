using Maple2.Model.Enum;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class NpcDamageConditionTest {
    [Test]
    public void DamageRate_ComparesDamageTakenWithMaxHealth() {
        // Test with max health 1000, damage 1000 and rate 1.0: GreaterEqual true, Less false
        Assert.IsTrue(TriggerContext.DamageRateMatches(1000, 1000, 1.0f, OperatorType.GreaterEqual));
        Assert.IsFalse(TriggerContext.DamageRateMatches(1000, 1000, 1.0f, OperatorType.Less));

        // Test with damage 500: GreaterEqual false, Less true, LessEqual true for rate 0.5, Greater false for rate 0.5
        Assert.IsFalse(TriggerContext.DamageRateMatches(500, 1000, 1.0f, OperatorType.GreaterEqual));
        Assert.IsTrue(TriggerContext.DamageRateMatches(500, 1000, 1.0f, OperatorType.Less));
        Assert.IsTrue(TriggerContext.DamageRateMatches(500, 1000, 0.5f, OperatorType.LessEqual));
        Assert.IsFalse(TriggerContext.DamageRateMatches(500, 1000, 0.5f, OperatorType.Greater));

        // Test with damage 1500 (held at a floor): GreaterEqual 1.0 true
        Assert.IsTrue(TriggerContext.DamageRateMatches(1500, 1000, 1.0f, OperatorType.GreaterEqual));

        // Test with max health 0: GreaterEqual 1.0 false
        Assert.IsFalse(TriggerContext.DamageRateMatches(1000, 0, 1.0f, OperatorType.GreaterEqual));
    }
}