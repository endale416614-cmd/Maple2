using Maple2.Database.Context;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Maple2.Model.Game;
using Maple2.Model.Game.Dungeon;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Tools;
using Maple2.Tools.Scheduler;
using Serilog;
using Serilog.Core;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext : ITriggerContext {
    private readonly FieldTrigger owner;
    private readonly ILogger logger = Log.Logger.ForContext<TriggerContext>();

    private FieldManager Field => owner.Field;
    private TriggerCollection Objects => owner.Field.TriggerObjects;

    private float currentRandom = float.MaxValue;

    // Skip state class reference, must instantiate before using.
    private TriggerState? skipState;
    public readonly EventQueue Events;
    public long StartTick;

    public TriggerContext(FieldTrigger owner) {
        this.owner = owner;

        Events = new EventQueue(logger);
        Events.Start();
        StartTick = Environment.TickCount64;
    }

    public bool TryGetSkip([NotNullWhen(true)] out TriggerState? state) {
        if (skipState == null) {
            state = null;
            return false;
        }

        state = skipState;
        return true;
    }

    private void Broadcast(ByteWriter packet) => Field.Broadcast(packet);

    private readonly Dictionary<string, string> lastDebugKeys = new();
    private int lastAliveCount = -1;
    private static readonly ConcurrentDictionary<string, byte> loggedFunctions = new();

    /// <summary>
    /// Trigger tracing for quest/dungeon debugging: OFFLINE_TRIGGER_TRACE=true logs every state change and action.
    /// (Previously only available in builds compiled with TRIGGER_DEBUG.)
    /// </summary>
    internal static readonly bool TraceEnabled =
        OfflineProfile.Flag("OFFLINE_TRIGGER_TRACE", false);

    [MessageTemplateFormatMethod("messageTemplate")]
    internal void DebugLog(string messageTemplate, params object[] args) {
        if (TraceEnabled) LogOnce(logger.Information, messageTemplate, args);
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    internal void WarnLog(string messageTemplate, params object[] args) {
        if (TraceEnabled) LogOnce(logger.Warning, messageTemplate, args);
    }

    [MessageTemplateFormatMethod("messageTemplate")]
    internal void ErrorLog(string messageTemplate, params object[] args) {
        if (TraceEnabled) {
            LogOnce(logger.Error, messageTemplate, args);
        } else {
            // Without TriggerTrace each stub is still logged once per map, so problem reports name what the player met.
            int end = messageTemplate.IndexOf(']');
            string function = end > 1 ? messageTemplate[1..end] : messageTemplate;
            string key = $"{Field.MapId} {function}";
            if (loggedFunctions.TryAdd(key, 0)) {
                logger.Warning("[{MapId}] map script function {Function} is not implemented offline", Field.MapId, function);
            }
        }
    }

    private void LogOnce(Action<string, object[]> logAction, string messageTemplate, params object[] args) {
        // Remembered per message template: a state polling several conditions each tick alternates templates,
        // which a single "last message" defeated (every poll was logged).
        string key = string.Join(", ", args);
        if (lastDebugKeys.TryGetValue(messageTemplate, out string? last) && last == key) {
            return;
        }

        logAction($"[{Field.MapId}] {owner.Value.Name} {messageTemplate}", args);
        lastDebugKeys[messageTemplate] = key;
    }

    // Accessors
    public bool ShadowExpeditionPoints(int score) {
        ErrorLog("[GetShadowExpeditionPoints]");
        return 0 >= score;
    }

    public bool DungeonVariable(int id, int value) {
        if (TraceEnabled) DebugLog("[GetDungeonVariable] id:{Id}, value:{Value}", id, value);
        // unset variables read as 0, so scripts can also wait for value="0"
        return Field is DungeonFieldManager dungeonField && dungeonField.DungeonRoomRecord.Variables.GetValueOrDefault(id) == value;
    }

    public bool NpcDamage(int spawnPointId, float damageRate, OperatorType operatorType) {
        if (TraceEnabled) DebugLog("[NpcDamage] spawnPointId:{Id}, damageRate:{Rate}, operatorType:{Operator}", spawnPointId, damageRate, operatorType);
        FieldNpc? npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnPointId);
        long maxHealth = npc?.Stats.Values[Maple2.Model.Enum.BasicAttribute.Health].Total ?? 0;
        return DamageRateMatches(npc == null ? 1 : npc.DamageTaken, npc == null ? 1 : maxHealth, damageRate, operatorType);
    }

    public static bool DamageRateMatches(long damageTaken, long maxHealth, float damageRate, OperatorType operatorType) {
        float rate = maxHealth > 0 ? (float) damageTaken / maxHealth : 0;
        return operatorType switch {
            OperatorType.Greater => rate > damageRate,
            OperatorType.GreaterEqual => rate >= damageRate,
            OperatorType.Equal => Math.Abs(rate - damageRate) < 0.0001f,
            OperatorType.LessEqual => rate <= damageRate,
            OperatorType.Less => rate < damageRate,
            _ => false,
        };
    }

    // Boss-phase scripts wait on it: 80 "health at or below X%" steps in 22 maps (BT-23: it was a stub at 100%).
    public bool NpcHp(int spawnPointId, bool isRelative, int value, CompareType compareType) {
        var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnPointId && !npc.IsDead);
        long health = 0; // no such NPC (not spawned yet, or dead and removed): as if dead
        if (npc != null) {
            var stat = npc.Stats.Values[Maple2.Model.Enum.BasicAttribute.Health];
            health = isRelative ? (stat.Total > 0 ? stat.Current * 100 / stat.Total : 0) : stat.Current;
        }
        if (TraceEnabled) DebugLog("[GetNpcHpRate] spawnPointId:{Id}, isRelative:{IsRelative}, value:{Value}, compareType:{CompareType}, health:{Health}", spawnPointId, isRelative, value, compareType, health);
        return compareType switch {
            CompareType.lower => health < value,
            CompareType.lowerEqual => health <= value,
            CompareType.higher => health > value,
            CompareType.higherEqual => health >= value,
            _ => false,
        };
    }

    public bool DungeonId(int dungeonId) {
        if (TraceEnabled) DebugLog("[GetDungeonId] dungeonId:{DungeonId}, fieldDungeonId:{FieldDungeonId}", dungeonId, Field.DungeonId);
        return dungeonId != 0 && Field.DungeonId == dungeonId;
    }

    public bool DungeonLevel(int level) {
        Maple2.Model.Enum.DungeonGroupType? groupType = (Field as DungeonFieldManager)?.DungeonMetadata.GroupType;
        if (TraceEnabled) DebugLog("[GetDungeonLevel] level:{Level}, groupType:{GroupType}", level, groupType ?? Maple2.Model.Enum.DungeonGroupType.none);
        // level 2 is the raid boss, 3 the chaos-raid boss
        return groupType != null && level == (groupType == Maple2.Model.Enum.DungeonGroupType.chaosRaid ? 3 : 2);
    }

    public bool DungeonMaxUserCount(int value) {
        if (TraceEnabled) DebugLog("[GetDungeonMaxUserCount]");
        // solo on purpose, the scripts wait for the room's max party size before they start
        return value == 1;
    }

    public bool DungeonRound(int round) {
        if (TraceEnabled) DebugLog("[GetDungeonRoundsRequired] round:{Round}", round);
        // offline every Colosseum round is open (the original limited rounds by item score)
        return round > 0;
    }

    public bool CheckUser(bool negate) {
        if (negate) {
            return Field.Players.IsEmpty;
        }
        return !Field.Players.IsEmpty;
    }

    public bool UserCount(int count) {
        return Field.Players.Count == count;
    }

    public bool CountUsers(int boxId, int userTagId, int minUsers, OperatorType operatorType, bool negate) {
        if (TraceEnabled) DebugLog("[GetUserCount] boxId:{BoxId}, userTagId:{TagId}", boxId, userTagId);
        if (!Objects.Boxes.TryGetValue(boxId, out TriggerBox? box)) {
            return negate;
        }

        // /allboxes (test kit): the player counts as inside the box.
        int count;
        if (userTagId > 0) {
            count = Field.Players.Values.Count(player => player.TagId == userTagId && (box.Contains(player.Position) || Maple2.Server.Game.Util.OfflineCheats.IsInAllBoxes(player.Session.CharacterId)));
        } else {
            count = Field.Players.Values.Count(player => (box.Contains(player.Position) || Maple2.Server.Game.Util.OfflineCheats.IsInAllBoxes(player.Session.CharacterId)));
        }

        bool result = operatorType switch {
            OperatorType.Greater => count > minUsers,
            OperatorType.GreaterEqual => count >= minUsers,
            OperatorType.Equal => count == minUsers,
            OperatorType.LessEqual => count <= minUsers,
            OperatorType.Less => count < minUsers,
            _ => false,
        };
        return negate ? !result : result;
    }

    public bool NpcExtraData(int spawnId, string extraDataKey, int extraDataValue, OperatorType operatorType) {
        if (TraceEnabled) WarnLog("[GetNpcExtraData] spawnId:{SpawnId}, extraDataKey:{Key}, extraDataValue:{Value}, operatorType:{Operator}", spawnId, extraDataKey, extraDataValue, operatorType);
        var npc = Field.EnumerateNpcs().FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        if (npc is null) {
            return false;
        }
        int extraData = npc.AiExtraData.GetValueOrDefault(extraDataKey, 0);
        return operatorType switch {
            OperatorType.Greater => extraData > extraDataValue,
            OperatorType.GreaterEqual => extraData >= extraDataValue,
            OperatorType.Equal => extraData == extraDataValue,
            OperatorType.LessEqual => extraData <= extraDataValue,
            OperatorType.Less => extraData < extraDataValue,
            _ => false,
        };
    }

    public bool DungeonPlayTime(int playSeconds, OperatorType operatorType) {
        if (TraceEnabled) DebugLog("[GetDungeonPlayTime] playSeconds:{PlaySeconds}, operator:{Operator}", playSeconds, operatorType);
        if (Field is not DungeonFieldManager dungeonField) {
            return false;
        }
        return DungeonPlayTimeMatches(dungeonField.DungeonRoomRecord, Field.FieldTick, playSeconds, operatorType);
    }

    // Seconds since the last dungeon_reset_time, or since the run started. "Equal" only appears as the time-up branch
    // (02020101/02020111 "Equal 420" next to "Less 420"), so it means "reached": an exact-second match could be missed.
    public static bool DungeonPlayTimeMatches(DungeonRoomRecord record, long fieldTick, int playSeconds, OperatorType operatorType) {
        long startTick = record.TimerResetTick > 0 ? record.TimerResetTick : record.StartTick;
        if (startTick <= 0) {
            return false;
        }

        long elapsed = (fieldTick - startTick) / 1000;
        return operatorType switch {
            OperatorType.Greater => elapsed > playSeconds,
            OperatorType.GreaterEqual => elapsed >= playSeconds,
            OperatorType.Equal => elapsed >= playSeconds,
            OperatorType.LessEqual => elapsed <= playSeconds,
            OperatorType.Less => elapsed < playSeconds,
            _ => false,
        };
    }

    // Scripts seem to just check if this is "Fail"
    public bool DungeonState(string checkState) {
        ErrorLog("[GetDungeonState]");
        return checkState == "";
    }

    public bool DungeonFirstUserMissionScore(int score, OperatorType operatorType) {
        ErrorLog("[GetDungeonFirstUserMissionScore]");
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    public bool ScoreBoardScore(int score, OperatorType operatorType) {
        ErrorLog("[GetScoreBoardScore]");
        return operatorType switch {
            OperatorType.Greater => score > 0,
            OperatorType.GreaterEqual => score >= 0,
            OperatorType.Equal => score == 0,
            OperatorType.LessEqual => score <= 0,
            OperatorType.Less => score < 0,
            _ => false,
        };
    }

    public bool UserValue(string key, int value, bool negate) {
        if (TraceEnabled) WarnLog("[GetUserValue] key:{Key}", key);
        int userValue = Field.UserValues.GetValueOrDefault(key, 0);
        if (negate) {
            return userValue != value;
        }
        return userValue == value;
    }

    public void DebugString(string value, string feature) {
        logger.Debug("{Value} [{Feature}]", value, feature);
    }

    public void WriteLog(string logName, string @event, int triggerId, string subEvent, int level) {
        logger.Information("{Log}: {Event}, {TriggerId}, {SubEvent}, {Level}", logName, @event, triggerId, subEvent, level);
    }

    #region Conditions
    public bool DayOfWeek(int[] dayOfWeeks, string description, bool negate) {
        if (negate) {
            return !dayOfWeeks.Contains((int) GameClock.UtcNow.DayOfWeek + 1);
        }
        return dayOfWeeks.Contains((int) GameClock.UtcNow.DayOfWeek + 1);
    }

    public bool RandomCondition(float rate, string description) {
        if (rate < 0f || rate > 100f) {
            LogOnce(logger.Error, "[RandomCondition] Invalid rate: {Rate}", rate);
            return false;
        }

        if (currentRandom >= 100f) {
            currentRandom = Random.Shared.NextSingle() * 100;
        }

        currentRandom -= rate;
        if (currentRandom > rate) {
            return false;
        }

        currentRandom = float.MaxValue; // Reset
        return true;
    }

    public bool WaitAndResetTick(int waitTick) {
        long tickNow = Environment.TickCount64;
        if (tickNow <= StartTick + waitTick) {
            return false;
        }

        StartTick = tickNow;
        return true;
    }

    public bool WaitTick(int waitTick) {
        return Environment.TickCount64 > StartTick + waitTick;
    }
    #endregion
}
