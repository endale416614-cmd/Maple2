using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

/// <summary>/autoreroll [attribute]: names the line one press of Change Attributes keeps rolling for; alone it turns the mode off. AdminPermissions.None = no admin rights needed.</summary>
public class AutoRerollCommand : GameCommand {
    private readonly GameSession session;
    private readonly bool enabled;

    public AutoRerollCommand(GameSession session) : this(session, OfflineAutoReroll.Enabled) { }

    /// <param name="enabled">For tests: OfflineAutoReroll.Enabled is read once from the environment.</param>
    public AutoRerollCommand(GameSession session, bool enabled) : base(AdminPermissions.None, "autoreroll", "One press of Change Attributes keeps rolling until the named line appears; alone: off.") {
        this.session = session;
        this.enabled = enabled;

        var attribute = new Argument<string[]>("attribute", "The line to stop at: CriticalRate, or critical rate.") { Arity = ArgumentArity.ZeroOrMore };
        AddArgument(attribute);
        this.SetHandler<InvocationContext, string[]>(Handle, attribute);
    }

    private void Handle(InvocationContext ctx, string[] words) {
        if (!enabled) {
            ctx.Console.Error.WriteLine("Auto reroll is off in Legacy Mode.");
            ctx.ExitCode = 1;
            return;
        }

        LockOption? line = null;
        if (words.Length > 0) {
            line = OfflineAutoReroll.Parse(string.Concat(words));
            if (line == null) {
                ctx.Console.Error.WriteLine($"Unknown line {string.Join(' ', words)}. The player guide lists the names.");
                ctx.ExitCode = 1;
                return;
            }
        }

        ctx.Console.Out.WriteLine(OfflineAutoReroll.Message(OfflineAutoReroll.Set(session.CharacterId, line)));
    }
}
