using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

public class NpcTalkHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.NpcTalk;

    private enum Command : byte {
        Close = 0,
        Talk = 1,
        Continue = 2,
        EnchantUnknown = 4,
        Enchant = 6,
        Quest = 7,
        AcceptAllianceQuest = 8,
        TalkAlliance = 9,
        Custom = 11,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required NpcMetadataStorage NpcMetadata { private get; init; }
    public required ScriptMetadataStorage ScriptMetadata { private get; init; }
    public required ServerTableMetadataStorage ServerTableMetadata { private get; init; }
    private ConstantsTable Constants => ServerTableMetadata.ConstantsTable;
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Close:
                HandleClose(session);
                return;
            case Command.Talk:
                HandleTalk(session, packet);
                return;
            case Command.Continue:
                HandleContinue(session, packet);
                return;
            case Command.EnchantUnknown:
                return;
            case Command.Enchant:
                HandleEnchant(session, packet);
                return;
            case Command.Quest:
                HandleQuest(session, packet);
                return;
            case Command.AcceptAllianceQuest:
                HandleQuest(session, packet); // alliance-board quests open like a quest of the NPC; QuestHandler checks requirements when player accepts
                return;
            case Command.TalkAlliance:
                HandleTalkAlliance(session);
                return;
            case Command.Custom:
                return;
        }
    }

    private void HandleClose(GameSession session) {
        session.NpcScript?.Npc?.StopTalk();
        session.NpcScript = null;
        session.Shop.ClearActiveShop();
    }

    private void HandleTalk(GameSession session, IByteReader packet) {
        if (session.Field is null) return;
        int objectId = packet.ReadInt();
        if (!session.Field.Npcs.TryGetValue(objectId, out FieldNpc? npc)) {
            return; // Invalid Npc
        }

        NpcTalkUtil.Talk(session, npc);
    }

    private void HandleContinue(GameSession session, IByteReader packet) {
        session.NpcScript?.ProcessScriptFunction(false);
        // Not talking to an Npc.
        if (session.NpcScript == null) {
            return;
        }
        int pick = packet.ReadInt();

        /* The ordering is
        / Quests
        / Dialog
        / Talk */
        int addedOptions = 0;
        if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Select)) {
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Quest)) {
                addedOptions += session.NpcScript.Quests.Count;
                // Picked quest
                if (pick < addedOptions) {
                    FieldNpc? npc = session.NpcScript.Npc;
                    if (npc == null) {
                        session.Send(NpcTalkPacket.Close());
                        return;
                    }
                    if (!session.ScriptMetadata.TryGet(session.NpcScript.Quests.ElementAt(pick).Value.Id, out ScriptMetadata? metadata)) {
                        session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
                        session.NpcScript = null;
                        return;
                    }
                    ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
                    session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);
                    if (!session.NpcScript.BeginQuest()) {
                        session.NpcScript = null;
                    }
                    return;
                }
            }

            NpcDialogue dialogue;
            if (session.NpcScript.TalkType.HasFlag(NpcTalkType.Dialog)) {
                addedOptions++;
                if (pick < addedOptions) {
                    session.NpcScript.EnterDialog();
                    dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
                    session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
                    return;
                }
            }

            session.NpcScript.EnterTalk();
            dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
            session.Send(NpcTalkPacket.Continue(session.NpcScript.TalkType, dialogue));
            return;
        }


        // Attempt to Continue, if |false|, the dialogue has terminated.
        if (!session.NpcScript.Continue(pick)) {
            session.NpcScript = null;
        }
    }

    private void HandleEnchant(GameSession session, IByteReader packet) {
        int npcId = packet.ReadInt();
        int scriptId = packet.ReadInt();
        var eventType = packet.Read<ScriptEventType>();

        switch (eventType) {
            case ScriptEventType.EnchantSelect:
            case ScriptEventType.PeachySelect:
                if (!session.ScriptMetadata.TryGet(npcId, out ScriptMetadata? script) ||
                    !script.States.TryGetValue(Constants.EnchantMasterScriptID, out ScriptState? state)) {
                    return;
                }

                CinematicEventScript[] eventScripts = state.Contents.First().Events;
                session.NpcScript = new NpcScriptManager(session, eventScripts);
                return;
            case ScriptEventType.EmpowerSelect:
                session.NpcScript?.EmpowerEvent();
                return;
            case ScriptEventType.EnchantComplete:
                session.ItemEnchant.NpcTalkEvent(eventType);
                return;
        }
    }

    private void HandleQuest(GameSession session, IByteReader packet) {
        if (session.NpcScript == null) {
            return;
        }

        int questId = packet.ReadInt();
        packet.ReadShort(); // 2 or 0. 2 = Start quest, 0 = Complete quest.

        FieldNpc? npc = session.NpcScript.Npc;
        if (npc == null) {
            session.Send(NpcTalkPacket.Close());
            return;
        }
        if (!session.ScriptMetadata.TryGet(questId, out ScriptMetadata? metadata)) {
            session.Send(NpcTalkPacket.Respond(npc, NpcTalkType.None, default));
            session.NpcScript = null;
            return;
        }

        ScriptState? state = NpcTalkUtil.GetQuestScriptState(session, metadata, npc.Value.Id);
        session.NpcScript = new NpcScriptManager(session, npc, metadata, state, NpcTalkType.Quest);

        if (!session.NpcScript.BeginQuest()) {
            session.NpcScript = null;
        }
    }

    private void HandleTalkAlliance(GameSession session) {
        if (session.NpcScript == null) {
            return;
        }
        session.NpcScript.EnterTalk();
        var dialogue = new NpcDialogue(session.NpcScript.State?.Id ?? 0, session.NpcScript.Index, session.NpcScript.Button);
        session.Send(NpcTalkPacket.AllianceTalk(session.NpcScript.TalkType, dialogue));
    }
}
