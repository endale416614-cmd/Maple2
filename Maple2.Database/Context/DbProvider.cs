using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maple2.Database.Context;

/// <summary>
/// Builds DbContextOptions for the configured database engine.
/// DB_PROVIDER=mysql (default) uses DB_IP/DB_PORT/DB_USER/DB_PASSWORD.
/// DB_PROVIDER=sqlite stores each database as "&lt;name&gt;.db" in SQLITE_DIR, no database server needed.
/// </summary>
public static class DbProvider {
    public const string GAME_DB = "GAME_DB_NAME";
    public const string DATA_DB = "DATA_DB_NAME";

    public static bool IsSqlite =>
        string.Equals(Environment.GetEnvironmentVariable("DB_PROVIDER"), "sqlite", StringComparison.OrdinalIgnoreCase);

    // AUTO_INCREMENT seeds from the MySQL migrations, so ids look the same on both engines.
    private static readonly (string Table, long NextId)[] IdSeeds = [
        ("account", 10000000000),
        ("character", 20000000000),
        ("club", 30000000000),
        ("buddy", 40000000000),
        ("ugcmap", 80000000000),
        ("ugcmap-cube", 1000000000000),
        ("item", 2000000000000),
        ("meso-market", 60000000000),
        ("mail", 100000),
        ("guild", 70000000000),
        ("ugc-market-item", 90000000001),
        ("premium-market-item", 100000000000),
        ("system-banner", 10000),
        ("black-market-listing", 110000000000),
    ];

    public static DbContextOptions Options(string databaseEnv, string? migrationsAssembly = null) {
        var builder = new DbContextOptionsBuilder();
        if (IsSqlite) {
            builder.UseSqlite(SqliteConnectionString(databaseEnv), sqlite => {
                if (migrationsAssembly != null) {
                    sqlite.MigrationsAssembly(migrationsAssembly);
                }
            });
        } else {
            string connection = MySqlConnectionString(databaseEnv);
            builder.UseMySql(connection, ServerVersion.AutoDetect(connection), mysql => {
                if (migrationsAssembly != null) {
                    mysql.MigrationsAssembly(migrationsAssembly);
                }
            });
        }
        return builder.Options;
    }

    public static DbContextOptions MySqlOptions(string databaseEnv) {
        string connection = MySqlConnectionString(databaseEnv);
        return new DbContextOptionsBuilder().UseMySql(connection, ServerVersion.AutoDetect(connection)).Options;
    }

    public static DbContextOptions SqliteOptions(string databaseEnv, bool foreignKeys = true) {
        return new DbContextOptionsBuilder().UseSqlite(SqliteConnectionString(databaseEnv, foreignKeys)).Options;
    }

    public static string MySqlConnectionString(string databaseEnv) {
        string? server = Environment.GetEnvironmentVariable("DB_IP");
        string? port = Environment.GetEnvironmentVariable("DB_PORT");
        string? database = Environment.GetEnvironmentVariable(databaseEnv);
        string? user = Environment.GetEnvironmentVariable("DB_USER");
        string? password = Environment.GetEnvironmentVariable("DB_PASSWORD");

        if (server == null || port == null || database == null || user == null || password == null) {
            throw new ArgumentException("Database connection information was not set");
        }

        return $"Server={server};Port={port};Database={database};User={user};Password={password};oldguids=true";
    }

    public static string SqlitePath(string databaseEnv) {
        string? database = Environment.GetEnvironmentVariable(databaseEnv);
        if (database == null) {
            throw new ArgumentException($"{databaseEnv} was not set");
        }

        // The game data (rebuilt from the client, replaced by updates) may live apart from the player's save.
        string? dataDirectory = databaseEnv == DATA_DB ? Environment.GetEnvironmentVariable("SQLITE_DATA_DIR") : null;
        string directory = dataDirectory ?? Environment.GetEnvironmentVariable("SQLITE_DIR") ?? Path.Combine(Paths.SOLUTION_DIR, "SaveData");
        Directory.CreateDirectory(directory);
        return Path.GetFullPath(Path.Combine(directory, $"{database}.db"));
    }

    private static string SqliteConnectionString(string databaseEnv, bool foreignKeys = true) {
        return new SqliteConnectionStringBuilder {
            DataSource = SqlitePath(databaseEnv),
            ForeignKeys = foreignKeys,
            // Seconds to keep retrying while another server process holds the write lock.
            DefaultTimeout = 30,
        }.ToString();
    }

