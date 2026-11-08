using System.Collections.Concurrent;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;

namespace Maple2.Model.Game.Dungeon;

public class DungeonRoomRecord {
    public ConcurrentDictionary<long, DungeonUserRecord> UserResults = [];
    public readonly DungeonRoomMetadata Metadata;
    public long StartTick { get; set; }
    // Set by dungeon_reset_time: dungeon_play_time counts from here instead of StartTick (boss time attacks restart the clock)
    public long TimerResetTick { get; set; }
    public long EndTick { get; set; }
    public DungeonState State { get; set; } = DungeonState.None;
    // Trigger dungeon variables (dungeon_variable): one set per dungeon run, shared by the lobby and every room map
    public readonly ConcurrentDictionary<int, int> Variables = [];
    // Safe revivals per character in this run (DungeonReviveCount); they stay when the player leaves and comes back
    public readonly ConcurrentDictionary<long, int> Revivals = [];
    // Dark Stream (Dark Descent): the number of rounds the run has (dark_stream_start_game); 0 = not a Dark Stream run
    public int DarkStreamRounds { get; set; }
    // The room timer (RoomTimerPacket), one per run: on from the run's start for DurationTick, or from dungeon_reset_time;
    // ModifyRoomTime and an NPC's extendRoomTime change its length, dungeon_stop_timer and dungeon_close_timer turn it off.
    public bool TimerOn { get; set; }
    public int TimerDuration { get; set; }
    // The room timer and dungeon_play_time count from here
    public long TimerStartTick => TimerResetTick > 0 ? TimerResetTick : StartTick;

    public DungeonRoomRecord(DungeonRoomMetadata metadata) {
        Metadata = metadata;
    }

    // The run starts when its first player is sent to the first room.
    public void Start(long tick) {
        StartTick = tick;
        TimerDuration = Metadata.DurationTick;
        TimerOn = TimerDuration > 0;
    }

    // dungeon_timeout: the timer is on and has run out.
    public bool TimedOut(long tick) => TimerOn && tick - TimerStartTick >= TimerDuration;
}
