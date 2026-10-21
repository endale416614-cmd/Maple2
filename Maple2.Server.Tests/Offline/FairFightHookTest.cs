using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class FairFightHookTest {
    [Test]
    public void TestFairFightHookOrder() {
        // Read the Actor.cs file as text
        string testDirectory = TestContext.CurrentContext.TestDirectory;
        string gameDirectory = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", "..", "..", "Maple2.Server.Game"));
        string actorPath = Path.Combine(gameDirectory, "Model", "Field", "Actor", "Actor.cs");

        string content = File.ReadAllText(actorPath);

        // Find the positions of the relevant method calls
        int calculateDamagePos = content.IndexOf("DamageCalculator.CalculateDamage(");
        int offlineFairFightPos = content.IndexOf("OfflineFairFight.Floor(");
        int addDamagePos = content.IndexOf("targetRecord.AddDamage(damageTypeResult, damageResult)");

        // Verify that all three methods appear in the correct order
        Assert.That(calculateDamagePos, Is.GreaterThan(0), "DamageCalculator.CalculateDamage should be found");
        Assert.That(offlineFairFightPos, Is.GreaterThan(calculateDamagePos), "OfflineFairFight.Floor should come after DamageCalculator.CalculateDamage");
        Assert.That(addDamagePos, Is.GreaterThan(offlineFairFightPos), "targetRecord.AddDamage should come after OfflineFairFight.Floor");

        // Verify that OfflineFairFight appears exactly once in the file (not in the percent-of-HP or constant branches)
        int offlineFairFightCount = new string[] { "OfflineFairFight" }
            .Select(s => content.Split(s, StringSplitOptions.None).Length - 1)
            .Sum();
        Assert.That(offlineFairFightCount, Is.EqualTo(1), "OfflineFairFight should appear exactly once in the file");
    }
}