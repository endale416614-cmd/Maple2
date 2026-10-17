using Maple2.Database.Context;

namespace Maple2.Server.Tests.Offline;

/// <summary>OfflineProfile.Resolve is pure; the Offline* statics read the environment once, so only Resolve is tested.</summary>
public class OfflineProfileTest {
    [TestCase("Legacy", null, true, false, TestName = "Legacy without override turns the adaptation off")]
    [TestCase("legacy", null, true, false, TestName = "Legacy is case-insensitive")]
    [TestCase("Adapted", null, true, true, TestName = "Adapted without override uses the default")]
    [TestCase("Adapted", null, false, false, TestName = "Adapted keeps an off default")]
    [TestCase(null, null, true, true, TestName = "Unset mode is Adapted")]
    [TestCase("nonsense", null, true, true, TestName = "Unknown mode is Adapted")]
    [TestCase("Legacy", "true", true, true, TestName = "Explicit true wins under Legacy")]
    [TestCase("Adapted", "false", true, false, TestName = "Explicit false wins under Adapted")]
    [TestCase("Legacy", "false", false, false, TestName = "Explicit false under Legacy")]
    [TestCase("Adapted", "True", false, true, TestName = "Explicit true wins over an off default")]
    [TestCase("Legacy", "garbage", true, false, TestName = "Unparsable flag falls back to the profile")]
    [TestCase("Adapted", "garbage", true, true, TestName = "Unparsable flag falls back to the default")]
    public void Resolve(string? mode, string? flag, bool adaptedDefault, bool expected) {
        Assert.That(OfflineProfile.Resolve(mode, flag, adaptedDefault), Is.EqualTo(expected));
    }
}
