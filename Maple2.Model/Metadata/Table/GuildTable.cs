using Maple2.Model.Enum;

namespace Maple2.Model.Metadata;

public record GuildTable(
    IReadOnlyDictionary<int, IReadOnlyDictionary<short, GuildTable.Buff>> Buffs,
    IReadOnlyDictionary<int, IReadOnlyDictionary<int, GuildTable.House>> Houses,
    IReadOnlyDictionary<GuildNpcType, IReadOnlyDictionary<short, GuildTable.Npc>> Npcs,
    IReadOnlyDictionary<short, GuildTable.Property> Properties) : Table {

    // Experience is the guild EXP that finishes a level, so the level is the lowest one above the EXP; past the last level it stays there.
    public Property PropertyFor(int experience) {
        return Properties.Values.Where(property => property.Experience > experience).MinBy(property => property.Experience)
               ?? Properties.Values.MaxBy(property => property.Experience)!;
    }

    // guildbuff.xml numbers guild-wide buffs 1-4 and personal buffs 10001-10005, and every row has type 1, so the id is the only mark.
    public Buff? PersonalBuff(int buffId, int level) {
        return buffId > 10000 && Buffs.TryGetValue(buffId, out IReadOnlyDictionary<short, Buff>? levels) && levels.TryGetValue((short) level, out Buff? row) ? row : null;
    }

    public record Buff(
        int Id,
        short Level,
        short RequireLevel,
        int Cost,
        int UpgradeCost,
        int Duration) {
        // guildbuff duration is in minutes: the guild-wide 10080 is their effects' 7-day DurationTick.
        public int DurationMs() => Duration * 60_000;
    }

    public record House(
        int MapId,
        int RequireLevel,
        int UpgradeCost,
        int ReThemeCost,
        int[] Facilities);

    public record Npc(
        GuildNpcType Type,
        short Level,
        int RequireGuildLevel,
        int RequireHouseLevel,
        int UpgradeCost);

    public record Property(
        short Level,
        int Experience,
        byte Capacity,
        long FundMax,
        int DonateMax,
        int CheckInExp,
        int WinMiniGameExp,
        int LoseMiniGameExp,
        int RaidExp,
        int CheckInFund,
        int WinMiniGameFund,
        int LoseMiniGameFund,
        int RaidFund,
        float CheckInPlayerExpRate,
        float DonatePlayerExpRate,
        int CheckInCoin,
        int DonateCoin,
        int WinMiniGameCoin,
        int LoseMiniGameCoin);
}
