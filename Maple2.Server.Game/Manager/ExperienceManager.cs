using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Manager;

public sealed class ExperienceManager {
    private readonly GameSession session;

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    private ConstantsTable Constants => session.ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    private long Exp {
        get => session.Player.Value.Character.Exp;
        set => session.Player.Value.Character.Exp = value;
    }
    private long RestExp {
        get => session.Player.Value.Character.RestExp;
        set => session.Player.Value.Character.RestExp = value;
    }

    private short Level {
        get => session.Player.Value.Character.Level;
        set => session.Player.Value.Character.Level = value;
    }

    public int PrestigeLevel {
        get => session.Player.Value.Account.PrestigeLevel;
        set => session.Player.Value.Account.PrestigeLevel = value;
    }

    public int PrestigeLevelsGained {
        get => session.Player.Value.Account.PrestigeLevelsGained;
        set => session.Player.Value.Account.PrestigeLevelsGained = value;
    }

    public long PrestigeExp {
        get => session.Player.Value.Account.PrestigeExp;
        set => session.Player.Value.Account.PrestigeExp = value;
    }

    public long PrestigeCurrentExp {
        get => session.Player.Value.Account.PrestigeCurrentExp;
        set => session.Player.Value.Account.PrestigeCurrentExp = value;
    }

    public IList<PrestigeMission> PrestigeMissions => session.Player.Value.Account.PrestigeMissions;
    public IList<int> PrestigeRewardsClaimed => session.Player.Value.Account.PrestigeRewardsClaimed;

    private int ChainKillCount { get; set; }
    private long lastKillTick;

    public ExperienceManager(GameSession session) {
        this.session = session;
        Init();
    }

    private void Init() {
        if (session.Player.Value.Account.PrestigeMissions.Count == 0) {
            foreach ((int missionId, PrestigeMissionMetadata _) in session.TableMetadata.PrestigeMissionTable.Entries) {
                session.Player.Value.Account.PrestigeMissions.Add(new PrestigeMission(missionId));
            }
        }
    }

    /// <summary>The kill chain after one more kill: Lua.CalcKillCount, with a gap of 5 s or more starting a new chain.</summary>
    public static int NextChain(int chain, int myLevel, int npcLevel, long elapsedMs) {
        return elapsedMs >= 5000 ? 1 : Lua.CalcKillCount(myLevel, npcLevel, (int) elapsedMs, chain);
    }

    /// <summary>A reward's relative exp this server pays. A flat Exp goes first, and epicQuest has no factor.</summary>
    public static bool PaysRelativeExp(ExpType type) => type is ExpType.quest or ExpType.mission or ExpType.dailymission or ExpType.dailymissionLevelUp or ExpType.questSkyFortress;

    /// <summary>Counts a kill in the chain (Unstoppable!) and returns the chain's length.</summary>
    public int ChainKill(int npcLevel, long tickCount) {
        ChainKillCount = NextChain(ChainKillCount, Level, npcLevel, tickCount - lastKillTick);
        lastKillTick = tickCount;
        return ChainKillCount;
    }

    public void ResetChainKill() => ChainKillCount = 0;

    public void OnKill(IActor npc) {
        if (npc is not FieldNpc) {
            return;
        }
        FieldNpc fieldNpc = (npc as FieldNpc)!;
        // TODO: Check if there are level requirements for Chain Kill Count to count ?
        ChainKillCount++;
        float expRate = Lua.CalcKillCountBonusExpRate(ChainKillCount);

        long expGained = fieldNpc.Value.Metadata.Basic.CustomExp;
        if (fieldNpc.Value.Metadata.Basic.CustomExp < 0) {
            if (MonsterExp(session.ServerTableMetadata.MonsterExpTable, fieldNpc.Value.Metadata.Basic.Class, fieldNpc.Value.Metadata.Basic.Level) is not { } tableExp) {
                return;
            }
            expGained = tableExp;
        }
        expGained += GetRestExp((long) (expGained * expRate));
        LevelUp();
        session.Send(ExperienceUpPacket.Add(expGained, Exp, RestExp, ExpMessageCode.s_msg_take_exp, npc.ObjectId));
    }

    private long GetRestExp(long expGained) {
        long addedRestExp = Math.Min(RestExp, (long) (expGained * (Constants.RestExpAcquireRate / 10000.0f))); // convert int to a percentage
        RestExp = Math.Max(0, RestExp - addedRestExp);
        Exp += expGained;
        return addedRestExp;
    }

