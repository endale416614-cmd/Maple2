using System.Collections.Generic;
using System.IO;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// FT-12: monster EXP by rank from monsterExpTable. Golden: its level 40 row (table/Server/monsterExpTable.xml of Server.m2d), as
/// MonsterExpTableMapperTest has it. Which class takes which column is a reconstruction (1.2.3).
/// </summary>
public class MonsterExpTest {
    private static readonly MonsterExpTable Table = new(new Dictionary<int, MonsterExpTable.Entry> {
        [40] = new(Common: 6060, Elite: 13786, DungeonElite: 45450, Boss: 155829, DungeonBoss: 218160),
    });

    [TestCase(1, 6060, TestName = "A common monster gives the common column, as before")]
    [TestCase(3, 155829, TestName = "Class 3 gives the boss column")]
    [TestCase(7, 155829, TestName = "Class 7 gives the boss column")]
    [TestCase(4, 218160, TestName = "Class 4 gives the dungeon boss column")]
    [TestCase(6, 218160, TestName = "Class 6 gives the dungeon boss column")]
    [TestCase(5, 6060, TestName = "Any other class gives the common column")]
    public void ByClass(int npcClass, long expected) {
        Assert.That(ExperienceManager.MonsterExp(Table, npcClass, 40), Is.EqualTo(expected));
    }

    [Test]
    public void ALevelWithoutARowGivesNothing() {
        Assert.That(ExperienceManager.MonsterExp(Table, 1, 41), Is.Null);
    }

    [Test]
    public void BothKillPathsReadTheTable() {
        // A kill needs a session and a field, so the paths are checked by their source lines.
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Manager", "ExperienceManager.cs"));
        Assert.That(source.Split("MonsterExp(session.ServerTableMetadata.MonsterExpTable, ").Length - 1, Is.EqualTo(2), "AddMobExp and OnKill");
        Assert.That(source, Does.Not.Contain("ExpBase.TryGetValue(2"), "no kill reads exp table 2");
        string npc = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Model", "Field", "Actor", "FieldNpc.cs"));
        Assert.That(npc, Does.Contain("AddMobExp(Value.Metadata.Basic.Level, Value.Metadata.Basic.Class, expMultiplier)"));
    }
}