    /// <summary>Whether the database exists. SQLite would silently create an empty file on connect.</summary>
    public static bool Exists(DbContext context, string databaseEnv) {
        return IsSqlite ? File.Exists(SqlitePath(databaseEnv)) : context.Database.CanConnect();
    }

    /// <summary>
    /// Additive save upgrade for SQLite (EnsureCreated never changes an existing database): creates missing tables
    /// and indexes and adds missing columns (NOT NULL columns get the type's zero value). Never drops or alters
    /// existing data. Returns the statements it ran.
    /// </summary>
    public static IReadOnlyList<string> UpgradeSqliteSchema(DbContext context) {
        var applied = new List<string>();
        string[] script = context.Database.GenerateCreateScript()
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(statement => statement.Length > 0)
            .ToArray();

        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToList()) {
            existingTables.Add(name);
        }

        // Missing tables, created exactly as a new save would create them.
        foreach (string statement in script.Where(s => s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))) {
            string table = statement.Split('"')[1];
            if (!existingTables.Contains(table)) {
                context.Database.ExecuteSqlRaw(EscapeBraces(statement));
                existingTables.Add(table);
                applied.Add($"table {table}");
            }
        }

        // Missing columns of existing tables.
        foreach (IEntityType entity in context.Model.GetEntityTypes()) {
            string? table = entity.GetTableName();
            if (table == null) {
                continue;
            }
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string column in context.Database.SqlQueryRaw<string>($"SELECT name AS Value FROM pragma_table_info('{table}')").ToList()) {
                columns.Add(column);
            }
            foreach (IProperty property in entity.GetProperties()) {
                string? column = property.GetColumnName(store);
                if (column == null || columns.Contains(column)) {
                    continue;
                }
                string type = property.GetColumnType(store);
                string definition = property.IsColumnNullable(store) ? type : $"{type} NOT NULL DEFAULT {ZeroValue(type)}";
                context.Database.ExecuteSqlRaw(EscapeBraces($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}"));
                columns.Add(column);
                applied.Add($"column {table}.{column}");
            }
        }

        // Missing indexes.
        foreach (string statement in script.Where(s => s.StartsWith("CREATE INDEX", StringComparison.OrdinalIgnoreCase)
                                                       || s.StartsWith("CREATE UNIQUE INDEX", StringComparison.OrdinalIgnoreCase))) {
            string index = statement.Split('"')[1];
            bool exists = context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND name = {0}", index).Any();
            if (!exists) {
                try {
                    context.Database.ExecuteSqlRaw(EscapeBraces(statement));
                    applied.Add($"index {index}");
                } catch (SqliteException ex) {
                    // e.g. a new unique index the old data violates: keep playing without it.
                    Serilog.Log.Logger.Warning("Save upgrade could not create index {Index}: {Message}", index, ex.Message);
                }
            }
        }
        return applied;

        static string ZeroValue(string type) {
            string upper = type.ToUpperInvariant();
            return upper.Contains("INT") || upper.Contains("REAL") || upper.Contains("NUM") || upper.Contains("FLOA") || upper.Contains("DOUB")
                ? "0"
                : upper.Contains("BLOB") ? "X''" : "''";
        }
        static string EscapeBraces(string sql) => sql.Replace("{", "{{").Replace("}", "}}");
    }

    /// <summary>
    /// Creates the SQLite game database from the model (the MySQL migrations do not apply to SQLite).
    /// Safe to call on every start; does nothing once the tables exist.
    /// </summary>
    public static void EnsureSqliteGameSchema() {
        using var context = new Ms2Context(Options(GAME_DB));
        bool created = context.Database.EnsureCreated();
        // WAL lets the World/Login/Game/Web processes read while one of them writes.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        if (!created) {
            // An existing save from an older version: add what newer versions expect.
            foreach (string change in UpgradeSqliteSchema(context)) {
                Serilog.Log.Logger.Information("Save upgraded: {Change}", change);
            }
            return;
        }

        foreach ((string table, long nextId) in IdSeeds) {
            context.Database.ExecuteSqlRaw(
                "INSERT INTO sqlite_sequence (name, seq) SELECT {0}, {1} " +
                "WHERE EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = {0}) " +
                "AND NOT EXISTS (SELECT 1 FROM sqlite_sequence WHERE name = {0})",
                table, nextId - 1);
        }
    }
}
