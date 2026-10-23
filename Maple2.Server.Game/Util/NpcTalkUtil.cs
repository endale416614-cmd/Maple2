using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Util;

public static class NpcTalkUtil {
    /// <summary>Whether the NPC's shop is a game shop. Beauty NPCs (kinds 30-39) name shop_beauty ids, which the beauty window loads.</summary>
    public static bool OpensGameShop(int shopId, int kind) => shopId > 0 && kind is not (>= 30 and < 40);

    /// <summary>A player's talk with an NPC: what a click on the NPC does (NpcTalkHandler) and the map-script action talk_npc.</summary>
    public static void Talk(GameSession session, FieldNpc npc) {
        if (session.Field is null) return;

        int options = 0;
        var talkType = NpcTalkType.None;
        if (npc.Value.Metadata.Basic.ShopId > 0) {
            if (OpensGameShop(npc.Value.Metadata.Basic.ShopId, npc.Value.Metadata.Basic.Kind)) {
                session.Shop.Load(npc.Value.Metadata.Basic.ShopId, npc.Value.Id);
            }
            talkType |= NpcTalkType.Dialog;
            options++;
        }

        session.ScriptMetadata.TryGet(npc.Value.Id, out ScriptMetadata? metadata);
        ScriptState? scriptState = GetInitialScriptType(session, ScriptStateType.Script, metadata, npc.Value.Id);
        ScriptState? selectState = GetInitialScriptType(session, ScriptStateType.Select, metadata, npc.Value.Id);
        ScriptState? questState = GetInitialScriptType(session, ScriptStateType.Quest, metadata, npc.Value.Id);

        if (questState != null) {
            talkType |= NpcTalkType.Quest;
            options++;
        }

        if (scriptState?.Type == ScriptStateType.Job) {
            // Job script only counts as an additional option if quests are present.
            if (talkType.HasFlag(NpcTalkType.Quest)) {
                options++;
            }
            talkType |= NpcTalkType.Dialog;
        } else if (scriptState != null) {
            talkType |= NpcTalkType.Talk;
            options++;
        }

        if (options > 1 && selectState != null) {
            talkType |= NpcTalkType.Select;
        }

        switch (npc.Value.Metadata.Basic.Kind) {
            case >= 30 and < 40: // Beauty
            case 2: // Storage
            case 86: // TODO(BT-40): BlackMarket
            case 88: // TODO(BT-40): Birthday
            case 501:
                talkType |= NpcTalkType.Dialog;
                break;
            case >= 100 and <= 104: // Sky Fortress
            case >= 105 and <= 107: // Kritias
            case 108: // Humanitas
                talkType = NpcTalkType.Dialog;
                break;
        }

        // Determine which script to use.
        ScriptState? selectedState;
        if (npc.Value.Metadata.Basic.Kind is >= 100 and <= 108 || talkType.HasFlag(NpcTalkType.Select)) {
            selectedState = selectState;
        } else if (talkType.HasFlag(NpcTalkType.Quest)) {
            if (questState == null) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
            selectedState = questState;
            talkType = NpcTalkType.Quest;
            // now that quest is selected, change the metadata to the quest's metadata
            if (!session.ScriptMetadata.TryGet(session.Quest.GetAvailableQuests(npc.Value.Id).Keys.Min(), out metadata)) {
                session.Send(NpcTalkPacket.Close());
                npc.StopTalk();
                return;
            }
        } else if (scriptState == null && selectState == null) {
            if (talkType.HasFlag(NpcTalkType.Dialog)) {
                session.Send(NpcTalkPacket.Respond(npc, talkType, default));
                return;
            }
            session.Send(NpcTalkPacket.Close());
            npc.StopTalk();
            return;
        } else {
            selectedState = scriptState ?? selectState;
        }

        session.NpcScript = new NpcScriptManager(session, npc, metadata, selectedState, talkType);
        if (!session.NpcScript.BeginNpcTalk()) {
            session.NpcScript = null;
            return;
        }

        session.ConditionUpdate(ConditionType.dialogue, codeLong: npc.Value.Id);
        session.ConditionUpdate(ConditionType.talk_in, codeLong: npc.Value.Id);
    }

