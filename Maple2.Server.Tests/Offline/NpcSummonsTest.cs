using System.IO;
using System.Collections.Generic;
using System.Linq;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class NpcSummonsTest {
    [Test]
    public void Parse() {
        string json = """
            {
                "Summons": {
                    "1": [2, 3]
                }
            }
            """;
        var config = NpcSummons.Parse(json);
        Assert.That(config, Is.Not.Null);
        Assert.That(config.Summons.Count, Is.EqualTo(1));
        Assert.That(config.Summons[1], Is.EquivalentTo(new[] { 2, 3 }));
        Assert.That(config.StringIds, Is.Null, "a file without StringIds still loads");
    }

    [Test]
    public void Parse_WithStringIds() {
        string json = """
            {
                "Summons": {
                    "1": [2, 3]
                },
                "StringIds": {
                    "23001001": "Gigantika_01"
                }
            }
            """;
        var config = NpcSummons.Parse(json);
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.StringIds![23001001], Is.EqualTo("Gigantika_01"));
    }

    [Test]
    public void Parse_WithRaces() {
        string json = """
            {
                "Summons": {
                    "1": [2, 3]
                },
                "Races": {
                    "21000013": "animal"
                }
            }
            """;
        var config = NpcSummons.Parse(json);
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Races![21000013], Is.EqualTo("animal"));
    }

    [Test]
    public void ShippedFile_KeepsTheXmlOrder() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        Assert.That(File.Exists(path), Is.True, "summon-npcs.json ships next to the solution");
        var config = NpcSummons.Parse(File.ReadAllText(path));
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Summons[23001001], Is.EqualTo(new[] { 23001002 }), "Gigantica's tail summons her head");
        Assert.That(config.Summons[23000072], Has.Length.EqualTo(13));
        Assert.That(config.Summons[23000072][..4], Is.EqualTo(new[] { 23407007, 23407008, 23407009, 23407010 }), "N = 1-4: Zakum's arms, in order");
    }

    [Test]
    public void ShippedFile_GigantasPhasesShareAStringId() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        var config = NpcSummons.Parse(File.ReadAllText(path));
        Assert.That(config?.StringIds, Is.Not.Null);
        Assert.That(config!.StringIds!, Has.Count.EqualTo(15), "every client NPC with a stringID");
        foreach (int phase in Enumerable.Range(23001001, 6)) {
            Assert.That(config.StringIds[phase], Is.EqualTo("Gigantika_01"), $"phase {phase}");
        }
    }

    [Test]
    public void Parse_WithInteractSkills() {
        string json = """
            {
                "Summons": {},
                "InteractSkills": {
                    "21800911": [50100511, 2, 7000]
                }
            }
            """;
        var config = NpcSummons.Parse(json);
        Assert.That(config!.InteractSkills![21800911], Is.EqualTo(new[] { 50100511, 2, 7000 }));
    }

    [Test]
    public void ShippedFile_InteractSkills() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        var config = NpcSummons.Parse(File.ReadAllText(path));
        Assert.That(config?.InteractSkills, Has.Count.EqualTo(35), "every client NPC with an interactFunction");
        Assert.That(config!.InteractSkills![21800911], Is.EqualTo(new[] { 50100511, 2, 7000 }), "Moonlight Fortress's Pirate Cannon");
        Assert.That(config.InteractSkills[21800912], Is.EqualTo(new[] { 50100511, 3, 7000 }), "the cannon at the gate");
    }

    [Test]
    public void ShippedFile_Races() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        var config = NpcSummons.Parse(File.ReadAllText(path));
        Assert.That(config?.Races, Is.Not.Null);
        Assert.That(config!.Races![21000013], Is.EqualTo("animal"), "Punky Pig has race animal");
        Assert.That(config.Races[21000136], Is.EqualTo("undead"), "Squid has race undead");
    }

    [Test]
    public void ShippedFile_SkillProbabilities() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json");
        var config = NpcSummons.Parse(File.ReadAllText(path));
        Assert.That(config?.SkillProbs, Is.Not.Null);
        Assert.That(config!.SkillProbs![21000101], Is.EqualTo(new[] { 50, 50 }), "two skills at 50/50");
        Assert.That(config.SkillProbs, Has.Count.GreaterThan(4000));
        Assert.That(config.SkillProbs.ContainsKey(21000102), Is.False, "one skill: not listed");
        Assert.That(NpcSummons.Parse("""{"Summons": {}}""")!.SkillProbs, Is.Null, "a file without SkillProbs still loads");
    }

    [Test]
    public void DeadByStringId() {
        var dead = new HashSet<string> { "Gigantika_01" };
        Assert.That(TriggerContext.DeadByStringId(dead, [null, "722"], "Gigantika_01"), Is.True);
        Assert.That(TriggerContext.DeadByStringId(dead, ["Gigantika_01"], "Gigantika_01"), Is.False, "the next phase is alive");
        Assert.That(TriggerContext.DeadByStringId(new HashSet<string>(), [], "Gigantika_01"), Is.False, "no death yet");
        Assert.That(TriggerContext.DeadByStringId(dead, [], "Gigantika_01"), Is.True, "the corpse is gone");
    }
}
