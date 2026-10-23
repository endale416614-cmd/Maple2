using System.Numerics;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.PacketHandlers;

/// <summary>
/// A usable NPC (client NPC XML interact/@interactFunction "UseSkill,id,level", e.g. the Pirate Cannons of Moonlight
/// Fortress): the client plays the interaction and sends the NPC's object id; the NPC casts its skill (DL-52).
/// </summary>
public class InteractNpcHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.InteractNpc;

    public override void Handle(GameSession session, IByteReader packet) {
        int objectId = packet.ReadInt();
        // Every usable NPC is friendly, so it lives in Npcs.
        if (session.Field?.Npcs.TryGetValue(objectId, out FieldNpc? npc) != true || npc.IsDead) {
            return;
        }
        if (NpcSummons.InteractSkill(npc.Value.Id) is not [int skillId, int level, int coolTime]) {
            return;
        }
        // Twice the client's reach: the player's position on the server lags a little.
        if (Vector3.Distance(session.Player.Position, npc.Position) > 2 * session.ServerTableMetadata.ConstantsTable.InteractionDistance) {
            return;
        }
        if (!TryUse(ref npc.InteractReadyTick, session.Field.FieldTick, coolTime)) {
            return;
        }
        Logger.Debug("[InteractNpc] npc {NpcId} obj {ObjectId}: skill {SkillId} lv {Level}", npc.Value.Id, objectId, skillId, level);
        npc.CastAiSkill(skillId, (short) level, faceTarget: 0, facePos: default); // the shot keeps the NPC's own facing
    }

    /// <summary>The interact cooldown: false while it runs, else true and it starts.</summary>
    public static bool TryUse(ref long readyTick, long now, int coolTime) {
        if (now < readyTick) {
            return false;
        }
        readyTick = now + coolTime;
        return true;
    }
}
