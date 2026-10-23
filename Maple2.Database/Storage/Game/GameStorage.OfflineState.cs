using Maple2.Database.Context;
using Maple2.Database.Model;
using Maple2.Tools;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

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

        /// <summary>In-game clock (T-02): the ticks World saved last and when (Unix milliseconds); 0 and 0 in a new save.</summary>
        public (long Ticks, long SavedAt) GetWorldTicks() {
            lock (Context) {
                long.TryParse(Context.OfflineState.Find("WorldTicks")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks);
                long.TryParse(Context.OfflineState.Find("WorldTicksAt")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long savedAt);
                return (ticks, savedAt);
            }
        }

        public void SaveWorldTicks(long ticks, long savedAt) {
            lock (Context) {
                Set("WorldTicks", ticks.ToString(CultureInfo.InvariantCulture));
                Set("WorldTicksAt", savedAt.ToString(CultureInfo.InvariantCulture));
                Context.SaveChanges();
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