using System;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Tests.Offline;

/// <summary>SE-02: fame challenge mission scores count, so the five challenges can be lost (BT-37). Golden: dungeonmission.xml of the
/// shipped data and the captures of room 24094005 (Mason Phases, Clone Failure 1 and 2, Fortress_Rumble__Quest_Objective_Completio).</summary>
public class DungeonMissionScoreTest {
    private const int Mason = 24120001;
    private const int MasonClone = 24120002;

    private static DungeonMissionMetadata Step(int id, long dealt, long npcId = Mason) =>
        new(id, DungeonMissionType.DamageToNpc, [dealt], npcId, 500, 0, false);

    private static DungeonMissionMetadata Other(int id, DungeonMissionType type, params long[] value1) =>
        new(id, type, value1, 0, 500, 0, false);

    /// <summary>Room 24094005, its user missions in the room's order.</summary>
    private static DungeonUserRecord MasonRoom() {
        var record = new DungeonUserRecord(24094005, 1);
        foreach (DungeonMissionMetadata metadata in new[] {
                     Step(24094007, 2_179_967), Step(24094008, 4_359_935), Step(24094009, 6_539_902),
                     Other(24094004, DungeonMissionType.LastHitNpc, 24120000), Other(24094010, DungeonMissionType.Trigger),
                 }) {
            record.Missions[metadata.Id] = new DungeonMission(metadata);
        }
        return record;
    }

    private static int[] Hit(DungeonUserRecord record, long amount, int npcId = Mason) =>
        record.AddNpcDamage(npcId, amount).Select(mission => mission.Id).ToArray();

    private static string Hex(ByteWriter packet) => Convert.ToHexString(packet.Buffer[..packet.Length]);

    [Test]
    public void DamageStep_CompletesAtItsThreshold() {
        var mission = new DungeonMission(Step(24094007, 2_179_967));
        Assert.Multiple(() => {
            Assert.That(mission.CompletesOnDamage(Mason, 2_179_966), Is.False);
            Assert.That(mission.CompletesOnDamage(Mason, 2_179_967), Is.True);
            Assert.That(mission.CompletesOnDamage(MasonClone, 9_000_000), Is.False);
        });
    }

    [Test]
    public void DamageStep_WithoutAnNpcCountsAnyOneNpc() {
        var mission = new DungeonMission(Step(24091007, 5_110_781, npcId: 0));
        Assert.Multiple(() => {
            Assert.That(mission.CompletesOnDamage(23200083, 5_110_780), Is.False);
            Assert.That(mission.CompletesOnDamage(23200083, 5_110_781), Is.True);
        });
    }

    [Test]
    public void Complete_ScoresTheMaxWithCounterOne() {
        var mission = new DungeonMission(Step(24094007, 2_179_967));
        mission.Complete();
        var counted = new DungeonMission(new DungeonMissionMetadata(23037003, DungeonMissionType.DeathCount, [], 0, 300, 5, true));
        counted.Complete();
        Assert.Multiple(() => {
            Assert.That(mission.Score, Is.EqualTo(500));
            Assert.That(mission.Counter, Is.EqualTo(1));
            Assert.That(mission.CompletesOnDamage(Mason, 2_179_967), Is.False, "a completed step does not complete again");
            Assert.That(counted.Counter, Is.EqualTo(5));
        });
    }

    [Test]
    public void OtherTypes_NeverCompleteOnDamage() {
        Assert.Multiple(() => {
            Assert.That(new DungeonMission(Other(24094010, DungeonMissionType.Trigger)).CompletesOnDamage(Mason, long.MaxValue), Is.False);
            Assert.That(new DungeonMission(Other(24094004, DungeonMissionType.LastHitNpc, 24120000)).CompletesOnDamage(24120000, long.MaxValue), Is.False);
        });
    }

    // Capture Ishura: 23037003 at 300 in the Load. Capture Mason Phases: 24094007 at 0.
    [Test]
    public void PenaltyMission_StartsAtItsMaxScore() {
        var penalty = new DungeonMission(new DungeonMissionMetadata(23037003, DungeonMissionType.DeathCount, [], 0, 300, 5, true));
        Assert.Multiple(() => {
            Assert.That(penalty.Score, Is.EqualTo(300));
            Assert.That(penalty.Counter, Is.EqualTo(0));
            Assert.That(new DungeonMission(Step(24094007, 2_179_967)).Score, Is.EqualTo(0));
        });
    }

    [Test]
    public void AddNpcDamage_AddsUpPerNpcAndCompletesOnce() {
        var record = new DungeonUserRecord(24091005, 1);
        record.Missions[24091007] = new DungeonMission(Step(24091007, 5_110_781, npcId: 0));
        Assert.Multiple(() => {
            Assert.That(Hit(record, 3_000_000, 23200083), Is.Empty);
            Assert.That(Hit(record, 3_000_000, 23200089), Is.Empty, "another NPC's damage is not added");
            Assert.That(Hit(record, 2_110_781, 23200083), Is.EqualTo(new[] { 24091007 }));
            Assert.That(Hit(record, 9_000_000, 23200083), Is.Empty, "completed once");
        });
    }

    [Test]
    public void AddNpcDamage_OnlyTheNamedNpcCounts() {
        DungeonUserRecord record = MasonRoom();
        Assert.Multiple(() => {
            Assert.That(Hit(record, 9_000_000, MasonClone), Is.Empty);
            Assert.That(record.MissionScore(), Is.EqualTo(0));
        });
    }

