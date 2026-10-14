using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Util;

/// <summary>
/// Repairs gear created by 1.1.5 with the EpicGear option: it raised weapons, armor and accessories to Epic before their
/// stats were built, but the option tables of many items (every class's starter weapon, for one) have no Epic row, so
/// the item came out with no attack or defense. Such an item gets the highest rarity below its current one that its
/// option tables have rows for, and its stats are built again. Items with base stats, outfits (skins) and pets are
/// never touched.
/// </summary>
public static class OfflineGearRepair {
    public static bool IsGear(ItemMetadata metadata) {
        var type = new ItemType(metadata.Id);
        return !metadata.Property.IsSkin && (type.IsWeapon || type.IsArmor || type.IsAccessory);
    }

    // Base stats (weapon attack, defense) are the constant ones; bonus lines can exist without them.
    public static bool HasNoBaseStats(ItemStats? stats) => stats == null || stats[ItemStats.Type.Constant].Count == 0;

    // Rarities at which ItemStatsCalculator can build the item's constant stats: a row in itemoptionconstant, or the
    // pick table row of an item whose constant stats come from the pick table.
    public static IReadOnlyList<int> BuildableRarities(ItemMetadataOption? option, ItemOptionConstantTable constants, ItemOptionPickTable picks) {
        if (option == null) {
            return [];
        }

        var rarities = new SortedSet<int>();
        if (option.ConstantId > 0 && constants.Options.TryGetValue(option.ConstantId, out IReadOnlyDictionary<int, ItemOptionConstant>? byRarity)) {
            rarities.UnionWith(byRarity.Keys);
        }
        if (option.ConstantType == Maple2.Model.Enum.ItemOptionMakeType.Lua && option.PickId > 0 &&
            picks.Options.TryGetValue(option.PickId, out IReadOnlyDictionary<int, ItemOptionPickTable.Option>? pickRarities)) {
            rarities.UnionWith(pickRarities.Keys);
        }

        return rarities.Where(rarity => rarity is > 0 and < 7).ToList();
    }

    /// <summary>The rarity the item should have, or null when it needs no repair.</summary>
    public static int? RepairedRarity(ItemMetadata metadata, int rarity, ItemStats? stats, ItemOptionConstantTable constants, ItemOptionPickTable picks) {
        if (!IsGear(metadata) || !HasNoBaseStats(stats)) {
            return null;
        }

        IReadOnlyList<int> rarities = BuildableRarities(metadata.Option, constants, picks);
        if (rarities.Count == 0 || rarities.Contains(rarity)) {
            return null;
        }

        return rarities.Where(candidate => candidate < rarity).DefaultIfEmpty(rarities[0]).Max();
    }

    /// <summary>The repaired copy of the item (same Uid, slot and everything else), or null when it needs no repair.</summary>
    public static Item? Repair(Item item, ItemStatsCalculator calculator, TableMetadataStorage tables) {
        int? rarity = RepairedRarity(item.Metadata, item.Rarity, item.Stats, tables.ItemOptionConstantTable, tables.ItemOptionPickTable);
        if (rarity == null) {
            return null;
        }

        // Mutate leaves out where the item is kept.
        Item repaired = item.Mutate(item.Metadata, rarity);
        repaired.Slot = item.Slot;
        repaired.Group = item.Group;
        ItemStats? stats = calculator.GetStats(repaired);
        if (HasNoBaseStats(stats)) {
            return null;
        }

        repaired.Stats = stats;
        return repaired;
    }

    /// <summary>Repairs the gear of the given owners (character, account storage) in the save. Returns the number of items repaired.</summary>
    public static int RepairOwners(GameStorage.Request db, ItemStatsCalculator calculator, TableMetadataStorage tables, ILogger logger, params long[] ownerIds) {
        int count = 0;
        foreach (long ownerId in ownerIds) {
            var repaired = new List<Item>();
            foreach (Item item in db.GetAllItems(ownerId)) {
                Item? fixedItem = Repair(item, calculator, tables);
                if (fixedItem == null) {
                    continue;
                }

                logger.Information("Repaired item {ItemUid} ({ItemId}, {Name}): rarity {OldRarity} -> {NewRarity}, no base stats -> {StatCount} constant stats",
                    item.Uid, item.Id, item.Metadata.Name, item.Rarity, fixedItem.Rarity, fixedItem.Stats![ItemStats.Type.Constant].Count);
                repaired.Add(fixedItem);
            }

            if (repaired.Count == 0) {
                continue;
            }
            if (db.SaveItems(ownerId, repaired.ToArray())) {
                count += repaired.Count;
            } else {
                logger.Warning("Could not save {Count} repaired items of owner {OwnerId}", repaired.Count, ownerId);
            }
        }

        if (count > 0) {
            logger.Information("Repaired {Count} items without stats", count);
        }
        return count;
    }
}
