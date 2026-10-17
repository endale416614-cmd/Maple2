using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Text;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Packets;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

// Offline edition cheats for every player (see OfflineCheats). AdminPermissions.None = no admin rights needed.

/// <summary>/godmode: toggles taking no damage and killing any NPC in one hit.</summary>
public class GodModeCommand : GameCommand {
    private readonly GameSession session;

    public GodModeCommand(GameSession session) : base(AdminPermissions.None, "godmode", "Toggle god mode: no damage taken, every hit kills.") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }
        if (session.Field is null) {
            return;
        }

        bool on = OfflineCheats.ToggleGodMode(session.CharacterId);
        if (on) {
            FieldPlayer player = session.Player;
            player.RecoverHp((int) Math.Min(int.MaxValue, player.Stats.Values[BasicAttribute.Health].Total));
            ctx.Console.Out.WriteLine("God mode ON: you take no damage and every hit kills. Type /godmode again to turn it off.");
        } else {
            ctx.Console.Out.WriteLine("God mode OFF.");
        }
    }
}

/// <summary>/killall: kills every monster on the map, with kill credit (quests, exp, drops) for the player.</summary>
public class KillAllCommand : GameCommand {
    private readonly GameSession session;

    public KillAllCommand(GameSession session) : base(AdminPermissions.None, "killall", "Kill every monster on this map (counts for quests).") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }
        if (session.Field is null) {
            return;
        }

        int killed = 0;
        foreach (FieldNpc mob in session.Field.Mobs.Values) {
            if (mob.IsDead || mob.Stats.Values[BasicAttribute.Health].Current <= 0) {
                continue;
            }
            mob.CheatKill(session.Player);
            killed++;
        }
        ctx.Console.Out.WriteLine(killed == 0 ? "No monsters on this map." : $"Killed {killed} monster{(killed == 1 ? "" : "s")}.");
    }
}

/// <summary>/unstuck: revives the player if needed and sends them to Henesys.</summary>
public class UnstuckCommand : GameCommand {
    private readonly GameSession session;

    public UnstuckCommand(GameSession session) : base(AdminPermissions.None, "unstuck", "Teleport back to Henesys.") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }
        if (session.Field is null) {
            return;
        }

        if (session.Player.IsDead) {
            session.Player.Revive(instant: true);
        }

        ctx.Console.Out.WriteLine("Returning to Henesys...");
        // Homes and guild houses are left by migrating, like GameSession.ReturnField does.
        if (session.Player.Value.Character.MapId == Constant.DefaultHomeMapId || InGuildHouse()) {
            session.Migrate(OfflineCheats.UnstuckMapId);
            return;
        }
        session.Send(session.PrepareField(OfflineCheats.UnstuckMapId)
            ? FieldEnterPacket.Request(session.Player)
            : FieldEnterPacket.Error(MigrationError.s_move_err_default));
    }

    private bool InGuildHouse() {
        if (session.Guild.Guild is null) {
            return false;
        }
        if (!session.TableMetadata.GuildTable.Houses.TryGetValue(session.Guild.Guild.HouseRank, out IReadOnlyDictionary<int, GuildTable.House>? houseRank)) {
            return false;
        }
        return houseRank.TryGetValue(session.Guild.Guild.HouseTheme, out GuildTable.House? house) && house.MapId == session.Player.Value.Character.MapId;
    }
}

/// <summary>/freebies: toggles buying anything for free, whatever the currency.</summary>
public class FreebiesCommand : GameCommand {
    private readonly GameSession session;

    public FreebiesCommand(GameSession session) : base(AdminPermissions.None, "freebies", "Toggle free purchases: buy anything without paying, whatever the currency.") {
        this.session = session;
        this.SetHandler<InvocationContext>(Handle);
    }

    private void Handle(InvocationContext ctx) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }

        if (OfflineCheats.ToggleFreebies(session.CharacterId)) {
            ctx.Console.Out.WriteLine("Freebies ON: everything you buy is free, whatever the currency. Reopen any shop that is already open. Type /freebies again to turn it off.");
        } else {
            ctx.Console.Out.WriteLine("Freebies OFF.");
        }
    }
}

/// <summary>/give: gives an item by its English name, e.g. /give Red Potion 5.</summary>
public class GiveCommand : GameCommand {
    private readonly GameSession session;
    private readonly ItemMetadataStorage itemStorage;

    public GiveCommand(GameSession session, ItemMetadataStorage itemStorage) : base(AdminPermissions.None, "give", "Give an item by name: /give <item name> [count].") {
        this.session = session;
        this.itemStorage = itemStorage;

        var words = new Argument<string[]>("name", "Item name, optionally followed by a count.");
        var rarity = new Option<int>(["--rarity", "-r"], () => -1, "Rarity of the item (default: from item data).");
        var rollMax = new Option<bool>(["--roll-max"], () => false, "Max roll stats.");

        AddArgument(words);
        AddOption(rarity);
        AddOption(rollMax);
        this.SetHandler<InvocationContext, string[], int, bool>(Handle, words, rarity, rollMax);
    }

    private void Handle(InvocationContext ctx, string[] words, int rarity, bool rollMax) {
        if (!OfflineCheats.Enabled) {
            ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
            return;
        }
        if (session.Field is null) {
            return;
        }

        try {
            ItemNameResolver.Result result = ItemNameResolver.Resolve(words, session.Player.Value.Character.Job.Code(),
                name => itemStorage.Search(name).Select(item => new ItemNameResolver.Entry(item.Id, item.Name ?? "", item.Limit.JobRecommends)));
            if (result.Id != 0) {
                ctx.ExitCode = ItemGrant.Give(session, result.Id, result.Count, rarity, drop: false, rollMax, ctx.Console) ? 0 : 1;
                return;
            }

            if (result.Options.Count == 0) {
                ctx.Console.Error.WriteLine($"No item named '{result.Name}'");
                return;
            }
            var builder = new StringBuilder($"<b>Several items match '{result.Name}'. Use /item <id>:</b>");
            foreach (ItemNameResolver.Entry entry in result.Options) {
                builder.Append($"\n• {entry.Id}: {entry.Name}");
            }
            session.Send(NoticePacket.Message(builder.ToString(), true));
        } catch (SystemException ex) {
            ctx.Console.Error.WriteLine(ex.Message);
            ctx.ExitCode = 1;
        }
    }
}
