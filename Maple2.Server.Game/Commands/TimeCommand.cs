using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Session;
using Maple2.Tools;

namespace Maple2.Server.Game.Commands;

/// <summary>/time: the in-game date of the world clock (T-02), for every player.</summary>
public class TimeCommand : GameCommand {
    public TimeCommand(GameSession session) : base(AdminPermissions.None, "time", "Show the in-game time and season.") {
        this.SetHandler<InvocationContext>(ctx => {
            using GameStorage.Request db = session.GameStorage.Context();
            (long ticks, long savedAt) = db.GetWorldTicks();
            ctx.Console.Out.WriteLine(OfflineCalendar.ToDate(OfflineCalendar.Now(ticks, savedAt, GameClock.UtcNow.ToUnixTimeMilliseconds())).ToString());
            ctx.ExitCode = 0;
        });
    }
}
