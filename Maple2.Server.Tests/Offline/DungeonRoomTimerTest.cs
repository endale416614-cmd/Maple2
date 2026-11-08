using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>FT-13a: the dungeon room timer. Golden packets from the captures: Temple of Time 2000531 (the orion2 packet log), Terminus
/// of Time 2020120 (Ishura.msb) and Nairin's Combat Simulator 2020019 (Nairin Waves.msb).</summary>
public class DungeonRoomTimerTest {
    private const long Now = 10_000_000;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [Test]
    public void TheCapturedPackets() {
        static string Hex(ByteWriter packet) => Convert.ToHexString(packet.Buffer[..packet.Length]).ToLowerInvariant();
        Assert.Multiple(() => {
            // At the field entry of the timed room, and when Ishura's dungeon_reset_time 720 starts the timer again
            Assert.That(Hex(RoomTimerPacket.Start(RoomTimerType.Clock, unchecked((int) 0x861D4C62), 900000)), Is.EqualTo("5100" + "00" + "01" + "624c1d86" + "a0bb0d00" + "00000000"));
            Assert.That(Hex(RoomTimerPacket.Start(RoomTimerType.Clock, 0x021547E1, 720000, reset: true)), Is.EqualTo("5100" + "02" + "01" + "e1471502" + "80fc0a00" + "00000000"));
            // Ishura's extendRoomTime at her death: the start tick, the new length, 2, +60000
            Assert.That(Hex(RoomTimerPacket.Modify(0x021547E1, 780000, 60000)), Is.EqualTo("5100" + "01" + "e1471502" + "e0e60b00" + "02000000" + "60ea0000"));
            Assert.That(Hex(RoomTimerPacket.Close()), Is.EqualTo("5100" + "04"));
            Assert.That(Hex(RoomTimerPacket.Stop()), Is.EqualTo("5100" + "05"));
        });
    }

    [Test]
    public void ARoomWithADurationTickTimesOutAtItsLength() {
        // Temple of Time (23026003): 900000 from the run's start
        var record = new DungeonRoomRecord(Room(900000));
        record.Start(Now);
        Assert.That(record.TimedOut(Now + 899_999), Is.False);
        Assert.That(record.TimedOut(Now + 900_000), Is.True);

        var untimed = new DungeonRoomRecord(Room(0));
        untimed.Start(Now);
        Assert.That(untimed.TimedOut(Now + 3_600_000), Is.False, "no DurationTick: no timer until dungeon_reset_time");
    }

    [Test]
    public void ResetTimeStartsTheTimerDungeonTimeoutReads() {
        // 02020120_bf/bossspawn: dungeon_reset_time 720 when Ishura appears; dungeon_timeout fails the run
        (DungeonFieldManager field, TriggerContext script) = Build(0);
        script.DungeonResetTime(720);
        SetTick(field, Now + 719_999);
        Assert.That(script.DungeonTimeout(), Is.False);
        SetTick(field, Now + 720_000);
        Assert.That(script.DungeonTimeout(), Is.True);
    }

    [Test]
    public void StopAndCloseTurnTheTimerOff() {
        (DungeonFieldManager field, TriggerContext script) = Build(0);
        script.DungeonResetTime(720);
        SetTick(field, Now + 720_000);
        Assert.That(script.DungeonTimeout(), Is.True);
        // Ishura's failed skill break: dungeon_stop_timer, then dungeon_reset_time 720 when the player is back at the start
        script.DungeonStopTimer();
        Assert.That(script.DungeonTimeout(), Is.False);

        // 02020019_bf/02020019_battle: dungeon_reset_time 300, dungeon_close_timer when the battle ends
        script.DungeonResetTime(300);
        SetTick(field, Now + 1_020_000);
        Assert.That(script.DungeonTimeout(), Is.True);
        script.DungeonCloseTimer();
        Assert.That(script.DungeonTimeout(), Is.False);
    }

    [Test]
    public void ModifyRoomTimeChangesTheLength() {
        (DungeonFieldManager field, TriggerContext script) = Build(900000);
        // AI_DarkStarSummon_ChaosSmall_TypeA.xml: -30000 brings the timeout 30 s nearer
        ModifyRoomTime(field, -30000);
        SetTick(field, Now + 870_000);
        Assert.That(script.DungeonTimeout(), Is.True);
        // AI_ZakumBrown01.xml: +80000 when an arm dies
        ModifyRoomTime(field, 80000);
        Assert.That(script.DungeonTimeout(), Is.False);
        SetTick(field, Now + 950_000);
        Assert.That(script.DungeonTimeout(), Is.True);
        Assert.That(field.DungeonRoomRecord.TimerDuration, Is.EqualTo(950000));
    }

