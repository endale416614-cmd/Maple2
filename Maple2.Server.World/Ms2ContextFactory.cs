using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore.Design;

namespace Maple2.Server.World;

public sealed class Ms2ContextFactory : IDesignTimeDbContextFactory<Ms2Context> {
    public Ms2Context CreateDbContext(string[] args) {
        return new Ms2Context(DbProvider.Options(DbProvider.GAME_DB, typeof(Ms2ContextFactory).Assembly.FullName));
    }
}