    public long AddExp(long expGained, ExpMessageCode message = ExpMessageCode.s_msg_take_exp) {
        if (expGained <= 0) {
            return 0;
        }
        expGained += GetRestExp(expGained);
        LevelUp();
        AddPrestigeExp(message.Type());
        session.Send(ExperienceUpPacket.Add(expGained, Exp, RestExp, message));
        session.ConditionUpdate(ConditionType.exp, counter: expGained);
        return expGained;
    }

    public long AddExp(ExpType expType, float modifier = 1f, long additionalExp = 0) {
        if (session.Field is null) return 0;
        if (!session.TableMetadata.CommonExpTable.Entries.TryGetValue(expType, out CommonExpTable.Entry? entry)
            || !session.TableMetadata.ExpTable.ExpBase.TryGetValue(entry.ExpTableId, out IReadOnlyDictionary<int, long>? expBase)) {
            return 0;
        }

        long expValue;
        switch (expType) {
            case ExpType.fishing:
            case ExpType.musicMastery1:
            case ExpType.musicMastery2:
            case ExpType.musicMastery3:
            case ExpType.musicMastery4:
            case ExpType.manufacturing:
            case ExpType.gathering:
            case ExpType.arcade:
            case ExpType.expDrop:
            case ExpType.miniGame:
            case ExpType.userMiniGame:
            case ExpType.guildUserExp:
            case ExpType.quest:
            case ExpType.mission:
            case ExpType.dailymission:
            case ExpType.dailymissionLevelUp:
            case ExpType.questSkyFortress:
                if (!expBase.TryGetValue(Level, out expValue)) {
                    return 0;
                }
                break;
            case ExpType.taxi:
            case ExpType.mapCommon:
            case ExpType.mapHidden:
            case ExpType.telescope:
            case ExpType.rareChestFirst:
            case ExpType.rareChest:
            case ExpType.normalChest:
                int fieldLevel = session.Field.Metadata.Drop.Level;
                int correctedLevel = fieldLevel > Level ? Level : fieldLevel;
                if (!expBase.TryGetValue(correctedLevel, out expValue)) {
                    return 0;
                }
                break;
            default:
                Log.Logger.Warning("Unhandled ExpType: {ExpType}", expType);
                return 0;
        }

        return AddExp((long) ((expValue * modifier) * entry.Factor) + additionalExp, expType.Message());
    }

    public void AddStaticExp(long amount) {
        if (amount <= 0) {
            return;
        }
        Exp += amount;
        session.Send(ExperienceUpPacket.Add(amount, Exp, RestExp, ExpMessageCode.s_msg_take_exp));
        session.ConditionUpdate(ConditionType.exp, counter: amount);
        LevelUp();
    }

    public void AddMobExp(int moblevel, int npcClass, float modifier = 1f, long additionalExp = 0) {
        if (MonsterExp(session.ServerTableMetadata.MonsterExpTable, npcClass, moblevel) is not { } expValue) {
            return;
        }

        AddExp((long) (expValue * modifier) + additionalExp, ExpType.monster.Message());
    }

    /// <summary>A monster's EXP by its NPC class and level (monsterExpTable): 3 and 7 boss, 4 and 6 dungeonBoss, any other common. The class match is a reconstruction.</summary>
    public static long? MonsterExp(MonsterExpTable table, int npcClass, int level) {
        if (!table.Entries.TryGetValue(level, out MonsterExpTable.Entry? entry)) {
            return null;
        }
        return npcClass switch {
            3 or 7 => entry.Boss,
            4 or 6 => entry.DungeonBoss,
            _ => entry.Common,
        };
    }

    public bool LevelUp() {
        int startLevel = Level;
        for (int level = startLevel; level < Constants.characterMaxLevel; level++) {
            if (!session.TableMetadata.ExpTable.NextExp.TryGetValue(level, out long expToNextLevel) || expToNextLevel > Exp) {
                break;
            }

            Exp -= expToNextLevel;
            Level++;
        }
        if (Level > startLevel) {
            session.Player.Flag |= PlayerObjectFlag.Level;
            session.Dungeon.UpdateDungeonEnterLimit();
            session.Field?.Broadcast(LevelUpPacket.LevelUp(session.Player));
            session.ConditionUpdate(ConditionType.level_up, codeLong: (int) session.Player.Value.Character.Job.Code(), targetLong: Level);
            session.ConditionUpdate(ConditionType.level, targetLong: Level);
            session.Config.UpdateDeathPenalty(0);
            session.Stats.Refresh(fill: true);

            session.PlayerInfo.SendUpdate(new PlayerUpdateRequest {
                AccountId = session.AccountId,
                CharacterId = session.CharacterId,
                Level = Level,
                Async = true,
            });
        }
        return startLevel != Level;
    }