    [Test]
    public void AChangeWithTheTimerOffChangesNothing() {
        // An NPC's extendRoomTime (Ishura 23000113: 60000) in a room without a timer
        (DungeonFieldManager field, _) = Build(0);
        field.ModifyRoomTimer(60000);
        Assert.That(field.DungeonRoomRecord.TimerOn, Is.False);
        Assert.That(field.DungeonRoomRecord.TimerDuration, Is.EqualTo(0));
    }

    [Test]
    public void AnExpiringRoomFailsTheRunAtZero() {
        // FT-13c: 23006003 (02000326) has isExpireTimeOut, 900000 from the run's start, and no dungeon_timeout in its scripts
        (DungeonFieldManager field, _) = Build(900000, expireTimeOut: true);
        SetTick(field, Now + 899_999);
        field.CheckRoomTimeOut();
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.None));
        SetTick(field, Now + 900_000);
        field.CheckRoomTimeOut();
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.Fail));
        Assert.That(field.DungeonRoomRecord.EndTick, Is.EqualTo(Now + 900_000));
    }

    [Test]
    public void TheClockAtZeroEndsNoRunWithoutTheFlagOrInTheLobby() {
        // 24090005: isExpireTimeOut 0, its script fails the run on dungeon_timeout
        (DungeonFieldManager field, _) = Build(300000);
        SetTick(field, Now + 300_000);
        field.CheckRoomTimeOut();
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.None));

        // The lobby shares the run's record; only a room ends the run
        (DungeonFieldManager lobby, _) = Build(900000, expireTimeOut: true);
        SetField(lobby, "<Lobby>k__BackingField", lobby);
        SetTick(lobby, Now + 900_000);
        lobby.CheckRoomTimeOut();
        Assert.That(lobby.DungeonRoomRecord.State, Is.EqualTo(DungeonState.None));
    }

    [Test]
    public void AStoppedTimerOrAFinishedRunStaysAsItIs() {
        // Emerald Prison 2020101 (27000001): dungeon_reset_time 420, dungeon_stop_timer when the boss falls
        (DungeonFieldManager field, TriggerContext script) = Build(0, expireTimeOut: true);
        script.DungeonResetTime(420);
        script.DungeonStopTimer();
        SetTick(field, Now + 420_000);
        field.CheckRoomTimeOut();
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.None));

        // A cleared run is not failed later, by the clock or by dungeon_fail
        field.DungeonRoomRecord.State = DungeonState.Clear;
        script.DungeonResetTime(420);
        SetTick(field, Now + 840_000);
        field.CheckRoomTimeOut();
        script.DungeonFail();
        Assert.That(field.DungeonRoomRecord.State, Is.EqualTo(DungeonState.Clear));
    }

    private static DungeonRoomMetadata Room(int durationTick, bool expireTimeOut = false) => Bare<DungeonRoomMetadata>() with { DurationTick = durationTick, ExpireTimeOut = expireTimeOut };

    // A run started at Now, its room field and a script on it; the field was never initialized, so Broadcast sends nothing.
    private static (DungeonFieldManager, TriggerContext) Build(int durationTick, bool expireTimeOut = false) {
        var record = new DungeonRoomRecord(Room(durationTick, expireTimeOut));
        record.Start(Now);
        var field = Bare<DungeonFieldManager>();
        SetField(field, "<DungeonRoomRecord>k__BackingField", record);
        SetTick(field, Now);
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", field);
        var script = Bare<TriggerContext>();
        SetField(script, "owner", trigger);
        return (field, script);
    }

    private static void SetTick(FieldManager field, long tick) => SetField(field, "<FieldTick>k__BackingField", tick);

    // The AI step as AiState runs it, from an NPC in the room
    private static void ModifyRoomTime(DungeonFieldManager field, int timeTick) {
        var npc = Bare<FieldNpc>();
        SetField(npc, "<Field>k__BackingField", field);
        var ai = Bare<AiState>();
        SetField(ai, "actor", npc);
        MethodInfo process = typeof(AiState).GetMethod("ProcessNode", Any, [typeof(AiMetadata.ModifyRoomTimeNode)])!;
        process.Invoke(ai, [new AiMetadata.ModifyRoomTimeNode("ModifyRoomTime", [], timeTick, true)]);
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
