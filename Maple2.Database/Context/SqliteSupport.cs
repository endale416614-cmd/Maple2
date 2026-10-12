using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Maple2.Database.Context;

/// <summary>
/// Makes the MySQL-shaped model work on SQLite. MySQL fills timestamp columns itself
/// (CURRENT_TIMESTAMP / ON UPDATE); SQLite cannot, so those columns are filled by the app instead.
/// </summary>
internal static class SqliteSupport {
    public const string PROVIDER = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string AUTO_TIMESTAMP = "Maple2:AutoTimestamp";

    public static bool IsSqlite(this DbContext context) => context.Database.ProviderName == PROVIDER;

    /// <summary>Shared setup for every Maple2 DbContext.</summary>
    public static void Configure(DbContextOptionsBuilder optionsBuilder) {
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, ProviderModelCacheKeyFactory>();
        optionsBuilder.AddInterceptors(AutoTimestampInterceptor.Instance);
    }

    public static void ApplyConventions(ModelBuilder modelBuilder) {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes()) {
            foreach (IMutableProperty property in entity.GetProperties()) {
                if (property.GetColumnType() == "json") {
                    property.SetColumnType(null); // stored as TEXT; JSON_EXTRACT/JSON_SET still work
                }

                // MySQL compares text case-insensitively (e.g. npc model "11001545_M_Bravo" finds
                // animation "11001545_m_bravo"); SQLite's default BINARY collation would not.
                if (property.ClrType == typeof(string) && property.GetCollation() == null) {
                    property.SetCollation("NOCASE");
                }

                Type clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                bool isTimestamp = clrType == typeof(DateTime) || clrType == typeof(DateTimeOffset);
                if (isTimestamp && !property.IsKey() && property.ValueGenerated != ValueGenerated.Never) {
                    property.SetAnnotation(AUTO_TIMESTAMP, property.ValueGenerated == ValueGenerated.OnAdd ? "add" : "always");
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                }

                // SQLite cannot order or compare DateTimeOffset text; store it as a sortable number.
                if (clrType == typeof(DateTimeOffset) && property.GetValueConverter() == null) {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }
            }
        }
    }

    private sealed class ProviderModelCacheKeyFactory : IModelCacheKeyFactory {
        public object Create(DbContext context, bool designTime) {
            return (context.GetType(), context.Database.ProviderName, designTime);
        }

        public object Create(DbContext context) => Create(context, false);
    }

    private sealed class AutoTimestampInterceptor : SaveChangesInterceptor {
        public static readonly AutoTimestampInterceptor Instance = new();

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) {
            Stamp(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
                InterceptionResult<int> result, CancellationToken cancellationToken = default) {
            Stamp(eventData.Context);
            return ValueTask.FromResult(result);
        }

        private static void Stamp(DbContext? context) {
            if (context == null || !context.IsSqlite()) {
                return;
            }

            DateTime now = DateTime.Now;
            foreach (var entry in context.ChangeTracker.Entries()) {
                if (entry.State is not (EntityState.Added or EntityState.Modified)) {
                    continue;
                }

                foreach (var property in entry.Properties) {
                    if (property.Metadata.FindAnnotation(AUTO_TIMESTAMP)?.Value is not string mode) {
                        continue;
                    }

                    try {
                        if (entry.State == EntityState.Added) {
                            // Keep a value the code already chose (e.g. restored creation times).
                            if (property.CurrentValue == null || property.CurrentValue.Equals(DefaultOf(property.Metadata.ClrType))) {
                                property.CurrentValue = NowAs(property.Metadata.ClrType, now);
                            }
                        } else if (mode == "always") {
                            property.CurrentValue = NowAs(property.Metadata.ClrType, now);
                        }
                    } catch (InvalidCastException ex) {
                        throw new InvalidOperationException(
                            $"Auto timestamp failed for {entry.Metadata.DisplayName()}.{property.Metadata.Name} " +
                            $"(type {property.Metadata.ClrType}, comparer {property.Metadata.GetValueComparer().Type})", ex);
                    }
                }
            }
        }

        private static object? DefaultOf(Type type) => type.IsValueType && Nullable.GetUnderlyingType(type) == null ? Activator.CreateInstance(type) : null;

        private static object NowAs(Type type, DateTime now) {
            Type clrType = Nullable.GetUnderlyingType(type) ?? type;
            // Box each branch separately: a plain ternary would convert both to DateTimeOffset.
            return clrType == typeof(DateTimeOffset) ? (object) new DateTimeOffset(now) : (object) now;
        }
    }
}
