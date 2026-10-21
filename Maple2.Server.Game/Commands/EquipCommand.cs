using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

/// <summary>
/// /equip &lt;id&gt; [--rarity n]: spawns the item like /item and puts it on in its data slot. Same gate as /item.
/// Refusals (level, job, binding, no slot) are printed instead of the client's message box, so a script can read them.
/// Hair, face and face-decor slots go through the normal path, which discards the cosmetic being replaced.
/// </summary>
public class EquipCommand : GameCommand {
    private readonly GameSession session;
    private readonly bool cheatsEnabled;

    public EquipCommand(GameSession session) : this(session, OfflineCheats.Enabled) { }

    /// <param name="cheatsEnabled">For tests: OfflineCheats.Enabled is read once from the environment.</param>
    public EquipCommand(GameSession session, bool cheatsEnabled) : base(AdminPermissions.None, "equip", "Spawn an item and equip it.") {
        this.session = session;
        this.cheatsEnabled = cheatsEnabled;

        var id = new Argument<int>("id", "Id of item to equip.");
        var rarity = new Option<int>(["--rarity", "-r"], () => -1, "Rarity of the item (default: from item data).");
        AddArgument(id);
        AddOption(rarity);
        this.SetHandler<InvocationContext, int, int>(Handle, id, rarity);
    }

    internal static bool CheatsOff(bool enabled, InvocationContext ctx) {
        if (enabled) return false;
        ctx.Console.Error.WriteLine("Cheats are off. Set CheatCommands=true in launcher.ini and restart the game.");
        ctx.ExitCode = 1;
        return true;
    }

    private void Handle(InvocationContext ctx, int itemId, int rarity) {
        if (CheatsOff(cheatsEnabled, ctx)) return;
        ctx.ExitCode = Equip(itemId, rarity, ctx.Console) ? 0 : 1;
    }

    private bool Equip(int itemId, int rarity, IConsole console) {
        string status = TryEquip(session, itemId, rarity, console, out EquipSlot slot, out Item? item);
        if (status != Equipped) {
            console.Error.WriteLine(status);
            return false;
        }
        console.Out.WriteLine($"Equipped {itemId} ({item!.Metadata.Name}) into {slot}.");
        return true;
    }

    internal const string Equipped = "equipped";

    /// <summary>The slot an item is worn in: its first data slot, left hand for off-hand items.</summary>
    internal static EquipSlot DataSlot(EquipSlot[] slots) => slots.Contains(EquipSlot.OH) ? EquipSlot.LH : slots[0];

    /// <summary>Spawns the item and equips it into its data slot. Shared by /equip and /equipsweep.</summary>
    /// <returns><see cref="Equipped"/>, "not equipment", "refused &lt;StringCode&gt; (...)" or "failed ..."; ItemGrant writes its own reason to <paramref name="console"/>.</returns>
    internal static string TryEquip(GameSession session, int itemId, int rarity, IConsole console, out EquipSlot slot, out Item? item) {
        slot = EquipSlot.Unknown;
        item = null;
        if (!ItemGrant.Give(session, itemId, 1, rarity, drop: false, rollMax: false, console)) {
            return "failed /item";
        }
        // Give() does not hand the item back; any unexpired copy of this id and rarity looks the same when worn.
        item = session.Item.Inventory.Find(itemId, rarity < 0 ? -1 : rarity).FirstOrDefault();
        if (item == null) {
            return $"failed: item {itemId} is not in the inventory after /item";
        }

        EquipSlot[] slots = item.Metadata.SlotNames;
        if (slots.Length == 0) {
            return "not equipment";
        }
        slot = DataSlot(slots);

        StringCode reason = EquipManager.ValidateEquipItem(session, item);
        if (reason != StringCode.s_empty_string) {
            return $"refused {reason} (item level {item.Metadata.Limit.Level}, jobs [{string.Join(",", item.Metadata.Limit.JobLimits)}], gender {item.Metadata.Limit.Gender})";
        }
        if (!session.Item.Equips.Equip(item.Uid, slot, item.Metadata.Property.IsSkin)) {
            return $"failed: could not equip {itemId} into {slot} (slot not valid for the item, or no free inventory slot)";
        }
        return Equipped;
    }
}

/// <summary>/unequip [slot]: takes off one slot, or without a slot everything except hair, eyes and face decor.</summary>
public class UnequipCommand : GameCommand {
    // Unequipping these discards the cosmetic instead of returning it to the inventory.
    private static readonly EquipSlot[] Cosmetic = [EquipSlot.HR, EquipSlot.ER, EquipSlot.FA, EquipSlot.FD];

    private readonly GameSession session;
    private readonly bool cheatsEnabled;

    public UnequipCommand(GameSession session) : this(session, OfflineCheats.Enabled) { }

    public UnequipCommand(GameSession session, bool cheatsEnabled) : base(AdminPermissions.None, "unequip", "Take off one slot (CP, CL, ...) or everything but hair and face.") {
        this.session = session;
        this.cheatsEnabled = cheatsEnabled;

        var slot = new Argument<string?>("slot", () => null, "Equip slot name; omit for all non-cosmetic slots.");
        AddArgument(slot);
        this.SetHandler<InvocationContext, string?>(Handle, slot);
    }

    private void Handle(InvocationContext ctx, string? slotName) {
        if (EquipCommand.CheatsOff(cheatsEnabled, ctx)) return;

        if (slotName != null) {
            if (!Enum.TryParse(slotName, ignoreCase: true, out EquipSlot slot) || slot == EquipSlot.Unknown) {
                ctx.Console.Error.WriteLine($"Unknown slot '{slotName}'. Slots: {string.Join(", ", Enum.GetNames<EquipSlot>())}");
                ctx.ExitCode = 1;
                return;
            }
            Item? item = session.Item.Equips.Get(slot);
            if (item == null) {
                ctx.Console.Out.WriteLine($"Nothing in slot {slot}.");
                return;
            }
            ctx.ExitCode = session.Item.Equips.Unequip(item.Uid) ? 0 : 1;
            if (ctx.ExitCode != 0) ctx.Console.Error.WriteLine($"Could not unequip {slot} (inventory full?).");
            return;
        }

        int count = StripAll(session, ctx);
        ctx.Console.Out.WriteLine($"Unequipped {count} item(s).");
    }

    /// <summary>Takes off everything except hair, eyes and face decor. Returns the count; failures go to the console and set exit code 1.</summary>
    internal static int StripAll(GameSession session, InvocationContext ctx) {
        int count = 0;
        EquipManager equips = session.Item.Equips;
        foreach (Item item in equips.Gear.Values.Concat(equips.Outfit.Values).ToArray()) {
            if (Cosmetic.Contains(item.EquipSlot())) continue;
            if (equips.Unequip(item.Uid)) {
                count++;
            } else {
                ctx.Console.Error.WriteLine($"Could not unequip {item.Id} from {item.EquipSlot()} (inventory full?).");
                ctx.ExitCode = 1;
            }
        }
        return count;
    }
}
