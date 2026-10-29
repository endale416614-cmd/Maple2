using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>A hit tree counts for the vibrate_object conditions that name its material (NB-18).</summary>
[TestFixture]
public class VibrateConditionTest {
    [Test]
    public void CodedConditionCountsOnlyItsMaterial() {
        var condition = new ConditionMetadata(ConditionType.vibrate_object, 100, new ConditionMetadata.Parameters(Strings: ["wood"]), null);

        Assert.That(ConditionUtil.Check(condition, null!, codeString: "wood"), Is.True);
        Assert.That(ConditionUtil.Check(condition, null!, codeString: "grass"), Is.False);
        Assert.That(ConditionUtil.Check(condition, null!, codeString: "stone"), Is.False);
        Assert.That(ConditionUtil.Check(condition, null!, codeString: ""), Is.False);
    }

    [Test]
    public void UncodedConditionCountsEveryHit() {
        var condition = new ConditionMetadata(ConditionType.vibrate_object, 100, null, null);

        Assert.That(ConditionUtil.Check(condition, null!, codeString: ""), Is.True);
        Assert.That(ConditionUtil.Check(condition, null!, codeString: "grass"), Is.True);
    }
}
