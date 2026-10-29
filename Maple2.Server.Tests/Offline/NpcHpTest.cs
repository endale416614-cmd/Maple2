using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>npc_hp read compare_type while the data writes compare, so every "health at or below X%" step passed at once as higher.</summary>
public class NpcHpTest {
    private static HTrigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test_map", "test_trigger", document);
    }

    private static HTrigger ParseCondition(string attributes) {
        return Parse($"""
            <ms2><state name="a"><onEnter></onEnter><condition name="npc_hp" {attributes} value="75" spawn_id="209" is_relative="true"><transition state="b" /></condition></state><state name="b"><onEnter></onEnter></state></ms2>
            """);
    }

    private static IEnumerable<HTrigger.NpcHp> NpcHps(IEnumerable<ICondition> conditions) {
        foreach (ICondition condition in conditions) {
            if (condition is HTrigger.NpcHp npcHp) {
                yield return npcHp;
            } else if (condition is IGroupCondition group) {
                foreach (HTrigger.NpcHp inner in NpcHps(group.Conditions)) {
                    yield return inner;
                }
            }
        }
    }

    [Test]
    public void Parser_ReadsCompare() {
        HTrigger trigger = ParseCondition("""compare="lowerEqual" """);
        var condition = (HTrigger.NpcHp) trigger.States[0].Conditions.Single();
        Assert.That(condition.CompareType, Is.EqualTo(CompareType.lowerEqual));
        Assert.That(condition.Value, Is.EqualTo(75));
    }

    [Test]
    public void Parser_FallsBackToCompareType() {
        var lower = (HTrigger.NpcHp) ParseCondition("""compare_type="lower" """).States[0].Conditions.Single();
        Assert.That(lower.CompareType, Is.EqualTo(CompareType.lower));

        var none = (HTrigger.NpcHp) ParseCondition("").States[0].Conditions.Single();
        Assert.That(none.CompareType, Is.EqualTo(CompareType.higher));
    }

    [Test]
    [Category("Data")]
    public void Data_EveryConditionKeepsItsCompare() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var parsed = new List<string>();
        var written = new List<string>();
        foreach (TriggerMetadata row in context.TriggerMetadata.Where(trigger => trigger.Xml.Contains("npc_hp"))) {
            var document = new XmlDocument();
            document.LoadXml(row.Xml);
            HTrigger trigger = TriggerCache.ParseTrigger(row.MapXBlock, row.Name, document);
            parsed.AddRange(trigger.States.SelectMany(state => NpcHps(state.Conditions)).Select(npcHp => npcHp.CompareType.ToString()));
            written.AddRange(document.SelectNodes("//condition[@name='npc_hp']")!.Cast<XmlNode>().Select(node => node.Attributes?["compare"]?.Value ?? string.Empty));
        }

        Assert.That(written.Count, Is.GreaterThanOrEqualTo(80));
        Assert.That(parsed.Order().ToList(), Is.EqualTo(written.Order().ToList()));
    }
}
