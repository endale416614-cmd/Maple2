using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

/// <summary>
/// /npchp &lt;percent&gt; [--id n]: sets the health of the field's living NPCs (or those with one id) to a percent of
/// their max, so a play test can reach an AI's HP thresholds (RFF_DESIGN 5 scenario 7: MinimumHpNode holds).
/// </summary>
public class NpcHpCommand : GameCommand {
    public NpcHpCommand(GameSession session) : base(AdminPermissions.GameMaster, "npchp", "Set NPC health to a percent of max.") {
        var percent = new Argument<float>("percent", "Health in percent of max (0-100].");
        var id = new Option<int>(["--id", "-i"], () => 0, "Only NPCs with this id.");
        AddArgument(percent);
        AddOption(id);
        this.SetHandler<InvocationContext, float, int>((ctx, value, npcId) => {
            if (session.Field is null || value <= 0 || value > 100) {
                ctx.Console.Error.WriteLine("Usage: /npchp <percent 0-100> [--id <npc id>]");
                return;
            }
            int count = 0;
            foreach (FieldNpc npc in session.Field.Mobs.Values.Concat(session.Field.Npcs.Values)) {
                if (npc.IsDead || npcId != 0 && npc.Value.Metadata.Id != npcId) continue;
                var health = npc.Stats.Values[BasicAttribute.Health];
                health.Current = Math.Max(1, (long) (health.Total * value / 100));
                count++;
            }
            ctx.Console.Out.WriteLine($"Set {count} NPC(s) to {value}% health.");
            ctx.ExitCode = 0;
        }, percent, id);
    }
}
