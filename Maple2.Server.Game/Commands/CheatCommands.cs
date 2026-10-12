using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
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
