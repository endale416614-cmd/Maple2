using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>SE-01: Shadow Expedition gauges and score boards count kills (BT-26). Golden: the GMS captures Port 50453 (52100031) and
/// Port 50522 (52100041), the only ones with ShadowExpedition (0x00E3): their Open bytes, and 302 kills of 21 NPC ids worth 5, 10 or 20.</summary>
public class FieldScoreTest {
    // The captured weight of each NPC id that died while a gauge was open; the allies' deaths add nothing.
    private static readonly Dictionary<int, int> Captured = new() {
        [21421421] = 5, [21421422] = 5, [21421423] = 5, [21421424] = 5, [21421425] = 5, [21421426] = 5, [21421503] = 5, [21421505] = 5, [21421506] = 5,
        [21421427] = 10, [21421428] = 10, [21421429] = 10, [21421502] = 10, [21421504] = 10, [21421507] = 10, [21421511] = 10, [21421512] = 10,
        [21421508] = 20, [21421509] = 20, [21421510] = 20, [21421513] = 20,
        [21421409] = 0, [21421412] = 0, [21421417] = 0,
    };

    [Test]
    public void Score_CountsOnlyWhileOpen() {
        var score = new FieldScore();
        Assert.That(score.Add(5), Is.False);
        Assert.That(score.Points, Is.Zero);

        score.Open(gauge: true);
        Assert.That(score.Add(5), Is.True);
        Assert.That(score.Add(10), Is.True);
        Assert.That(score.Add(0), Is.False);
        Assert.Multiple(() => {
            Assert.That(score.Points, Is.EqualTo(15));
            Assert.That(score.IsOpen, Is.True);
            Assert.That(score.IsGauge, Is.True);
        });
    }

    [Test]
    public void Close_KeepsThePointsAndOpenResetsThem() {
        var score = new FieldScore();
        score.Open(gauge: true);
        score.Add(1035); // 52100031's points at its Close
        score.Close();
        Assert.Multiple(() => {
            Assert.That(score.Points, Is.EqualTo(1035), "the conditions read after the Close still see them");
            Assert.That(score.IsGauge, Is.False);
            Assert.That(score.Add(5), Is.False);
        });

        score.Open(gauge: false);
        Assert.That(score.Points, Is.Zero);
    }

    [Test]
    public void SetScore_ReplacesThePoints() {
        var score = new FieldScore();
        score.Open(gauge: false);
        score.Set(0);
        score.Add(1500); // a treasure-map boss: spawn_npc_range score 1500
        Assert.That(score.Points, Is.EqualTo(1500));
        score.Set(200);
        Assert.That(score.Points, Is.EqualTo(200));
    }