    public static ScriptState? GetInitialScriptType(GameSession session, ScriptStateType type, ScriptMetadata? metadata, int npcId) {
        switch (type) {
            case ScriptStateType.Script:
                if (metadata is null) {
                    return null;
                }
                // Check if player meets the requirements for the job script.
                ScriptState? jobScriptState = metadata.States.Values.FirstOrDefault(state => state.Type == ScriptStateType.Job);
                if (jobScriptState != null) {
                    if (!session.ServerTableMetadata.JobConditionTable.Entries.TryGetValue(metadata.Id, out JobConditionMetadata? jobCondition)) {
                        return jobScriptState;
                    }
                    if (MeetsJobCondition(session, jobCondition)) {
                        return jobScriptState;
                    }
                }

                List<ScriptState> scriptStates = [];
                session.ServerTableMetadata.ScriptConditionTable.Entries.TryGetValue(metadata.Id, out Dictionary<int, ScriptConditionMetadata>? scriptConditions);
                // Check if player meets the requirements for each pick script.
                foreach (ScriptState scriptState in metadata.States.Values.Where(state => state.Pick)) {
                    if (scriptConditions == null) {
                        scriptStates.Add(scriptState);
                        continue;
                    }

                    if (scriptState.JobCondition != null &&
                        scriptState.JobCondition != JobCode.None &&
                        scriptState.JobCondition != session.Player.Value.Character.Job.Code()) {
                        continue;
                    }

                    if (!scriptConditions.TryGetValue(scriptState.Id, out ScriptConditionMetadata? scriptCondition)) {
                        scriptStates.Add(scriptState);
                        continue;
                    }

                    if (scriptCondition.ConditionCheck(session)) {
                        scriptStates.Add(scriptState);
                    }
                }

                return scriptStates.Count == 0 ? null : scriptStates[Random.Shared.Next(scriptStates.Count)];
            case ScriptStateType.Quest:
                SortedDictionary<int, QuestMetadata> quests = session.Quest.GetAvailableQuests(npcId);
                if (quests.Count == 0) {
                    return null;
                }

                if (!session.ScriptMetadata.TryGet(quests.Keys.Min(), out ScriptMetadata? questMetadata)) {
                    return null;
                }
                return GetQuestScriptState(session, questMetadata, npcId);
            case ScriptStateType.Select:
                if (metadata is null) {
                    return null;
                }
                List<ScriptState> selectScriptStates = [];
                session.ServerTableMetadata.ScriptConditionTable.Entries.TryGetValue(metadata.Id, out Dictionary<int, ScriptConditionMetadata>? selectScriptConditions);
                foreach (ScriptState scriptState in metadata.States.Values.Where(state => state.Type == ScriptStateType.Select)) {
                    if (selectScriptConditions == null) {
                        selectScriptStates.Add(scriptState);
                        continue;
                    }

                    if (!selectScriptConditions.TryGetValue(scriptState.Id, out ScriptConditionMetadata? scriptCondition)) {
                        selectScriptStates.Add(scriptState);
                        continue;
                    }

                    if (scriptCondition.ConditionCheck(session)) {
                        selectScriptStates.Add(scriptState);
                    }
                }
                return selectScriptStates.Count == 0 ? null : selectScriptStates[Random.Shared.Next(selectScriptStates.Count)];
        }
        return null;
    }

    private static bool MeetsJobCondition(GameSession session, JobConditionMetadata jobCondition) {
        if (session.Field is null) return false;
        if (jobCondition.StartedQuestId > 0 &&
            (!session.Quest.TryGetQuest(jobCondition.StartedQuestId, out Quest? startedQuest) || startedQuest.State != QuestState.Started)) {
            return false;
        }

        if (jobCondition.CompletedQuestId > 0 &&
            (!session.Quest.TryGetQuest(jobCondition.CompletedQuestId, out Quest? completedQuest) || completedQuest.State != QuestState.Completed)) {
            return false;
        }

        if (jobCondition.JobCode != JobCode.None && session.Player.Value.Character.Job.Code() != jobCondition.JobCode) {
            return false;
        }

        // TODO: Maid checks

        if (jobCondition.BuffId > 0 && !session.Player.Buffs.HasBuff(jobCondition.BuffId)) {
            return false;
        }

        if (jobCondition.Mesos > 0 && session.Currency.Meso < jobCondition.Mesos) {
            return false;
        }

        if (jobCondition.Level > 0 && session.Player.Value.Character.Level < jobCondition.Level) {
            return false;
        }

        // TODO: Check if player is in home

        if (jobCondition.Guild && session.Player.Value.Character.GuildId == 0) {
            return false;
        }

        if (jobCondition.CompletedAchievement > 0 && !session.Achievement.HasAchievement(jobCondition.CompletedAchievement)) {
            return false;
        }

        // TODO: Check if it's the player's birthday

        if (jobCondition.MapId > 0 && session.Field?.MapId != jobCondition.MapId) {
            return false;
        }

        if (jobCondition.DeathPenalty && session.Config.DeathPenaltyEndTick < session.Field.FieldTick) {
            return false;
        }

        return true;
    }

