using System.CommandLine;
using System.CommandLine.Invocation;
using Maple2.Model.Enum;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Game.Commands;

/// <summary>
/// /dungeon &lt;id&gt; opens a solo room of a dungeon and moves the player in, as the dungeon window's Enter does but
/// without the player's entry limits (level, trophy, gear score, earlier clears; the window's Enter keeps them);
/// /dungeon 0 enters the room's first field from its lobby. For scripted play tests (tools/harness, RFF_DESIGN 5, V-03):
/// /warp only reaches a dungeon map as a normal field.
/// </summary>
public class DungeonCommand : GameCommand {
    public DungeonCommand(GameSession session) : base(AdminPermissions.Warp, "dungeon", "Enter a dungeon alone.") {
        var id = new Argument<int>("id", "Dungeon id (e.g. 20004002), or 0 for the first field of the current room.");
        AddArgument(id);
        this.SetHandler<InvocationContext, int>((ctx, dungeonId) => {
            if (dungeonId == 0) {
                session.Dungeon.EnterInitField();
            } else {
                session.Dungeon.CreateDungeonRoom(dungeonId, false, ignoreEnterLimits: true);
            }
            ctx.ExitCode = 0;
        }, id);
    }
}
