namespace Maple2.Model.Metadata;

public record TimeEventTable(
    IReadOnlyDictionary<int, GlobalPortalMetadata> GlobalPortal,
    IReadOnlyDictionary<int, WorldBossMetadata> WorldBoss,
    IReadOnlyDictionary<int, FieldInteractEventMetadata>? FieldInteract = null,
    IReadOnlyDictionary<int, HiddenRealmPortalMetadata>? HiddenRealmPortal = null,
    IReadOnlyDictionary<int, TriggerValueEventMetadata>? TriggerValue = null) : ServerTable;

// Kritias field puzzles (time-event type 6): every CycleTime, the script TriggerId of the target maps gets Key = Value, and
// TimeEventLifeTime the seconds of LifeTime, which the script waits out (wait_seconds_user_value).
public record TriggerValueEventMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    int TriggerId,
    string Key,
    int Value);

// Field treasure chests: every CycleTime, Count objects appear on random spawn points of the target maps
// tagged with one of SpawnTags (or listed in SpawnPointIds) and disappear after LifeTime.
public record FieldInteractEventMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    int[] SpawnPointIds,
    string[] SpawnTags,
    int Count,
    int InteractId,
    string Model,
    string Asset,
    string Normal,
    string Reactable);

// Hidden Realm portals (type 2): in each cycle of CycleTime from StartTime, RoomCount portals open with Probability (of 100) at a random
// point of the cycle's first RandomTime, on spawns of the target maps tagged with one of SpawnTags, each for LifeTime, to a room of
// room_random group RoomId.
public record HiddenRealmPortalMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    string[] SpawnTags,
    int RoomId,
    int RoomCount) {
    // The start of the first cycle at or after time: StartTime plus whole cycles. Null without a cycle or past EndTime.
    public DateTime? NextCycle(DateTime time) {
        if (CycleTime <= TimeSpan.Zero) {
            return null;
        }
        long elapsed = Math.Max(0, (time - StartTime).Ticks);
        DateTime cycle = StartTime.AddTicks((elapsed + CycleTime.Ticks - 1) / CycleTime.Ticks * CycleTime.Ticks);
        return cycle > EndTime ? null : cycle;
    }
}

public record WorldBossMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    int[] TargetMapIds,
    int[] SpawnPointIds,
    int[] NpcIds,
    int Tag,
    bool Unique,
    bool IndividualChannelSpawn,
    float VariableCountByChannel,
    bool ScreenNotice,
    bool ChatNotice);

public record GlobalPortalMetadata(
    int Id,
    int Probability,
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan CycleTime,
    TimeSpan RandomTime,
    TimeSpan LifeTime,
    string PopupMessage,
    string SoundId,
    GlobalPortalMetadata.Field[] Entries) {
    // The first run at or after now: the start time while it lies ahead, else the start plus whole cycles.
    // Null without a cycle or when that run would be after the end time.
    public DateTime? FirstRun(DateTime now) {
        if (CycleTime <= TimeSpan.Zero || EndTime < now) {
            return null;
        }
        DateTime run = StartTime;
        while (run < now) {
            run += CycleTime;
        }
        return run > EndTime ? null : run;
    }

    public record Field(
        string Name,
        int MapId,
        int PortalId);
}
