using Microsoft.Extensions.Logging;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        /// <summary>
        /// Runs every component save inside one transaction. Commits only if all of them succeed; otherwise rolls everything back.
        /// A component fails if it throws, or if any database write made while it ran failed (even if it ignored the result).
        /// Failures are logged once, with the component name.
        /// </summary>
        public bool SaveAtomic(IEnumerable<(string Name, Action<Request> Save)> components, out string? failedComponent, out Exception? error) {
            failedComponent = null;
            error = null;
            if (!IsTransaction) {
                BeginTransaction();
            }

            string current = "begin";
            try {
                foreach ((string name, Action<Request> save) in components) {
                    current = name;
                    int failedBefore = FailedSaves;
                    save(this);
                    if (FailedSaves != failedBefore) {
                        throw new InvalidOperationException($"Database write failed while saving {name}");
                    }
                }

                current = "pending changes";
                Ensure(SaveChanges(), current);
                current = "commit";
                if (!Commit()) {
                    throw new InvalidOperationException("No open transaction to commit");
                }
                return true;
            } catch (Exception ex) {
                failedComponent = current;
                error = ex;
                Logger.LogError(ex, "Save failed in component {Component}; rolling back the whole save", current);
                try {
                    Rollback();
                } catch (Exception rollbackEx) {
                    Logger.LogError(rollbackEx, "Rollback failed after save failure in component {Component}", current);
                }
                return false;
            }
        }
    }
}
