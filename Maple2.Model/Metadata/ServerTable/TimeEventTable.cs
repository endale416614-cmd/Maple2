namespace Maple2.Model.Metadata;

public record TimeEventTable(
    IReadOnlyDictionary<int, GlobalPortalMetadata> GlobalPortal,
    IReadOnlyDictionary<int, WorldBossMetadata> WorldBoss,
    IReadOnlyDictionary<int, FieldInteractEventMetadata>? FieldInteract = null) : ServerTable;

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
