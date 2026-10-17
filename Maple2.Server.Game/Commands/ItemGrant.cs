using System.CommandLine;
using System.CommandLine.IO;
using Maple2.Model;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Game.Commands;

/// <summary>Creates an item for the player and puts it in the inventory or drops it. Shared by /item and /give.</summary>
public static class ItemGrant {
    private const int MAX_RARITY = 6;

    /// <param name="rarity">Negative = the data rarity. Otherwise clamped to 1-6.</param>
    /// <returns>False when nothing could be given; the reason is written to <paramref name="console"/>.</returns>
    public static bool Give(GameSession session, int itemId, int amount, int rarity, bool drop, bool rollMax, IConsole console) {
        if (session.Field is null) return false;
        try {
            rarity = rarity < 0 ? -1 : Math.Clamp(rarity, 1, MAX_RARITY);

            Item? firstItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
            if (firstItem == null) {
                console.Error.WriteLine($"Invalid Item: {itemId}");
                return false;
            }

            if (firstItem.IsCurrency()) {
                firstItem.Amount = amount;
                return AddToInventory(session, firstItem, console);
            }

            bool isNonStackable = firstItem.Metadata.Property.SlotMax == 1;

            if (drop) {
                if (isNonStackable) {
                    // Apply hard cap only when dropping non-stackable items
                    amount = Math.Clamp(amount, 1, 100);

                    session.Field.DropItem(session.Player, firstItem);
                    for (int i = 1; i < amount; i++) {
                        Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                        if (additionalItem == null) {
                            console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                            continue;
                        }
                        session.Field.DropItem(session.Player, additionalItem);
                    }
                } else {
                    // Stackable items can be dropped as a single stack only up to SlotMax
                    firstItem.Amount = Math.Clamp(amount, 1, firstItem.Metadata.Property.SlotMax);
                    session.Field.DropItem(session.Player, firstItem);
                }
                return true;
            }

            if (isNonStackable) {
                int freeSlots = session.Item.Inventory.FreeSlots(firstItem.Inventory);
                if (freeSlots <= 0) {
                    session.Send(ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory));
                    return false;
                }
                if (!AddToInventory(session, firstItem, console)) {
                    return false;
                }

                amount = Math.Clamp(amount, 1, freeSlots);
                // don't need to recalculate free slots, since we are skipping one already
                for (int i = 1; i < amount; i++) {
                    Item? additionalItem = session.Field.ItemDrop.CreateItem(itemId, rarity, rollMax: rollMax);
                    if (additionalItem == null) {
                        console.Error.WriteLine($"Failed to create additional item {i + 1}/{amount}");
                        continue;
                    }
                    if (!AddToInventory(session, additionalItem, console)) {
                        return false;
                    }
                }
                return true;
            }

            firstItem.Amount = amount;
            return AddToInventory(session, firstItem, console);
        } catch (SystemException ex) {
            console.Error.WriteLine(ex.Message);
            return false;
        }
    }

    private static bool AddToInventory(GameSession session, Item item, IConsole console) {
        if (session.Item.Inventory.Add(item, true)) return true;
        session.Item.Inventory.Discard(item);
        console.Error.WriteLine($"Failed to add item:{item.Id} to inventory");
        return false;
    }
}