    [TestCase(0, 1, 1f, 1, 0, 5)]
    [TestCase(0, 1, 1.25f, 1, 0, 10)]
    [TestCase(0, 1, 1f, 99, 0, 10)]
    [TestCase(0, 7, 1.8f, 1, 0, 20)]
    [TestCase(0, 7, 2f, 99, 0, 20)]
    [TestCase(0, 1, 1f, 1, 1500, 1500)]
    [TestCase(1, 1, 1f, 1, 0, 0)]
    [TestCase(1, 1, 1f, 1, 100, 0)]
    [TestCase(2, 1, 1f, 1, 0, 0)]
    public void KillPoints_SpawnScoreThenClassScaleAndHitImmune(int friendly, int npcClass, float scale, int hitImmune, int spawnScore, int expected) {
        Assert.That(FieldScore.KillPoints(friendly, npcClass, scale, hitImmune, spawnScore), Is.EqualTo(expected));
    }

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.Buffer[2..packet.Length]);

    [Test]
    public void GaugePackets_MatchTheOriginal() {
        ByteWriter open = ShadowExpeditionPacket.Open("$52100041_QD__EVENT_02__4$", 1000);
        Assert.Multiple(() => {
            Assert.That(BitConverter.ToUInt16(open.Buffer, 0), Is.EqualTo((ushort) SendOp.ShadowExpedition));
            Assert.That(Hex(open), Is.EqualTo("00011A002400350032003100300030003000340031005F00510044005F005F004500560045004E0054005F00300032005F005F003400240000000000E8030000"));
            Assert.That(Hex(ShadowExpeditionPacket.Open("", 1000)), Is.EqualTo("0001000000000000E8030000"));
            Assert.That(Hex(ShadowExpeditionPacket.Update(5)), Is.EqualTo("0105000000"));
            Assert.That(Hex(ShadowExpeditionPacket.Update(1035)), Is.EqualTo("010B040000"));
            Assert.That(Hex(ShadowExpeditionPacket.Close()), Is.EqualTo("02"));
        });
    }

    private static HTrigger Parse(string condition) {
        var document = new XmlDocument();
        document.LoadXml($"""<ms2><state name="a"><onEnter></onEnter>{condition}</state><state name="b"><onEnter></onEnter></state></ms2>""");
        return TriggerCache.ParseTrigger("test_map", "test_trigger", document);
    }

    [Test]
    public void Parser_ReadsPointAndOperator() {
        var gauge = (HTrigger.ShadowExpeditionPoints) Parse("""<condition name="shadow_expedition_points" point="1000"><transition state="b" /></condition>""").States[0].Conditions.Single();
        var board = (HTrigger.ScoreBoardScore) Parse("""<condition name="score_board_score" score="18000" operator="Less"><transition state="b" /></condition>""").States[0].Conditions.Single();
        Assert.Multiple(() => {
            Assert.That(gauge.Score, Is.EqualTo(1000));
            Assert.That(board.Score, Is.EqualTo(18000));
            Assert.That(board.OperatorType, Is.EqualTo(OperatorType.Less));
        });
    }

    private static IEnumerable<ICondition> Flatten(IEnumerable<ICondition> conditions) {
        foreach (ICondition condition in conditions) {
            yield return condition;
            if (condition is IGroupCondition group) {
                foreach (ICondition inner in Flatten(group.Conditions)) {
                    yield return inner;
                }
            }
        }
    }

    private static void WithData(Action<MetadataContext> test) {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        test(context);
    }

    /// <summary>The parsed values of every condition with this name (groups included), and the values its XML writes.</summary>
    private static (List<string> Parsed, List<string> Written) Conditions<T>(MetadataContext context, string name, Func<T, string> parsed, Func<XmlNode, string> written) {
        var result = (Parsed: new List<string>(), Written: new List<string>());
        foreach (TriggerMetadata row in context.TriggerMetadata.Where(trigger => trigger.Xml.Contains(name))) {
            var document = new XmlDocument();
            document.LoadXml(row.Xml);
            HTrigger trigger = TriggerCache.ParseTrigger(row.MapXBlock, row.Name, document);
            result.Parsed.AddRange(trigger.States.SelectMany(state => Flatten(state.Conditions)).OfType<T>().Select(parsed));
            result.Written.AddRange(document.SelectNodes($"//condition[@name='{name}']")!.Cast<XmlNode>().Select(written));
        }
        return result;
    }

    [Test]
    [Category("Data")]
    public void Data_EveryGaugeConditionKeepsItsPoint() {
        WithData(context => {
            (List<string> parsed, List<string> written) = Conditions<HTrigger.ShadowExpeditionPoints>(context, "shadow_expedition_points",
                gauge => gauge.Score.ToString(), node => node.Attributes?["point"]?.Value ?? string.Empty);
            Assert.That(written.Count, Is.GreaterThanOrEqualTo(32));
            Assert.That(parsed.Order().ToList(), Is.EqualTo(written.Order().ToList()));
        });
    }

    [Test]
    [Category("Data")]
    public void Data_EveryScoreBoardConditionKeepsItsOperator() {
        WithData(context => {
            (List<string> parsed, List<string> written) = Conditions<HTrigger.ScoreBoardScore>(context, "score_board_score",
                board => $"{board.Score} {board.OperatorType}", node => $"{node.Attributes?["score"]?.Value} {node.Attributes?["operator"]?.Value}");
            Assert.That(written.Count, Is.GreaterThanOrEqualTo(34));
            Assert.That(parsed.Order().ToList(), Is.EqualTo(written.Order().ToList()));
        });
    }

    [Test]
    [Category("Data")]
    public void Data_KillPointsMatchTheCapturedGauges() {
        WithData(context => {
            var weights = new Dictionary<int, int>();
            foreach (int id in Captured.Keys) {
                NpcMetadata? npc = context.NpcMetadata.SingleOrDefault(row => row.Id == id);
                if (npc != null) {
                    weights[id] = FieldScore.KillPoints(npc, 0);
                }
            }
            Assert.That(weights, Is.EquivalentTo(Captured));
        });
    }
}
