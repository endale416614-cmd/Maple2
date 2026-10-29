using System.IO;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Tools;

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

    [TestCase(null, true, true, TestName = "Both-mode option unset uses its default")]
    [TestCase("false", true, false, TestName = "Both-mode option can be switched off")]
    [TestCase("TRUE", false, true, TestName = "Both-mode option can be switched on")]
    [TestCase("garbage", true, true, TestName = "Both-mode option unparsable uses its default")]
    public void Option(string? flag, bool defaultValue, bool expected) {
        Assert.That(OfflineProfile.ResolveOption(flag, defaultValue), Is.EqualTo(expected));
    }

    [TestCase("Legacy", null, false, TestName = "Legacy turns the cheat commands off")]
    [TestCase("Legacy", "true", true, TestName = "CheatCommands=true wins under Legacy")]
    [TestCase("Adapted", null, true, TestName = "Adapted keeps the cheat commands")]
    [TestCase("Adapted", "false", false, TestName = "CheatCommands=false wins under Adapted")]
    [TestCase(null, null, true, TestName = "No mode keeps the cheat commands")]
    public void Cheats(string? mode, string? cheatCommands, bool expected) {
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Util", "OfflineCheats.cs"));
        Assert.That(source, Does.Contain("OfflineProfile.Flag(\"OFFLINE_CHEATS\", true)"), "OfflineCheats.Enabled is a mode flag");
        Assert.That(OfflineProfile.Resolve(mode, cheatCommands, adaptedDefault: true), Is.EqualTo(expected));
    }

    [TestCase(26001001, 4, 1, false, true, TestName = "Legacy: a solo player is refused at a guild raid")]
    [TestCase(26001001, 4, 1, true, false, TestName = "Adapted: a solo player may enter a guild raid")]
    [TestCase(29006001, 4, 2, true, false, TestName = "Adapted: two players may enter Ludibrium Clock Tower")]
    [TestCase(29003001, 4, 1, true, true, TestName = "Adapted: Cathy Mart still needs the full group")]
    [TestCase(29003001, 4, 4, true, false, TestName = "A full group enters Cathy Mart")]
    [TestCase(26001001, 4, 4, false, false, TestName = "Legacy: a full group enters")]
    [TestCase(1001, 0, 1, false, false, TestName = "Dungeons without a minimum are never refused")]
    public void PartyDungeons(int dungeonId, int minUserCount, int size, bool open, bool refused) {
        Assert.That(OfflineDungeons.TooFewPlayers(dungeonId, minUserCount, size, open), Is.EqualTo(refused));
    }
}
