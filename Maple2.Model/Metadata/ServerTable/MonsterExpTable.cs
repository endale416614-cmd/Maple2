namespace Maple2.Model.Metadata;

/// <summary>table/Server/monsterExpTable.xml: the EXP a monster of a level gives, by rank, by level.</summary>
public record MonsterExpTable(IReadOnlyDictionary<int, MonsterExpTable.Entry> Entries) : ServerTable {
    public record Entry(
        long Common,
        long Elite,
        long DungeonElite,
        long Boss,
        long DungeonBoss);
}
