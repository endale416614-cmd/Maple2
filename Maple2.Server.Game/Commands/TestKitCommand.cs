using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

/// <summary>
/// /testkit [name]: gives an offline play-testing kit (see OfflineTestKit). Off unless OFFLINE_TESTKIT=true.
/// Enchantment uses the game's own enchant table, one level at a time, exactly like real enchanting.
/// </summary>
public class TestKitCommand : GameCommand {
    private const int MAX_RARITY = 6;
    private const int MAX_ENCHANT = 15;

    private readonly GameSession session;

    public TestKitCommand(GameSession session) : base(AdminPermissions.SpawnItem, "testkit", "Offline play-testing gear kits.") {
        this.session = session;

        var name = new Argument<string?>("name", () => null, "Kit name (omit to list the kits).");
        AddArgument(name);
        this.SetHandler<InvocationContext, string?>(Handle, name);
    }

    private void Handle(InvocationContext ctx, string? name) {
        if (!OfflineTestKit.Enabled) {
            ctx.Console.Error.WriteLine("The test kit is off. Set TestKit=true in launcher.ini and restart the game.");
            return;
        }
        if (string.IsNullOrWhiteSpace(name) || !OfflineTestKit.Kits.TryGetValue(name, out OfflineTestKit.Kit? kit)) {
            ctx.Console.Out.WriteLine("Test kits: " + string.Join(", ", OfflineTestKit.Kits.Select(entry => $"{entry.Key} ({entry.Value.Description})")));
            return;
        }
        if (session.Field is null) {
            return;
        }
        if (session.Item.Inventory.FreeSlots(InventoryType.Gear) < kit.Items.Length) {
            ctx.Console.Error.WriteLine($"Need {kit.Items.Length} free gear slots.");
            return;
        }

        int rarity = Math.Clamp(kit.Rarity, 1, MAX_RARITY);
        int given = 0;
        foreach (int itemId in kit.Items) {
            Item? item = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: kit.RollMax);
            if (item == null) {
                ctx.Console.Error.WriteLine($"Invalid item {itemId}");
                continue;
            }

            Enchant(item, Math.Clamp(kit.Enchant, 0, MAX_ENCHANT));
            if (!session.Item.Inventory.Add(item, true)) {
                session.Item.Inventory.Discard(item);
                ctx.Console.Error.WriteLine($"Could not add item {itemId}");
                continue;
            }
            given++;
        }
        ctx.Console.Out.WriteLine($"Test kit '{name}': {given}/{kit.Items.Length} items, rarity {rarity}, +{kit.Enchant} where enchantable.");
    }

    /// <summary>Applies enchant levels 1..target with the same table and accumulation as ItemEnchantManager.Enchant.</summary>
    private void Enchant(Item item, int target) {
        item.Enchant ??= new ItemEnchant();
        for (int level = item.Enchant.Enchants + 1; level <= target; level++) {
            ItemEnchant step = ItemEnchantManager.GetEnchant(session, item, level);
            if (step.BasicOptions.Count == 0) {
                return; // this slot/rarity/level has no enchant data: leave the item where it is
            }
            foreach ((BasicAttribute attribute, BasicOption option) in step.BasicOptions) {
                item.Enchant.BasicOptions[attribute] = item.Enchant.BasicOptions.TryGetValue(attribute, out BasicOption existing)
                    ? option + existing
                    : option;
            }
            item.Enchant.Enchants = level;
        }
    }
}
