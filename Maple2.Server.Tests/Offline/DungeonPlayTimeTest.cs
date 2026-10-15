using System.Linq;
using System.Xml;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>dungeon_play_time was a stub that only matched play_seconds 0, so Winding Halls (27001001) could never clear.</summary>
public class DungeonPlayTimeTest {
    private const long Start = 1_000_000;

    private static HTrigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test_map", "test_trigger", document);
    }

    // Real trigger 02020111_bf/main, state "조건추가" (Winding Halls boss).
    private const string BossXml = """
        <ms2><state name="조건추가"><onEnter></onEnter><condition name="all_of"><group><condition name="monster_dead" spawn_ids="101" /><condition name="dungeon_play_time" operator="Less" play_seconds="420" /></group><transition state="보스전_성공" /></condition><condition name="dungeon_play_time" operator="Equal" play_seconds="420"><transition state="보스전_타임어택실패" /></condition></state><state name="보스전_성공"><onEnter></onEnter></state><state name="보스전_타임어택실패"><onEnter></onEnter></state></ms2>
        """;

    private static DungeonRoomRecord Record(long startTick = Start, long resetTick = 0) {
        return new DungeonRoomRecord(null!) { StartTick = startTick, TimerResetTick = resetTick };
    }

    private static bool Matches(DungeonRoomRecord record, long elapsedMs, int playSeconds, OperatorType op) {
        long from = record.TimerResetTick > 0 ? record.TimerResetTick : record.StartTick;
        return TriggerContext.DungeonPlayTimeMatches(record, from + elapsedMs, playSeconds, op);
    }

    [Test]
    public void Parser_KeepsOperator() {
        HTrigger trigger = Parse(BossXml);
        ICondition[] conditions = trigger.States[0].Conditions.ToArray();
        var timeUp = (HTrigger.DungeonPlayTime) conditions[1];
        Assert.That(timeUp.OperatorType, Is.EqualTo(OperatorType.Equal));
        Assert.That(timeUp.PlaySeconds, Is.EqualTo(420));

        var success = (HTrigger.GroupAllOf) conditions[0];
        var inGroup = success.Conditions.OfType<HTrigger.DungeonPlayTime>().Single();
        Assert.That(inGroup.OperatorType, Is.EqualTo(OperatorType.Less));
    }

    [Test]
    public void Less_IsTrueBeforeTheLimitOnly() {
        DungeonRoomRecord record = Record();
        Assert.Multiple(() => {
            Assert.That(Matches(record, 0, 420, OperatorType.Less), Is.True);
            Assert.That(Matches(record, 419_999, 420, OperatorType.Less), Is.True);
            Assert.That(Matches(record, 420_000, 420, OperatorType.Less), Is.False);
        });
    }

    [Test]
    public void Equal_MeansTimeReached_AndNeverOverlapsLess() {
        DungeonRoomRecord record = Record();
        foreach (long ms in new long[] { 0, 1_000, 419_999, 420_000, 420_500, 421_000, 900_000 }) {
            bool less = Matches(record, ms, 420, OperatorType.Less);
            bool equal = Matches(record, ms, 420, OperatorType.Equal);
            Assert.That(less ^ equal, Is.True, $"exactly one boss branch at {ms} ms");
        }
        // Not only the exact second: a check that lands later still fails the run instead of soft-locking it.
        Assert.That(Matches(record, 425_000, 420, OperatorType.Equal), Is.True);
    }

    [Test]
    public void DefaultOperator_IsGreaterEqual() {
        // 02020019_bf/02020019_battle: <condition name="dungeon_play_time" play_seconds="300"> ends the battle at 5 minutes.
        HTrigger trigger = Parse("""
            <ms2><state name="a"><onEnter></onEnter><condition name="dungeon_play_time" play_seconds="300"><transition state="b" /></condition></state><state name="b"><onEnter></onEnter></state></ms2>
            """);
        var condition = (HTrigger.DungeonPlayTime) trigger.States[0].Conditions.Single();
        Assert.That(condition.OperatorType, Is.EqualTo(OperatorType.GreaterEqual));
        DungeonRoomRecord record = Record();
        Assert.That(Matches(record, 299_999, 300, OperatorType.GreaterEqual), Is.False);
        Assert.That(Matches(record, 300_000, 300, OperatorType.GreaterEqual), Is.True);
    }

    [Test]
    public void LessEqual_CountsWholeSeconds() {
        DungeonRoomRecord record = Record();
        Assert.That(Matches(record, 270_999, 270, OperatorType.LessEqual), Is.True);
        Assert.That(Matches(record, 271_000, 270, OperatorType.LessEqual), Is.False);
    }

    [Test]
    public void ResetTime_RestartsTheClock() {
        // The first map took 10 minutes; the boss room calls dungeon_reset_time before its 420 s check.
        DungeonRoomRecord record = Record(Start, resetTick: Start + 600_000);
        long now = Start + 600_000 + 30_000;
        Assert.That(TriggerContext.DungeonPlayTimeMatches(record, now, 420, OperatorType.Less), Is.True);
        Assert.That(TriggerContext.DungeonPlayTimeMatches(record, now, 420, OperatorType.Equal), Is.False);
    }

    [Test]
    public void WithoutAReset_CountsFromRunStart() {
        DungeonRoomRecord record = Record();
        Assert.That(TriggerContext.DungeonPlayTimeMatches(record, Start + 600_000, 540, OperatorType.GreaterEqual), Is.True);
    }

    [Test]
    public void RunNotStarted_IsFalse() {
        DungeonRoomRecord record = Record(startTick: 0);
        Assert.That(TriggerContext.DungeonPlayTimeMatches(record, Start, 0, OperatorType.GreaterEqual), Is.False);
        Assert.That(TriggerContext.DungeonPlayTimeMatches(record, Start, 420, OperatorType.Less), Is.False);
    }
}
