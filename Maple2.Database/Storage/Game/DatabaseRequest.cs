using Maple2.Database.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public abstract class DatabaseRequest<TContext>(TContext context, ILogger logger) : IDisposable
    where TContext : DbContext {
    protected readonly TContext Context = context;
    protected readonly ILogger Logger = logger;

    private IDbContextTransaction? transaction;
    private int failedSaves;
    public bool IsTransaction => transaction != null;

    /// <summary>
    /// Number of SaveChanges calls on this request's context that failed since the transaction began.
    /// Lets callers detect failures that storage methods swallow and report only as a false return value.
    /// Concurrency conflicts are not counted, since callers may retry those.
    /// </summary>
    public int FailedSaves => failedSaves;

    public void BeginTransaction() {
        transaction = Context.Database.BeginTransaction();
        Context.SaveChangesFailed -= OnSaveChangesFailed;
        Context.SaveChangesFailed += OnSaveChangesFailed;
    }

    private void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs e) {
        if (e.Exception is not DbUpdateConcurrencyException) {
            Interlocked.Increment(ref failedSaves);
        }
    }

    /// <summary>Throws if a database write reported failure, so a transactional caller aborts instead of committing a partial result.</summary>
    public void Ensure(bool success, string operation) {
        if (!success) {
            throw new InvalidOperationException($"Database write failed: {operation}");
        }
    }

    public bool Commit() {
        if (transaction == null) {
            return false;
        }

        transaction.Commit();
        transaction.Dispose();
        transaction = null; // transaction is completed.
        return true;
    }

    /// <summary>Discards everything written since BeginTransaction. Does nothing if there is no open transaction.</summary>
    public void Rollback() {
        if (transaction == null) {
            return;
        }

        try {
            transaction.Rollback();
        } finally {
            transaction.Dispose();
            transaction = null;
        }
    }

    public bool SaveChanges() {
        return Context.TrySaveChanges();
    }

    public void Dispose() {
        // A transaction that was never committed is abandoned work (an exception or an early return): never commit it implicitly.
        if (transaction != null) {
            Logger.LogWarning("Request disposed with an uncommitted transaction; rolling it back.");
            try {
                Rollback();
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to roll back an uncommitted transaction.");
            }
        }
        Context.Dispose();
    }
}