    private void AddPrestigeExp(ExpType expType) {
        if (Level < Constants.AdventureLevelStartLevel) {
            return;
        }

        if (!session.ServerTableMetadata.PrestigeExpTable.Entries.TryGetValue(expType, out long amount)) {
            return;
        }

        if (PrestigeCurrentExp - PrestigeExp + (PrestigeLevelsGained * Constants.AdventureLevelLvUpExp) >= Constants.AdventureLevelLvUpExp) {
            amount = (long) (amount * Constants.AdventureLevelFactor);
        }

        PrestigeCurrentExp = Math.Min(amount + PrestigeCurrentExp, long.MaxValue);

        int startLevel = PrestigeLevel;
        for (int level = startLevel; level < Constants.AdventureLevelLimit; level++) {
            if (Constants.AdventureLevelLvUpExp > PrestigeCurrentExp) {
                break;
            }

            PrestigeCurrentExp -= Constants.AdventureLevelLvUpExp;
            PrestigeLevel++;
        }
        session.Send(PrestigePacket.AddExp(PrestigeCurrentExp, amount));
        if (PrestigeLevel > startLevel) {
            PrestigeLevelUp(PrestigeLevel - startLevel);
        }
    }

    public void PrestigeLevelUp(int amount = 1) {
        PrestigeLevel = Math.Clamp(PrestigeLevel + amount, amount, Constants.AdventureLevelLimit);
        PrestigeLevelsGained += amount;
        session.ConditionUpdate(ConditionType.adventure_level, counter: amount);
        session.ConditionUpdate(ConditionType.adventure_level_up, counter: amount);
        foreach (PrestigeMission mission in PrestigeMissions) {
            mission.GainedLevels += amount;
        }

        for (int i = 0; i < amount; i++) {
            Item? item = session.Field?.ItemDrop.CreateItem(Constants.AdventureLevelLvUpRewardItem);
            if (item == null) {
                break;
            }

            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.MailItem(item);
            }
        }

        session.Send(PrestigePacket.LevelUp(session.Player.ObjectId, PrestigeLevel));
        session.Send(PrestigePacket.LoadMissions(session.Player.Value.Account));
        CheckPrestigeReward();
        RefreshPrestigeBuffs();
    }

    /// <summary>The level of a prestige ability's effect, 0 below its required level: one more every Interval levels, up to MaxCount. Golden: 1-Tria.msb, prestige level 402.</summary>
    public static short PrestigeAbilityLevel(int prestigeLevel, int requiredLevel, int interval, int maxCount) {
        if (prestigeLevel < requiredLevel) {
            return 0;
        }

        int steps = interval > 0 ? (prestigeLevel - requiredLevel) / interval : 0;
        return (short) Math.Min(steps + 1, maxCount);
    }

    /// <summary>The prestige level's abilities as effects, in the captures' order. An effect already held at its level stays as it is.</summary>
    public void RefreshPrestigeBuffs() {
        if (session.Field == null) {
            return;
        }

        foreach (PrestigeLevelAbilityMetadata ability in session.TableMetadata.PrestigeLevelAbilityTable.Entries.Values.OrderBy(ability => ability.RequiredLevel)) {
            short level = PrestigeAbilityLevel(PrestigeLevel, ability.RequiredLevel, ability.Interval, ability.MaxCount);
            if (level > 0) {
                session.Player.Buffs.AddBuff(session.Player, session.Player, ability.BuffId, level, session.Field.FieldTick);
            }
        }
    }

    private void CheckPrestigeReward() {
        if (!session.TableMetadata.PrestigeLevelRewardTable.Entries.TryGetValue(PrestigeLevel, out PrestigeLevelRewardMetadata? metadata)) {
            return;
        }

        if (metadata.Type != PrestigeAwardType.statPoint) {
            return;
        }

        session.Config.AddStatPoint(AttributePointSource.Prestige, metadata.Value);
    }
}
