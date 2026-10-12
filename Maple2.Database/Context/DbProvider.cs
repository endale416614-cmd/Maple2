using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

        string directory = Environment.GetEnvironmentVariable("SQLITE_DIR") ?? Path.Combine(Paths.SOLUTION_DIR, "SaveData");
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
    /// Creates the SQLite game database from the model (the MySQL migrations do not apply to SQLite).
    /// Safe to call on every start; does nothing once the tables exist.
    /// </summary>
    public static void EnsureSqliteGameSchema() {
        using var context = new Ms2Context(Options(GAME_DB));
        bool created = context.Database.EnsureCreated();
        // WAL lets the World/Login/Game/Web processes read while one of them writes.
        context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        if (!created) {
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
