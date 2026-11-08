using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

/// <summary>/autoenchant [target]: toggles the mode in which one press of Enchant keeps trying. AdminPermissions.None = no admin rights needed.</summary>
public class AutoEnchantCommand : GameCommand {
    private readonly GameSession session;
    private readonly bool enabled;

    public AutoEnchantCommand(GameSession session) : this(session, OfflineAutoEnchant.Enabled) { }

    /// <param name="enabled">For tests: OfflineAutoEnchant.Enabled is read once from the environment.</param>
    public AutoEnchantCommand(GameSession session, bool enabled) : base(AdminPermissions.None, "autoenchant", "Toggle: one press of Enchant keeps trying until the next level or the target; with a target, one press of a gem upgrade runs to that tier.") {
        this.session = session;
        this.enabled = enabled;

        var target = new Argument<int?>("target", () => null, $"Enchant level or gem tier to stop at, 1 to {OfflineAutoEnchant.MaxTarget} (default: the next level).");
        AddArgument(target);
        this.SetHandler<InvocationContext, int?>(Handle, target);
    }

    private void Handle(InvocationContext ctx, int? target) {
        if (!enabled) {
            ctx.Console.Error.WriteLine("Auto enchant is off in Legacy Mode.");
            ctx.ExitCode = 1;
            return;
        }
        if (target is < 1 or > OfflineAutoEnchant.MaxTarget) {
            ctx.Console.Error.WriteLine($"The target is an enchant level or gem tier from 1 to {OfflineAutoEnchant.MaxTarget}.");
            ctx.ExitCode = 1;
            return;
        }

        ctx.Console.Out.WriteLine(OfflineAutoEnchant.Message(OfflineAutoEnchant.Set(session.CharacterId, target)));
    }
}