    public static bool ConditionCheck(this ScriptConditionMetadata scriptCondition, GameSession session) {
        if (session.Field is null) return false;
        if (scriptCondition.JobCode.Count > 0 && !scriptCondition.JobCode.Contains(session.Player.Value.Character.Job.Code())) {
            return false;
        }

        foreach ((int questId, bool started) in scriptCondition.QuestStarted) {
            session.Quest.TryGetQuest(questId, out Quest? quest);
            if (started && quest is not { State: QuestState.Started }) {
                return false;
            }

            if (!started && quest is { State: QuestState.Started }) {
                return false;
            }
        }

        foreach ((int questId, bool completed) in scriptCondition.QuestCompleted) {
            session.Quest.TryGetQuest(questId, out Quest? quest);
            if (completed && quest is not { State: QuestState.Completed }) {
                return false;
            }

            if (!completed && quest is { State: QuestState.Completed }) {
                return false;
            }
        }

        foreach ((ItemComponent itemComponent, bool has) in scriptCondition.Items) {
            IEnumerable<Item> items = session.Item.Inventory.Find(itemComponent.ItemId, itemComponent.Rarity);
            int itemSum = items.Sum(item => item.Amount);
            if (has && itemSum < itemComponent.Amount) {
                return false;
            }

            if (!has && itemSum >= itemComponent.Amount) {
                return false;
            }
        }

        if (scriptCondition.Buff.Key > 0) {
            if (scriptCondition.Buff.Value && !session.Player.Buffs.HasBuff(scriptCondition.Buff.Key)) {
                return false;
            }
        }

        if (scriptCondition.Meso.Key > 0) {
            if (scriptCondition.Meso.Value && session.Currency.Meso < scriptCondition.Meso.Key) {
                return false;
            }

            if (!scriptCondition.Meso.Value && session.Currency.Meso >= scriptCondition.Meso.Key) {
                return false;
            }
        }

        if (scriptCondition.Level.Key > 0) {
            if (scriptCondition.Level.Value && session.Player.Value.Character.Level < scriptCondition.Level.Key) {
                return false;
            }

            if (!scriptCondition.Level.Value && session.Player.Value.Character.Level >= scriptCondition.Level.Key) {
                return false;
            }
        }

        if (scriptCondition.AchieveCompleted.Key > 0) {
            if (scriptCondition.AchieveCompleted.Value && !session.Achievement.HasAchievement(scriptCondition.AchieveCompleted.Key)) {
                return false;
            }

            if (!scriptCondition.AchieveCompleted.Value && session.Achievement.HasAchievement(scriptCondition.AchieveCompleted.Key)) {
                return false;
            }
        }

        if (scriptCondition.InGuild && session.Player.Value.Character.GuildId == 0) {
            return false;
        }

        if (scriptCondition.DeathPenalty && session.Config.DeathPenaltyEndTick < session.Field.FieldTick) {
            return false;
        }

        return true;
    }

    public static ScriptState? GetQuestScriptState(GameSession session, ScriptMetadata? scriptMetadata, int npcId) {
        if (scriptMetadata is null) {
            return null;
        }
        session.Quest.TryGetQuest(scriptMetadata.Id, out Quest? quest);
        QuestState questState = quest?.State ?? QuestState.None;

        int stateId = questState switch {
            QuestState.None => GetFirstStateScript(scriptMetadata.States.Keys.ToArray(), 100, 200),
            QuestState.Started => GetFirstStateScript(scriptMetadata.States.Keys.ToArray(), 200, 300),
            _ => 0,
        };

        if (quest != null && session.Quest.CanStart(quest.Metadata)) {
            stateId = GetFirstStateScript(scriptMetadata.States.Keys.ToArray(), 200, 300);
        }

        // The completion dialogue only when the quest can complete; otherwise its in-progress one (a refused Complete click said nothing).
        if (quest != null && quest.Metadata.Basic.CompleteNpc == npcId && session.Quest.CanComplete(quest)) {
            stateId = GetFirstStateScript(scriptMetadata.States.Keys.ToArray(), 300, 400);
        }

        return scriptMetadata.States.TryGetValue(stateId, out ScriptState? scriptState) ? scriptState : null;
    }

    public static int GetFirstStateScript(IEnumerable<int> questStates, int lowerBound, int upperBound) {
        IEnumerable<int> statesInRange = questStates.Where(id => id >= lowerBound && id <= upperBound);
        return statesInRange.Min();
    }
}
