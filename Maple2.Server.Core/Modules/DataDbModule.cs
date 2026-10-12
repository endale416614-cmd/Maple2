using Autofac;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Microsoft.EntityFrameworkCore;
using Module = Autofac.Module;

namespace Maple2.Server.Core.Modules;

public class DataDbModule : Module {
    private readonly DbContextOptions options;

    public DataDbModule() {
        options = DbProvider.Options(DbProvider.DATA_DB);
    }

    protected override void Load(ContainerBuilder builder) {
        // NoTracking, Metadata is cached separately, and we use a single context for lifetime.
        var context = new MetadataContext(options);

        // check Ingest has been run
        if (!DbProvider.Exists(context, DbProvider.DATA_DB)) {
            throw new Exception("Metadata database not found, did you run Maple2.File.Ingest?");
        }

        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        builder.RegisterInstance(context);

        builder.RegisterType<ItemMetadataStorage>().SingleInstance();
        builder.RegisterType<MapEntityStorage>().SingleInstance();
        builder.RegisterType<MapDataStorage>().SingleInstance();
        builder.RegisterType<MapMetadataStorage>().SingleInstance();
        builder.RegisterType<NpcMetadataStorage>().SingleInstance();
        builder.RegisterType<QuestMetadataStorage>().SingleInstance();
        builder.RegisterType<RideMetadataStorage>().SingleInstance();
        builder.RegisterType<AchievementMetadataStorage>().SingleInstance();
        builder.RegisterType<ScriptMetadataStorage>().SingleInstance();
        builder.RegisterType<SkillMetadataStorage>().SingleInstance();
        builder.RegisterType<TableMetadataStorage>().SingleInstance();
        builder.RegisterType<ServerTableMetadataStorage>().SingleInstance();
        builder.RegisterType<AiMetadataStorage>().SingleInstance();
        builder.RegisterType<FunctionCubeMetadataStorage>().SingleInstance();
        builder.RegisterType<TriggerScriptMetadata>().SingleInstance();
        builder.RegisterType<BanWordStorage>().SingleInstance();
    }
}
