using Maple2.Database.Context;
using Maple2.Database.Model;
using Maple2.Tools;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        /// <summary>Stamps the save with the running mode and returns what changed (R12-08).</summary>
        public SaveStamp.Result StampMode(string current) {
            lock (Context) {
                string? mode = Context.OfflineState.Find("Mode")?.Value;
                string? history = Context.OfflineState.Find("ModeHistory")?.Value;
                SaveStamp.Result result = SaveStamp.Next(mode, history, current);
                if (result.Changed) {
                    Set("Mode", result.Mode);
                    Set("ModeChangedAt", GameClock.Now.ToString("O"));
                    Set("ModeHistory", result.History);
                    Context.SaveChanges();
                }
                return result;
            }
        }

        private void Set(string key, string value) {
            OfflineState? row = Context.OfflineState.Find(key);
            if (row == null) {
                Context.OfflineState.Add(new OfflineState { Key = key, Value = value });
            } else {
                row.Value = value;
                Context.Update(row);
            }
        }
    }
}