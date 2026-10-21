using Maple2.Database.Extensions;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.PacketHandlers.Field;

namespace Maple2.Server.Game.PacketHandlers;

/// <summary>The client's "skip tutorial" button: the job's skip field, skip item and tutorial unlocks (R12-12).</summary>
public class SkipTutorialHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.RequestSkipTutorial;

    public override void Handle(GameSession session, IByteReader packet) {
        // source: RecvOp.cs:93; the request body carries nothing this server uses.
        if (session.Field is null) {
            return;
        }
        if (!session.TableMetadata.JobTable.Entries.TryGetValue(session.Player.Value.Character.Job.Code(), out JobTable.Entry? entry) || entry.Tutorial.SkipField == 0) {
            return;
        }
        JobTable.Tutorial tutorial = entry.Tutorial;
        if (tutorial.SkipItem > 0 && session.Field.ItemDrop.CreateItem(tutorial.SkipItem, rarity: 1, amount: 1) is { } item) {
            session.Item.Inventory.Add(item, true);
        }
        session.Player.Value.Unlock.Maps.UnionWith(tutorial.OpenMaps);
        session.Player.Value.Unlock.Taxis.UnionWith(tutorial.OpenTaxis);
        session.Send(session.PrepareField(tutorial.SkipField)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }
}