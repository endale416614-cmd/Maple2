using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.Text.RegularExpressions;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Commands;

/// <summary>
/// /equipsweep &lt;ids file&gt; [--ms 800]: strips the non-cosmetic slots once, then equips every id of the file in turn on the
/// session scheduler, one item per --ms. Same gate as /equip. A slot that is still worn is emptied one interval before
/// the next item goes in ("sweep &lt;id&gt; &lt;slot&gt; cleared"), so the driver's reference frame shows that slot empty: comparing with
/// the previous item's frame cannot tell "new item drawn" from "old item gone".
/// One log line per item for a driver that tails the server log: "sweep &lt;id&gt; &lt;slot&gt; equipped", "sweep &lt;id&gt; refused &lt;code&gt;",
/// "sweep &lt;id&gt; not equipment", "sweep &lt;id&gt; failed ..." and "sweep done &lt;n&gt;" at the end.
/// Items pushed back into the inventory by the sweep are deleted so a long sweep does not fill the inventory.
/// </summary>
public class EquipSweepCommand : GameCommand {
    private static readonly ILogger Logger = Log.Logger.ForContext<EquipSweepCommand>();
    // Same line shape as tools/harness: "/item <id> ..." or a bare id per line.
    private static readonly Regex IdLine = new(@"^\s*(?:/item\s+)?(\d{6,})", RegexOptions.Compiled);

    private readonly GameSession session;
    private readonly bool cheatsEnabled;

    public EquipSweepCommand(GameSession session) : this(session, OfflineCheats.Enabled) { }

    public EquipSweepCommand(GameSession session, bool cheatsEnabled) : base(AdminPermissions.None, "equipsweep", "Equip every item id of a file, one per interval, for a visual sweep.") {
        this.session = session;
        this.cheatsEnabled = cheatsEnabled;

        var file = new Argument<string>("file", "Text file with one id (or /item <id> line) per line.");
        var ms = new Option<int>("--ms", () => 800, "Milliseconds between items.");
        AddArgument(file);
        AddOption(ms);
        this.SetHandler<InvocationContext, string, int>(Handle, file, ms);
    }

    private void Handle(InvocationContext ctx, string file, int ms) {
        if (EquipCommand.CheatsOff(cheatsEnabled, ctx)) return;
        if (!File.Exists(file)) {
            ctx.Console.Error.WriteLine($"File not found: {file}");
            ctx.ExitCode = 1;
            return;
        }
        List<int> ids = File.ReadLines(file).Select(line => IdLine.Match(line)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
        if (ids.Count == 0) {
            ctx.Console.Error.WriteLine($"No ids in {file}");
            ctx.ExitCode = 1;
            return;
        }

        int stripped = UnequipCommand.StripAll(session, ctx);
        ctx.Console.Out.WriteLine($"Sweep of {ids.Count} ids started ({stripped} item(s) taken off); watch the server log for 'sweep' lines.");
        Logger.Information("sweep start {Count} ids, {Ms} ms", ids.Count, ms);
        var worn = new HashSet<long>();
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        // The scheduler runs on the session thread, so each step equips like a packet handler would.
        session.Scheduler.Schedule(() => Step(ids, 0, delay, worn), delay);
    }

    private void Step(List<int> ids, int i, TimeSpan delay, HashSet<long> worn, bool cleared = false) {
        if (i >= ids.Count) {
            Logger.Information("sweep done {Count}", ids.Count);
            return;
        }
        int id = ids[i];
        if (!session.ItemMetadata.TryGet(id, out ItemMetadata? metadata)) {
            Logger.Information("sweep {Id} failed: unknown item", id);
        } else if (metadata.SlotNames.Length == 0) {
            Logger.Information("sweep {Id} not equipment", id);
        } else {
            EquipSlot slot = EquipCommand.DataSlot(metadata.SlotNames);
            Item? occupant = session.Item.Equips.Get(slot);
            if (occupant != null && !cleared && session.Item.Equips.Unequip(occupant.Uid)) {
                Logger.Information("sweep {Id} {Slot} cleared", id, slot);
                DeleteDisplaced(worn);
                session.Scheduler.Schedule(() => Step(ids, i, delay, worn, cleared: true), delay);
                return;
            }
            string status = EquipCommand.TryEquip(session, id, -1, new SystemConsole(), out slot, out Item? item);
            if (status == EquipCommand.Equipped) {
                Logger.Information("sweep {Id} {Slot} equipped", id, slot);
                worn.Add(item!.Uid);
            } else {
                Logger.Information("sweep {Id} {Status}", id, status);
                if (item != null) Delete(item);
            }
        }
        DeleteDisplaced(worn);
        session.Scheduler.Schedule(() => Step(ids, i + 1, delay, worn), delay);
    }

    /// <summary>Whatever the sweep pushed out of a slot is back in the inventory: delete it.</summary>
    private void DeleteDisplaced(HashSet<long> worn) {
        foreach (long uid in worn.ToArray()) {
            Item? displaced = session.Item.Inventory.Get(uid);
            if (displaced == null) continue;
            Delete(displaced);
            worn.Remove(uid);
        }
    }

    private void Delete(Item item) {
        if (session.Item.Inventory.Remove(item.Uid, out Item? removed)) {
            session.Item.Inventory.Discard(removed);
        }
    }
}
