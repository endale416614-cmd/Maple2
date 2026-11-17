using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>NB-31 (BT-244): the two AI substitute rules on the real records. A reconstruction: the original's rule is not captured.</summary>
[Category("Data")]
[NonParallelizable]
public class AiSubstituteDataTest {
    [Test]
    public void SummonSpiritsAndPinkBeanStatuesGetTheirSubstitutes() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        string[] names = context.AiMetadata.Select(ai => ai.Name).ToArray();
        var missing = context.NpcMetadata.ToList().Where(npc => !string.IsNullOrEmpty(npc.AiPath) && !names.Contains(npc.AiPath, StringComparer.OrdinalIgnoreCase)).ToList();

        List<NpcMetadata> spirits = missing.Where(npc => npc.AiPath.StartsWith("AI_Default_Summon_", StringComparison.Ordinal)).ToList();
        Assert.That(spirits, Has.Count.EqualTo(38));
        AiMetadata baseAi = context.AiMetadata.Single(ai => ai.Name == "AI_Default_Summon.xml");
        var summon = baseAi.BattleEnd.OfType<AiMetadata.SummonNode>().Single();
        Assert.That(summon.NpcId, Is.EqualTo(1), "the base file summons entry 1 of the NPC's summon list");
        foreach (NpcMetadata npc in spirits) {
            Assert.That(AiMetadataStorage.Substitute(npc.AiPath, names), Is.EqualTo("AI_Default_Summon.xml"), npc.AiPath);
            int id = int.Parse(npc.AiPath["AI_Default_Summon_".Length..^".xml".Length]);
            Assert.That(NpcSummons.For(npc.Id)?[0], Is.EqualTo(id), $"{npc.Id}: entry 1 of its summon list is the id in its AI file name");
        }

        List<NpcMetadata> statues = missing.Where(npc => npc.AiPath.StartsWith("BossDungeon/Pinkbean/Chaos/AI_StatueHuman", StringComparison.Ordinal)).ToList();
        Assert.That(statues, Has.Count.EqualTo(6));
        foreach (NpcMetadata npc in statues) {
            Assert.That(AiMetadataStorage.Substitute(npc.AiPath, names), Is.EqualTo(npc.AiPath.Replace("Pinkbean/Chaos", "Pinkbean_Normal/Chaos")), npc.AiPath);
        }
    }
}
