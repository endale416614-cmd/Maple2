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
        if (TraceEnabled) ErrorLog("[DungeonCloseTimer]");
    }

    public void DungeonDisableRanking() {
        if (TraceEnabled) ErrorLog("[DungeonDisableRanking]");
    }

    public void DungeonEnableGiveUp(bool enabled) {
        if (TraceEnabled) DebugLog("[DungeonEnableGiveUp] enabled:{Enabled}", enabled);
        Field.Broadcast(DungeonMissionPacket.SetAbandon(enabled));
    }

    public void DungeonFail() {
        if (TraceEnabled) ErrorLog("[DungeonFail]");
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
        if (TraceEnabled) ErrorLog("[DungeonMoveLapTimeToNow] id:{Id}", id);
    }

    public void DungeonResetTime(int seconds) {
        if (TraceEnabled) ErrorLog("[DungeonResetTime] seconds:{Seconds}", seconds);
    }

    public void DungeonSetEndTime() {
        if (TraceEnabled) ErrorLog("[DungeonSetEndTime]");
    }

    public void DungeonSetLapTime(int id, int lapTime) {
        if (TraceEnabled) ErrorLog("[DungeonSetLapTime] id:{Id}, lapTime:{LapTime}", id, lapTime);
    }

    public void DungeonStopTimer() {
        if (TraceEnabled) ErrorLog("[DungeonStopTimer]");
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
        if (TraceEnabled) ErrorLog("[SetDungeonVariable] varId:{VarId}, value:{Value}", varId, value);
    }

    public void SetUserValueFromDungeonRewardCount(string key, int dungeonRewardId) {
        if (TraceEnabled) ErrorLog("[SetUserValueFromDungeonRewardCount] key:{Key}, dungeonRewardId:{RewardId}", key, dungeonRewardId);
    }

    public void StartTutorial() {
        if (TraceEnabled) ErrorLog("[StartTutorial]");
    }

    #region DarkStream
    public void DarkStreamSpawnMonster(int[] spawnIds, int score) {
        if (TraceEnabled) ErrorLog("[DarkStreamSpawnMonster]");
    }

    public void DarkStreamStartGame(int round) {
        if (TraceEnabled) ErrorLog("[DarkStreamStartGame]");
    }

    public void DarkStreamStartRound(int round, int uiDuration, int damagePenalty) {
        if (TraceEnabled) ErrorLog("[DarkStreamStartRound]");
    }

    public void DarkStreamClearRound(int round) {
        if (TraceEnabled) ErrorLog("[DarkStreamClearRound]");
    }
    #endregion

    #region ShadowExpedition
    public void ShadowExpeditionOpenBossGauge(int maxGaugePoint, string title) {
        if (TraceEnabled) ErrorLog("[ShadowExpeditionOpenBossGauge]");
    }

    public void ShadowExpeditionCloseBossGauge() {
        if (TraceEnabled) ErrorLog("[ShadowExpeditionCloseBossGauge]");
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
        if (TraceEnabled) ErrorLog("[DungeonTimeout]");
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
        if (TraceEnabled) ErrorLog("[IsPlayingMapleSurvival]");
        return false;
    }
    #endregion
}
