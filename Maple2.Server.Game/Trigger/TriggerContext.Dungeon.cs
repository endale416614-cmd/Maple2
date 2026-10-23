using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void DungeonClear(string uiType) {
        if (TraceEnabled) DebugLog("[DungeonClear] uiType:{UiType}", uiType);
        if (Field is not DungeonFieldManager dungeonField) {
            return;
        }

        if (uiType == "None") {
            // Do not send dungeon clear UI
            return;
        }

        dungeonField.ChangeState(Maple2.Model.Enum.DungeonState.Clear);
    }

    public void DungeonClearRound(int round) {
        if (TraceEnabled) DebugLog("[DungeonClearRound] round:{Round}", round);

        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.Session.Dungeon.UserRecord is null) {
                continue;
            }

            player.Session.Dungeon.UserRecord.Round = round;
        }
    }

    public void DungeonCloseTimer() {
        ErrorLog("[DungeonCloseTimer]");
    }

    public void DungeonDisableRanking() {
        ErrorLog("[DungeonDisableRanking]");
    }

    public void DungeonEnableGiveUp(bool enabled) {
        if (TraceEnabled) DebugLog("[DungeonEnableGiveUp] enabled:{Enabled}", enabled);
        Field.Broadcast(DungeonMissionPacket.SetAbandon(enabled));
    }

    public void DungeonFail() {
        ErrorLog("[DungeonFail]");
    }

    public void DungeonMissionComplete(string feature, int missionId) {
        if (TraceEnabled) DebugLog("[DungeonMissionComplete] missionId:{MissionId}, feature:{Feature}", missionId, feature);

        foreach (FieldPlayer player in Field.Players.Values) {
            if (player.Session.Dungeon.UserRecord is null) {
                continue;
            }
            if (!player.Session.Dungeon.UserRecord.Missions.TryGetValue(missionId, out DungeonMission? missionRecord)) {
                continue;
            }

            missionRecord.Complete();
            player.Session.Send(DungeonMissionPacket.Update(missionRecord));
        }
    }

    public void DungeonMoveLapTimeToNow(int id) {
        ErrorLog("[DungeonMoveLapTimeToNow] id:{Id}", id);
    }

    public void DungeonResetTime(int seconds) {
        if (TraceEnabled) DebugLog("[DungeonResetTime] seconds:{Seconds}", seconds);
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.DungeonRoomRecord.TimerResetTick = Field.FieldTick;
        }
    }

    public void DungeonSetEndTime() {
        if (TraceEnabled) DebugLog("[DungeonSetEndTime]");
        // The fight's end: the result time leaves out the ending talk (19 maps); the last call before the clear wins.
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.DungeonRoomRecord.EndTick = Field.FieldTick;
        }
    }

    public void DungeonSetLapTime(int id, int lapTime) {
        ErrorLog("[DungeonSetLapTime] id:{Id}, lapTime:{LapTime}", id, lapTime);
    }

    public void DungeonStopTimer() {
        ErrorLog("[DungeonStopTimer]");
    }

    public void RandomAdditionalEffect(
        string target,
        int boxId,
        int spawnId,
        int targetCount,
        int tick,
        int waitTick,
        string targetEffect,
        int additionalEffectId
    ) {
        ErrorLog("[RandomAdditionalEffect] target:{Target}, boxId:{BoxId}, spawnId:{SpawnId}, targetCount:{Count}, targetEffect:{Effect}, additionalEffectId:{Id}",
            target, boxId, spawnId, targetCount, targetEffect, additionalEffectId);
    }

    public void SetDungeonVariable(int varId, int value) {
        if (TraceEnabled) DebugLog("[SetDungeonVariable] varId:{VarId}, value:{Value}", varId, value);
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.DungeonRoomRecord.Variables[varId] = value;
        }
    }

    public void SetUserValueFromDungeonRewardCount(string key, int dungeonRewardId) {
        ErrorLog("[SetUserValueFromDungeonRewardCount] key:{Key}, dungeonRewardId:{RewardId}", key, dungeonRewardId);
    }

    public void StartTutorial() {
        ErrorLog("[StartTutorial]");
    }

    #region DarkStream
    public void DarkStreamSpawnMonster(int[] spawnIds, int score) {
        if (TraceEnabled) DebugLog("[DarkStreamSpawnMonster] spawnIds:{SpawnIds}, score:{Score}", spawnIds, score);
        SpawnMonster(spawnIds, true, 0);
    }

    public void DarkStreamStartGame(int round) {
        if (TraceEnabled) DebugLog("[DarkStreamStartGame] rounds:{Round}", round);
        if (Field is DungeonFieldManager dungeonField) {
            dungeonField.DungeonRoomRecord.DarkStreamRounds = round;
        }
    }

    public void DarkStreamStartRound(int round, int uiDuration, int damagePenalty) {
        // No-op on purpose: the script's own 3 s timer starts the round; the penalty belongs to a Dark Stream score the
        // offline server does not keep.
        if (TraceEnabled) DebugLog("[DarkStreamStartRound] round:{Round}", round);
    }

    public void DarkStreamClearRound(int round) {
        if (TraceEnabled) DebugLog("[DarkStreamClearRound] round:{Round}", round);
        DungeonClearRound(round);
        // The Dark Stream scripts have no dungeon_clear: the run is cleared with its last round (offline edition)
        if (Field is DungeonFieldManager dungeonField && dungeonField.DungeonRoomRecord.DarkStreamRounds > 0
            && round >= dungeonField.DungeonRoomRecord.DarkStreamRounds && dungeonField.DungeonRoomRecord.State != Maple2.Model.Enum.DungeonState.Clear) {
            dungeonField.ChangeState(Maple2.Model.Enum.DungeonState.Clear);
        }
    }
    #endregion

    #region ShadowExpedition
    public void ShadowExpeditionOpenBossGauge(int maxGaugePoint, string title) {
        ErrorLog("[ShadowExpeditionOpenBossGauge]");
    }

    public void ShadowExpeditionCloseBossGauge() {
        ErrorLog("[ShadowExpeditionCloseBossGauge]");
    }
    #endregion

    #region Conditions
    public bool CheckDungeonLobbyUserCount(bool negate) {
        if (TraceEnabled) DebugLog("[CheckDungeonLobbyUserCount]");
        if (Field is not DungeonFieldManager dungeonField) {
            return negate;
        }

        if (negate) {
            return Field.Players.Values.Count < dungeonField.Size;
        }
        return Field.Players.Values.Count >= dungeonField.Size;
    }

    public bool DungeonTimeout() {
        ErrorLog("[DungeonTimeout]");
        return false;
    }

    public bool IsDungeonRoom(bool negate) {
        if (TraceEnabled) DebugLog("[IsDungeonRoom]");
        if (negate) {
            return Field is not DungeonFieldManager;
        }
        return Field is DungeonFieldManager;
    }

    public bool IsPlayingMapleSurvival(bool negate) {
        ErrorLog("[IsPlayingMapleSurvival]");
        return false;
    }
    #endregion
}