    [Test]
    public void AddNpcDamage_OneHitCanCompleteTwoSteps() {
        Assert.That(Hit(MasonRoom(), 5_000_000), Is.EqualTo(new[] { 24094007, 24094008 }));
    }

    // Capture Mason Phases: the boss's dealt health by its Stat packets: 2,029,158, 2,207,679 (update), 4,283,314, 4,486,900 (update),
    // 6,470,661; then 03 02 and a reward score of 1000.
    [Test]
    public void MasonPhases_FailsAtOneThousand() {
        DungeonUserRecord record = MasonRoom();
        Assert.Multiple(() => {
            Assert.That(Hit(record, 2_029_158), Is.Empty);
            Assert.That(Hit(record, 178_521), Is.EqualTo(new[] { 24094007 }));
            Assert.That(Hit(record, 2_075_635), Is.Empty);
            Assert.That(Hit(record, 203_586), Is.EqualTo(new[] { 24094008 }));
            Assert.That(Hit(record, 1_983_761), Is.Empty);
            Assert.That(record.MissionScore(), Is.EqualTo(1000));
            Assert.That(TriggerContext.ScoreMatches(record.MissionScore(), 1500, OperatorType.Less), Is.True);
            Assert.That(TriggerContext.ScoreMatches(record.MissionScore(), 1500, OperatorType.GreaterEqual), Is.False);
        });
    }

    // Capture Clone Failure 1: 1,748,947 dealt, reward score 0. Clone Failure 2: 1,673,006, 2,367,012 (update), 2,368,655, reward score 500.
    [Test]
    public void CloneFailures_ScoreZeroAndFiveHundred() {
        DungeonUserRecord first = MasonRoom();
        DungeonUserRecord second = MasonRoom();
        Assert.Multiple(() => {
            Assert.That(Hit(first, 1_748_947), Is.Empty);
            Assert.That(first.MissionScore(), Is.EqualTo(0));
            Assert.That(Hit(second, 1_673_006), Is.Empty);
            Assert.That(Hit(second, 694_006), Is.EqualTo(new[] { 24094007 }));
            Assert.That(Hit(second, 1_643), Is.Empty);
            Assert.That(second.MissionScore(), Is.EqualTo(500));
        });
    }

    // Capture Fortress_Rumble__Quest_Objective_Completio: updates at 2,890,185, 5,465,466 and 7,511,103 dealt. Its last-hit mission is not
    // counted here, so the trigger mission makes 2000 where the capture's five make 2500.
    [Test]
    public void ClearRun_ReachesFifteenHundredOnTheThirdStep() {
        DungeonUserRecord record = MasonRoom();
        Assert.Multiple(() => {
            Assert.That(Hit(record, 2_890_185), Is.EqualTo(new[] { 24094007 }));
            Assert.That(Hit(record, 2_575_281), Is.EqualTo(new[] { 24094008 }));
            Assert.That(TriggerContext.ScoreMatches(record.MissionScore(), 1500, OperatorType.GreaterEqual), Is.False);
            Assert.That(Hit(record, 2_045_637), Is.EqualTo(new[] { 24094009 }));
            Assert.That(TriggerContext.ScoreMatches(record.MissionScore(), 1500, OperatorType.GreaterEqual), Is.True);
            Assert.That(TriggerContext.ScoreMatches(record.MissionScore(), 1500, OperatorType.Less), Is.False);
        });
        record.Missions[24094010].Complete();
        Assert.That(record.MissionScore(), Is.EqualTo(2000));
    }

    [TestCase(1500, 1500, OperatorType.GreaterEqual, true)]
    [TestCase(1499, 1500, OperatorType.GreaterEqual, false)]
    [TestCase(1500, 1500, OperatorType.Less, false)]
    [TestCase(1499, 1500, OperatorType.Less, true)]
    [TestCase(1501, 1500, OperatorType.Greater, true)]
    [TestCase(1500, 1500, OperatorType.Greater, false)]
    [TestCase(1500, 1500, OperatorType.Equal, true)]
    [TestCase(1501, 1500, OperatorType.Equal, false)]
    [TestCase(1500, 1500, OperatorType.LessEqual, true)]
    [TestCase(1501, 1500, OperatorType.LessEqual, false)]
    public void ScoreMatches_ComparesTheActualScore(int actual, int score, OperatorType operatorType, bool expected) {
        Assert.That(TriggerContext.ScoreMatches(actual, score, operatorType), Is.EqualTo(expected));
    }

    // Capture Mason Phases (opcode first: RoomStageDungeon 0x0101, DungeonMission 0x0102): the two packets beside the room's RequestFieldEnter and the first update.
    [Test]
    public void Packets_MatchTheCapture() {
        DungeonUserRecord record = MasonRoom();
        Assert.That(Hex(RoomStageDungeonPacket.Set(record.DungeonId)), Is.EqualTo("0101" + "00" + "35A56F01" + "00000000" + "0000"));
        Assert.That(Hex(DungeonMissionPacket.Load(record.Missions)),
            Is.EqualTo("0201" + "00" + "05000000" + "37A56F0100000000" + "38A56F0100000000" + "39A56F0100000000" + "34A56F0100000000" + "3AA56F0100000000"));

        DungeonMission completed = record.AddNpcDamage(Mason, 2_207_679).Single();
        Assert.That(Hex(DungeonMissionPacket.Update(completed)), Is.EqualTo("0201" + "01" + "01000000" + "37A56F01" + "F401" + "0100"));
    }
}
