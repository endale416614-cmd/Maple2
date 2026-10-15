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
    // Dark Stream (Dark Descent): the number of rounds the run has (dark_stream_start_game); 0 = not a Dark Stream run
    public int DarkStreamRounds { get; set; }

    public DungeonRoomRecord(DungeonRoomMetadata metadata) {
        Metadata = metadata;
    }
}
