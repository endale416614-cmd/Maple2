using System.Diagnostics;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using static Maple2.Model.Error.ItemSocketError;

namespace Maple2.Server.Game.PacketHandlers;

public class ItemSocketHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ItemSocketSystem;

    private enum Command : byte {
        UnlockSocket = 0,
        StageUnlockSocket = 2,
        UpgradeGemstone = 4,
        StageUpgradeGemstone = 6,
        EquipGemstone = 8,
        UnequipGemstone = 10,
        Unknown14 = 14,
        Unknown15 = 15,
        Unknown16 = 16,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemMetadataStorage ItemMetadata { private get; init; }
    public required TableMetadataStorage TableMetadata { private get; init; }
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.UnlockSocket:
                HandleUnlockSocket(session, packet);
                return;
            case Command.StageUnlockSocket:
                HandleStageUnlockSocket(session, packet);
                return;
            case Command.UpgradeGemstone:
                HandleUpgradeGemstone(session, packet);
                return;
            case Command.StageUpgradeGemstone:
                HandleStageUpgradeGemstone(session, packet);
                return;
            case Command.EquipGemstone:
                HandleEquipGemstone(session, packet);
                return;
            case Command.UnequipGemstone:
                HandleUnequipGemstone(session, packet);
                return;
        }
    }

    private void HandleUnlockSocket(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        byte count = packet.ReadByte();
        long[] materialUids = new long[count];
        for (int i = 0; i < count; i++) {
            materialUids[i] = packet.ReadLong();
        }

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }

            if (item.Socket == null || item.Socket.UnlockSlots >= item.Socket.MaxSlots) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_unlock_all));
                return;
            }

            // Gear pays with a copy, and the client will not send a press with none. Outfits, which it sends with none, are not gear: GetGear never returns one.
            // TODO: SO-01: only 0 is refused. The KMS client asks an exact count (2 under ItemSocketExpansion100), which the packed Global client can not confirm.
            if (count == 0) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient_count));
                return;
            }

            foreach (long materialUid in materialUids) {
                Item? material = session.Item.Inventory.Get(materialUid, InventoryType.Gear);
                if (material == null) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                    return;
                }

                if (!IsSocketMaterialEqual(item, material) || !AllSocketsEmpty(material)) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
                    return;
                }
            }

            if (!ConsumeSocketUnlockIngredients(item)) {
                return;
            }

            foreach (long materialUid in materialUids) {
                if (!session.Item.Inventory.Consume(materialUid, amount: 1)) {
                    Logger.Fatal("Failed to consume material: {ItemUid}", materialUid);
                    session.Send(ItemSocketPacket.Error(code: 1, error: s_itemsocketsystem_error_server_default));
                    return;
                }
            }

            item.Socket.UnlockSlots++;
            session.Send(ItemSocketPacket.UnlockSocket(true, item));
            session.ConditionUpdate(ConditionType.socket_unlock, targetLong: item.Socket.UnlockSlots);
            session.ConditionUpdate(ConditionType.socket_unlock_success);
            session.ConditionUpdate(ConditionType.socket_unlock_try);
        }

        #region Local Function
        bool ConsumeSocketUnlockIngredients(Item equip) {
            (int _, string tag, int amount) =
                Lua.CalcItemSocketUnlockIngredient(0, equip.Rarity, (ushort) equip.Metadata.Limit.Level, 0, equip.Metadata.Property.SkinType);

            var ingredient = new IngredientInfo(Enum.Parse<ItemTag>(tag), amount);
            if (!session.Item.Inventory.Consume([ingredient])) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return false;
            }

            return true;
        }
        #endregion
    }

    private void HandleStageUnlockSocket(GameSession session, IByteReader packet) {
        long zero = packet.ReadLong(); // 0
        byte max = packet.ReadByte(); // 0xFF (-1/255)
        Debug.Assert(zero == 0 && max == byte.MaxValue);

        long itemUid = packet.ReadLong();

        Item? item = session.Item.GetGear(itemUid);
        if (item == null) {
            return;
        }

        session.Send(ItemSocketPacket.StageUnlockSocket(item, 100f));
    }

    private void HandleUpgradeGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        sbyte slot = packet.Read<sbyte>(); // -1 if not equipped
        long gemUid = packet.ReadLong();   // this has a client-generated uid when if equipped

        lock (session.Item) {
            if (itemUid != 0) {
                (ItemGemstone gem, GemstoneUpgradeTable.Entry entry)? result = CheckUpgradeGemstoneOnItem(session, itemUid, slot);
                if (result == null) {
                    return;
                }

                List<GemstoneUpgradeTable.Entry> paid = PayGemstoneUpgrades(result.Value.entry);
                if (paid.Count == 0) {
                    return;
                }

                if (!ItemMetadata.TryGet(paid[^1].NextItemId, out ItemMetadata? upgrade)) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                    return;
                }

                result.Value.gem.ItemId = paid[^1].NextItemId;
                result.Value.gem.Stats = ItemStatsCalc.GetGemstoneStats(upgrade);
                session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, true, gemUid, result.Value.gem));
                GemstonesUpgraded(paid);

                RefreshIfWorn(session, itemUid);
            } else {
                (GemstoneUpgradeTable.Entry entry, ItemMetadata upgrade)? result = CheckUpgradeGemstoneInInventory(session, gemUid);
                if (result == null) {
                    return;
                }

                if (session.Item.Inventory.FreeSlots(InventoryType.Gemstone) < 1) {
                    session.Send(ItemSocketPacket.Error(code: 5, error: s_itemsocketsystem_error_server_default));
                    return;
                }

                List<GemstoneUpgradeTable.Entry> paid = PayGemstoneUpgrades(result.Value.entry);
                if (paid.Count == 0) {
                    return;
                }

                // One remove and one add however many tiers were paid, so the gem never stacks into a tier it only passes.
                if (!ItemMetadata.TryGet(paid[^1].NextItemId, out ItemMetadata? upgrade) || !session.Item.Inventory.Remove(gemUid, out Item? gem, amount: 1)) {
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                    return;
                }

                // Mutate keeps the stats, and these are the tier's. Set before Add, whose packet carries them.
                Item upgradeGem = gem.Mutate(upgrade);
                upgradeGem.Stats = ItemStatsCalc.GetGemstoneStats(upgrade);
                if (!session.Item.Inventory.Add(upgradeGem, true)) {
                    Logger.Fatal("Failed to add upgraded gem {ItemUid} to inventory", upgradeGem.Uid);
                    session.Send(ItemSocketPacket.Error(code: 6, error: s_itemsocketsystem_error_server_default));
                    return;
                }

                session.Send(ItemSocketPacket.UpgradeGemstone(itemUid, true, upgradeGem));
                GemstonesUpgraded(paid);
            }
        }

        #region Local Function
        // One tier, unless /autoenchant names a higher one. Each tier pays what a click pays, and only a tier that is an item is paid for.
        // Only a press that paid nothing says so: a run that paid something ends with its result.
        List<GemstoneUpgradeTable.Entry> PayGemstoneUpgrades(GemstoneUpgradeTable.Entry first) {
            int goal = OfflineAutoEnchant.Goal(session.CharacterId, first.Level) ?? first.Level + 1;
            List<GemstoneUpgradeTable.Entry> paid = GemstoneUpgradeRun(TableMetadata.GemstoneUpgradeTable.Entries, first, goal, entry =>
                ItemMetadata.TryGet(entry.NextItemId, out _)
                && session.Item.Inventory.Consume(entry.Ingredients.Select(ingredient => new IngredientInfo(ingredient.ItemTag, ingredient.Amount)).ToArray()));
            if (paid.Count == 0) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient_count));
            }

            return paid;
        }

        // Per tier, as a click fires them: the trophy for reaching tier 10 reads the tier, and the counters count upgrades.
        void GemstonesUpgraded(List<GemstoneUpgradeTable.Entry> paid) {
            foreach (GemstoneUpgradeTable.Entry entry in paid) {
                session.ConditionUpdate(ConditionType.gemstone_upgrade, targetLong: entry.Level + 1);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_try);
                session.ConditionUpdate(ConditionType.gemstone_upgrade_success);
            }
        }
        #endregion
    }

    private void HandleStageUpgradeGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        sbyte slot = packet.Read<sbyte>(); // -1 if not equipped
        long gemUid = packet.ReadLong();   // this has a client-generated uid when if equipped

        if (itemUid != 0) {
            if (CheckUpgradeGemstoneOnItem(session, itemUid, slot) == null) {
                return;
            }
        } else {
            if (CheckUpgradeGemstoneInInventory(session, gemUid) == null) {
                return;
            }
        }

        session.Send(ItemSocketPacket.StageUpgradeGemstone(itemUid, slot, gemUid, 100f));
    }

    private void HandleEquipGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        long gemUid = packet.ReadLong();
        byte socketSlot = packet.ReadByte();

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }
            Item? gem = session.Item.Inventory.Get(gemUid, InventoryType.Gemstone);
            if (gem == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                return;
            }

            SocketState state = CheckSocketSlot(item.Socket, socketSlot);
            switch (state) {
                case SocketState.None:
                    session.Send(ItemSocketPacket.Error(code: 8, error: s_itemsocketsystem_error_server_default));
                    return;
                case SocketState.Locked:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock));
                    return;
                case SocketState.Used:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_used));
                    return;
            }

            Debug.Assert(state == SocketState.Empty);
            if (!session.Item.Inventory.Consume(gemUid, amount: 1)) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
                return;
            }

            // item.Socket cannot be null after CheckSocketSlot()
            var itemGemstone = new ItemGemstone(gem.Id, gem.Binding, gem.Stats, gem.IsLocked, gem.UnlockTime);
            item.Socket!.Sockets[socketSlot] = itemGemstone;
            session.Send(ItemSocketPacket.EquipGemstone(item.Uid, socketSlot, itemGemstone));
            session.ConditionUpdate(ConditionType.gemstone_puton);
            RefreshIfWorn(session, item.Uid);
        }
    }

    private void HandleUnequipGemstone(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        byte socketSlot = packet.ReadByte();

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid);
            if (item == null) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
                return;
            }

            SocketState state = CheckSocketSlot(item.Socket, socketSlot);
            switch (state) {
                case SocketState.None:
                    session.Send(ItemSocketPacket.Error(code: 10, error: s_itemsocketsystem_error_server_default));
                    return;
                case SocketState.Locked:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_lock));
                    return;
                case SocketState.Empty:
                    session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_empty));
                    return;
            }

            Debug.Assert(state == SocketState.Used);
            // item.Socket cannot be null after CheckSocketSlot()
            // itemGemstone cannot be null if SocketState.Used
            ItemGemstone itemGemstone = item.Socket!.Sockets[socketSlot]!;
            if (!ConsumeGemstoneUnequipIngredients(itemGemstone)) {
                return;
            }

            Item? gem = session.Field?.ItemDrop.CreateItem(itemGemstone.ItemId, rarity: Constant.GemstoneGrade);
            if (gem == null) {
                session.Send(ItemSocketPacket.Error(11, error: s_itemsocketsystem_error_server_default));
                return;
            }

            if (!session.Item.Inventory.Add(gem, true)) {
                return; // Failed to add to inventory
            }

            item.Socket!.Sockets[socketSlot] = null;
            session.Send(ItemSocketPacket.UnequipGemstone(item.Uid, socketSlot));
            session.ConditionUpdate(ConditionType.gemstone_putoff);
            RefreshIfWorn(session, item.Uid);
        }

        #region Local Function
        bool ConsumeGemstoneUnequipIngredients(ItemGemstone gemstone) {
            if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gemstone.ItemId, out GemstoneUpgradeTable.Entry? entry)) {
                session.Send(ItemSocketPacket.Error(code: 12, error: s_itemsocketsystem_error_server_default));
                return false;
            }

            (string tag, int amount) = Lua.CalcGetGemStonePutOffPrice(Constant.GemstoneGrade, (ushort) entry.Level, 0);
            var ingredient = new IngredientInfo(Enum.Parse<ItemTag>(tag), amount);
            if (!session.Item.Inventory.Consume([ingredient])) {
                session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_lack_price));
                return false;
            }

            return true;
        }
        #endregion
    }

    private (ItemGemstone, GemstoneUpgradeTable.Entry)? CheckUpgradeGemstoneOnItem(GameSession session, long itemUid, sbyte slot) {
        ItemSocket? itemSocket = session.Item.GetGear(itemUid)?.Socket;
        if (itemSocket == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target));
            return null;
        }
        if (slot < 0 || slot >= itemSocket.UnlockSlots) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_server_default));
            return null;
        }

        ItemGemstone? gem = itemSocket.Sockets[slot];
        if (gem == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_socket_empty));
            return null;
        }

        if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gem.ItemId, out GemstoneUpgradeTable.Entry? entry)) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
            return null;
        }
        if (entry.NextItemId == 0) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_gemstone_maxlevel));
            return null;
        }

        return (gem, entry);
    }

    private (GemstoneUpgradeTable.Entry, ItemMetadata)? CheckUpgradeGemstoneInInventory(GameSession session, long gemUid) {
        Item? gem = session.Item.Inventory.Get(gemUid, InventoryType.Gemstone);
        if (gem == null) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_gemstone));
            return null;
        }

        if (!TableMetadata.GemstoneUpgradeTable.Entries.TryGetValue(gem.Id, out GemstoneUpgradeTable.Entry? entry)) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_invalid_target_ingredient));
            return null;
        }
        if (entry.NextItemId == 0) {
            session.Send(ItemSocketPacket.Error(error: s_itemsocketsystem_error_gemstone_maxlevel));
            return null;
        }
        if (!ItemMetadata.TryGet(entry.NextItemId, out ItemMetadata? upgrade)) {
            session.Send(ItemSocketPacket.Error(code: 4, error: s_itemsocketsystem_error_server_default));
            return null;
        }

        return (entry, upgrade);
    }

    /// <summary>
    /// The upgrades one press buys, in order: until the gem's tier reaches goal or its top, or until pay says no. A gem upgrade has no roll (the stage says 100 and
    /// both replies pass success), so every upgrade paid lands, and the last entry's NextItemId is the gem's new id.
    /// </summary>
    public static List<GemstoneUpgradeTable.Entry> GemstoneUpgradeRun(IReadOnlyDictionary<int, GemstoneUpgradeTable.Entry> table, GemstoneUpgradeTable.Entry first, int goal, Func<GemstoneUpgradeTable.Entry, bool> pay) {
        var paid = new List<GemstoneUpgradeTable.Entry>();
        GemstoneUpgradeTable.Entry? entry = first;
        while (entry is { NextItemId: not 0 } && entry.Level < goal && pay(entry)) {
            paid.Add(entry);
            entry = table.GetValueOrDefault(entry.NextItemId);
        }

        return paid;
    }

    private static bool IsSocketMaterialEqual(Item item, Item material) {
        return item.Id == material.Id
               && item.Rarity == material.Rarity
               && item.Socket != null
               && material.Socket != null
               && item.Socket.UnlockSlots == material.Socket.UnlockSlots;
    }

    private static bool AllSocketsEmpty(Item material) {
        return material.Socket == null || material.Socket.Sockets.All(gem => gem == null);
    }

    // GetGear looks in the bag and on the player, so gear not in the bag is worn: its totals change now, not at the next rebuild.
    private static void RefreshIfWorn(GameSession session, long itemUid) {
        if (session.Item.Inventory.Get(itemUid, InventoryType.Gear) == null) {
            session.Stats.Refresh();
        }
    }

    public enum SocketState { None, Locked, Used, Empty }

    /// <summary>What a slot names. Slots count from 0 and Sockets holds only the open ones, so the first locked slot is UnlockSlots and MaxSlots is past the last.</summary>
    public static SocketState CheckSocketSlot(ItemSocket? itemSocket, byte slot) {
        if (itemSocket == null || slot >= itemSocket.MaxSlots) {
            return SocketState.None;
        }
        if (slot >= itemSocket.UnlockSlots) {
            return SocketState.Locked;
        }
        return itemSocket.Sockets[slot] != null ? SocketState.Used : SocketState.Empty;
    }
}
