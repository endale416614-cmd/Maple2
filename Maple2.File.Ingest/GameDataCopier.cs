using System.Collections;
using System.Reflection;
using Maple2.Database.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maple2.File.Ingest;

/// <summary>
/// One-off move of player data (accounts, characters, items, ...) into the embedded SQLite save.
/// From MySQL:          dotnet run -- --copy-game-from-mysql
/// From an older save:  dotnet run -- --copy-game-from-sqlite &lt;path to game-server.db&gt;
/// (rebuilds the save with the current schema, e.g. after a collation change)
/// </summary>
public static class GameDataCopier {
    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!;
    private static readonly MethodInfo AsNoTrackingMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethod(nameof(EntityFrameworkQueryableExtensions.AsNoTracking))!;

    public static void CopyFromMySql() {
        Copy(DbProvider.MySqlOptions(DbProvider.GAME_DB));
    }

    public static void CopyFromSqlite(string sourcePath) {
        if (!System.IO.File.Exists(sourcePath)) {
            throw new FileNotFoundException("Source save not found", sourcePath);
        }
        if (Path.GetFullPath(sourcePath).Equals(DbProvider.SqlitePath(DbProvider.GAME_DB), StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Source and target are the same file; move the old save away first.");
        }
        Copy(new DbContextOptionsBuilder().UseSqlite($"Data Source={Path.GetFullPath(sourcePath)};Mode=ReadOnly").Options);
    }

    private static void Copy(DbContextOptions sourceOptions) {
        DbProvider.EnsureSqliteGameSchema();

        using var source = new Ms2Context(sourceOptions);
        // Foreign keys off while copying so tables can be filled in any order.
        using var target = new Ms2Context(DbProvider.SqliteOptions(DbProvider.GAME_DB, foreignKeys: false));

        if (target.Model.FindEntityType("Maple2.Database.Model.Account") is { } accountType &&
                Count(target, accountType.ClrType) > 0) {
            throw new InvalidOperationException($"{DbProvider.SqlitePath(DbProvider.GAME_DB)} already has accounts; refusing to overwrite.");
        }

        foreach (IEntityType entityType in source.Model.GetEntityTypes()) {
            if (entityType.IsOwned()) {
                continue; // copied with their owner
            }
            if (entityType.HasSharedClrType) {
                Console.WriteLine($"Skipping shared-type entity {entityType.Name}");
                continue;
            }

            var rows = ((IEnumerable) Query(source, entityType.ClrType)).Cast<object>().ToList();
            target.AddRange(rows);
            target.SaveChanges();
            target.ChangeTracker.Clear();
            Console.WriteLine($"{entityType.GetTableName(),-28} {rows.Count,6} rows");
        }

        Console.WriteLine($"Copied game data to {DbProvider.SqlitePath(DbProvider.GAME_DB)}");
    }

    /// <summary>
    /// Rebuilds the metadata database from an existing SQLite copy with the current schema
    /// (no client files needed). Run with: dotnet run -- --rebuild-data-from-sqlite &lt;path to maple-data.db&gt;
    /// </summary>
    public static void RebuildDataFromSqlite(string sourcePath) {
        sourcePath = Path.GetFullPath(sourcePath);
        if (!System.IO.File.Exists(sourcePath)) {
            throw new FileNotFoundException("Source metadata not found", sourcePath);
        }
        if (sourcePath.Equals(DbProvider.SqlitePath(DbProvider.DATA_DB), StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Source and target are the same file; point SQLITE_DIR somewhere else.");
        }

        using var target = new MetadataContext(DbProvider.Options(DbProvider.DATA_DB));
        target.Database.EnsureDeleted();
        target.Database.EnsureCreated();
        target.Database.OpenConnection();
        target.Database.ExecuteSqlRaw("ATTACH DATABASE {0} AS old", sourcePath);

        using (var transaction = target.Database.BeginTransaction()) {
            foreach (string table in target.Model.GetEntityTypes().Select(type => type.GetTableName()).OfType<string>().Distinct()) {
                using var command = target.Database.GetDbConnection().CreateCommand();
                command.Transaction = transaction.GetDbTransaction();
                command.CommandText = $"SELECT group_concat('\"' || name || '\"', ',') FROM pragma_table_info('{table}')";
                string columns = (string) command.ExecuteScalar()!;
#pragma warning disable EF1002 // table/column names come from the model, not user input
                int rows = target.Database.ExecuteSqlRaw($"INSERT INTO main.\"{table}\" ({columns}) SELECT {columns} FROM old.\"{table}\"");
#pragma warning restore EF1002
                Console.WriteLine($"{table,-28} {rows,8} rows");
            }
            transaction.Commit();
        }

        target.Database.ExecuteSqlRaw("DETACH DATABASE old");
        Console.WriteLine($"Rebuilt {DbProvider.SqlitePath(DbProvider.DATA_DB)}");
    }

    private static object Query(DbContext context, Type clrType) {
        object set = SetMethod.MakeGenericMethod(clrType).Invoke(context, null)!;
        return AsNoTrackingMethod.MakeGenericMethod(clrType).Invoke(null, [set])!;
    }

    private static int Count(DbContext context, Type clrType) {
        return ((IEnumerable) Query(context, clrType)).Cast<object>().Count();
    }
}
