using System.Reflection;
using Autofac;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Microsoft.EntityFrameworkCore;
using Module = Autofac.Module;

namespace Maple2.Server.Core.Modules;

public class WebDbModule : Module {
    private const string NAME = "WebDbOptions";

    private readonly DbContextOptions options;

    public WebDbModule() {
        options = DbProvider.Options(DbProvider.GAME_DB);
    }

    protected override void Load(ContainerBuilder builder) {
        builder.RegisterInstance(options)
            .Named<DbContextOptions>(NAME);

        builder.RegisterType<WebStorage>()
            .WithParameter(Condition, Resolve)
            .SingleInstance();
    }

    private static bool Condition(ParameterInfo info, IComponentContext context) {
        return info.Name == "options";
    }

    private static DbContextOptions Resolve(ParameterInfo info, IComponentContext context) {
        return context.ResolveNamed<DbContextOptions>(NAME);
    }
}
