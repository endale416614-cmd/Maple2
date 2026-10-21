using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Game.Trigger;

public partial class TriggerContext {
    public void AddCinematicTalk(int npcId, string illustId, string msg, int duration, Align align, int delayTick) {
        DebugLog("[AddCinematicTalk] npcId:{NpcId}, illustId:{IllustrationId}, msg:{Message}, duration:{Duration}, align:{Align}, delayTick:{Delay}",
            npcId, illustId, msg, duration, align, delayTick);
        Broadcast(CinematicPacket.Talk(npcId, illustId, msg, duration, align));
    }

    public void RemoveCinematicTalk() {
        if (TraceEnabled) DebugLog("[RemoveCinematicTalk]");
        Broadcast(CinematicPacket.RemoveTalk());
    }

    public void CloseCinematic() {
        if (TraceEnabled) DebugLog("[CloseCinematic]");
        // guessing
        Broadcast(CinematicPacket.HideUi());
    }

    public void PlaySceneMovie(string fileName, int movieId, string skipType) {
        if (TraceEnabled) DebugLog("[PlaySceneMovie] fileName:{FileName}, movieId:{MovieId}, skipType:{SkipType}", fileName, movieId, skipType);
        Broadcast(TriggerPacket.UiStartMovie(fileName, movieId));
    }

    public void SetCinematicIntro(string text) {
        if (TraceEnabled) DebugLog("[SetCinematicIntro] text:{Text}", text);
        Broadcast(CinematicPacket.Intro(text));
    }

    public void SetOnetimeEffect(int id, bool enabled, string path) {
        if (TraceEnabled) DebugLog("[SetOnetimeEffect] id:{Id}, enabled:{Enabled}, path:{Path}", id, enabled, path);
        Broadcast(CinematicPacket.OneTimeEffect(id, enabled, path));
    }

    public void SetCinematicUi(int type, string script, bool arg3) {
        if (TraceEnabled) DebugLog("[SetCinematicUI] type:{Type}, script:{Script}, arg3:{Arg3}", type, script, arg3);
        switch (type) {
            case 0:
                Broadcast(CinematicPacket.ToggleUi(false));
                return;
            case 1:
                Broadcast(CinematicPacket.ToggleUi(true));
                return;
            case 2:
                Broadcast(CinematicPacket.HideUi());
                return;
            case 3:
                Broadcast(CinematicPacket.LetterboxTransition(script));
                return;
            case 4:
                Broadcast(CinematicPacket.FadeTransition(script));
                return;
            case 5:
                Broadcast(CinematicPacket.HorizontalTransition(script));
                return;
            case 6:
                Broadcast(CinematicPacket.VerticalTransition(script));
                return;
            case 7: // many
                return;
            case 9:
                Broadcast(CinematicPacket.Opening(script, arg3));
                return;
        }
    }

    public void SetSceneSkip(string state, string nextState) {
        if (TraceEnabled) WarnLog("[SetSceneSkip] state:{State}, nextState:{NextState}", state, nextState);
        skipState = owner.GetState(state);
        Broadcast(CinematicPacket.SetSkipScene(nextState));
    }

    public void SetSkip(string state) {
        if (TraceEnabled) WarnLog("[SetSkip] state:{State}", nameof(state));
        skipState = owner.GetState(state);
        Broadcast(CinematicPacket.SetSkipState(""));
    }

    public void AddBalloonTalk(int spawnId, string msg, int duration, int delayTick, int npcId) {
        if (TraceEnabled) DebugLog("[AddBalloonTalk] spawnId:{SpawnId}, msg:{Message}, duration:{Duration}, delayTick:{Delay}, npcId:{NpcId}", spawnId, msg, duration, delayTick, npcId);
        if (spawnId == 0) {
            FieldPlayer? fieldPlayer = Field.Players.Values.FirstOrDefault();
            if (fieldPlayer == null) {
                if (TraceEnabled) WarnLog("[AddBalloonTalk] No players found in field");
                return;
            }

            Broadcast(CinematicPacket.BalloonTalk(false, fieldPlayer.ObjectId, msg, duration, delayTick));
            return;
        }

        FieldNpc? fieldNpc = Field.Npcs.Values.FirstOrDefault(npc => npc.SpawnPointId == spawnId);
        if (fieldNpc == null) {
            if (TraceEnabled) WarnLog("[AddBalloonTalk] No NPC with spawnId:{SpawnId} found in field", spawnId);
            return;
        }

        Broadcast(CinematicPacket.BalloonTalk(true, fieldNpc.ObjectId, msg, duration, delayTick));
    }

    public void RemoveBalloonTalk(int spawnId) {
        if (TraceEnabled) DebugLog("[RemoveBalloonTalk] spawnId:{SpawnId}", spawnId);
        foreach (FieldNpc npc in Field.Npcs.Values) {
            if (spawnId == 0 || npc.SpawnPointId == spawnId) {
                Broadcast(CinematicPacket.RemoveBalloonTalk(npc.ObjectId));
            }
        }
    }

    public void ShowCaption(
        string type,
        string title,
        string script,
        Align align,
        float offsetRateX,
        float offsetRateY,
        int duration,
        float scale
    ) {
        DebugLog("[ShowCaption] type:{Type}, title:{Title}, script:{Script} align:{Align}, offsetRateX:{OffsetRateX}, offsetRateY:{OffsetRateY}, duration:{Duration}, scale:{Scale}",
            type, title, script, align, offsetRateX, offsetRateY, duration, scale);
        // Not sure if this is correct, but I only found packets where NameCaption and offsetRateX and offsetRateY are both not 0 or both 0. even though in the trigger only one of them is set to a value.
        if (type == "NameCaption" && (offsetRateX == 0 || offsetRateY == 0)) {
            offsetRateX = offsetRateY = 0;
        }
        Broadcast(CinematicPacket.Caption(type, title, script, align, offsetRateX, offsetRateY, duration, scale));
    }
}
