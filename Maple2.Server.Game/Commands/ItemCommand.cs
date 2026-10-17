using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

public class ItemCommand : GameCommand {
    private const int MAX_SOCKET = 5;

    private readonly GameSession session;
    private readonly ItemMetadataStorage itemStorage;

    public ItemCommand(GameSession session, ItemMetadataStorage itemStorage) : base(AdminPermissions.None, "item", "Item spawning.") {
        this.session = session;
        this.itemStorage = itemStorage;

        var id = new Argument<int>("id", "Id of item to spawn.");
        var amount = new Option<int>(["--amount", "-a"], () => 1, "Amount of the item.");
        var rarity = new Option<int>(["--rarity", "-r"], () => -1, "Rarity of the item (default: from item data).");
        var drop = new Option<bool>(["--drop"], "Drop item instead of adding to inventory");
        var rollMax = new Option<bool>(["--roll-max"], () => false, "Max roll stats.");

        AddArgument(id);
        AddOption(amount);
        AddOption(rarity);
        AddOption(drop);
        AddOption(rollMax);
        this.SetHandler<InvocationContext, int, int, int, bool, bool>(Handle, id, amount, rarity, drop, rollMax);
    }

    private void Handle(InvocationContext ctx, int itemId, int amount, int rarity, bool drop, bool rollMax) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }
        ctx.ExitCode = ItemGrant.Give(session, itemId, amount, rarity, drop, rollMax, ctx.Console) ? 0 : 1;
    }
}
