using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers.Field;
using Maple2.Server.Game.LuaFunctions;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using static Maple2.Model.Error.ChangeAttributesError;

namespace Maple2.Server.Game.PacketHandlers;

public class ChangeAttributesHandler : FieldPacketHandler {
    public override RecvOp OpCode => RecvOp.ChangeAttributes;

    private enum Command : byte {
        Change = 0,
        Select = 2,
        Unknown5 = 5,
        ForceFill = 6,
    }

    #region Autofac Autowired
    // ReSharper disable MemberCanBePrivate.Global
    public required ItemStatsCalculator ItemStatsCalc { private get; init; }
    // ReSharper restore All
    #endregion

    public override void Handle(GameSession session, IByteReader packet) {
        var command = packet.Read<Command>();
        switch (command) {
            case Command.Change:
                HandleChange(session, packet);
                return;
            case Command.Select:
                HandleSelect(session, packet);
                return;
            case Command.Unknown5:
                HandleUnknown5(session, packet);
                return;
            case Command.ForceFill:
                HandleForceFill(session, packet);
                return;
        }
    }

    // One attempt, as a click: a lock scroll if a line is locked, the price at the item's own count, the roll, the payment. Null when paid and rolled.
    private ChangeAttributesError? Attempt(GameSession session, Item item, LockOption[] locks, ref Item? changeItem) {
        Item? lockItem = null;
        if (locks.Length > 0) {
            lockItem = GetLockConsumeItem(session, item);
            if (lockItem == null) {
                return s_itemremake_error_server_default;
            }
        }

        // Calculate required catalysts.
        var ingredients = new List<IngredientInfo>();
        (string tag1Str, int amount1, string tag2Str, int amount2, string tag3Str, int amount3) = item.Type.IsPet
            ? Lua.CalcGetPetRemakeIngredient(item.TimeChangedOption, item.Rarity, 0)
            : Lua.CalcGetItemRemakeIngredientNew(item.Type.Type, item.TimeChangedOption, item.Rarity, item.Metadata.Limit.Level);
        int discount = RemakeDiscount(session, item);
        if (Enum.TryParse<ItemTag>(tag1Str, out ItemTag tag1)) {
            ingredients.Add(new IngredientInfo(tag1, OnSale(amount1, discount)));
        }
        if (Enum.TryParse<ItemTag>(tag2Str, out ItemTag tag2)) {
            ingredients.Add(new IngredientInfo(tag2, OnSale(amount2, discount)));
        }
        if (Enum.TryParse<ItemTag>(tag3Str, out ItemTag tag3)) {
            ingredients.Add(new IngredientInfo(tag3, OnSale(amount3, discount)));
        }

        // Clone the item so we can preview changes without modifying existing item.
        Item rolled = item.Clone();
        if (rolled.Stats == null) { // This should be impossible, but check again to make linter happy.
            return s_itemremake_error_server_null_status;
        }

        // Randomize attributes. A locked attribute comes back.
        if (!ItemStatsCalc.UpdateRandomOption(ref rolled, locks)) {
            return s_itemremake_error_server_default;
        }

        // Consume required materials.
        if (!session.Item.Inventory.Consume(ingredients)) {
            return s_itemremake_error_server_lack_price;
        }
        if (lockItem != null && !session.Item.Inventory.Consume(lockItem.Uid, 1)) {
            return s_itemremake_error_server_fail_lack_lock_consume_item;
        }

        item.TimeChangedOption++;
        rolled.TimeChangedOption++;
        changeItem = rolled;
        return null;
    }

    // The SaleRemake events the servers list (OfflineRemakeSale in Adapted): the largest discount for every item (kind 0) or for this one's kind.
    private static int RemakeDiscount(GameSession session, Item item) {
        byte kind = item.Type.IsPet ? SaleRemake.Pets : SaleRemake.Equipment;
        return session.FindEvent(GameEventType.SaleRemake).Select(gameEvent => gameEvent.Metadata.Data).OfType<SaleRemake>()
            .Where(sale => sale.Kind == SaleRemake.Everything || sale.Kind == kind).Select(sale => sale.Discount).DefaultIfEmpty(0).Max();
    }

    // Rounded down, as pet_change_talents.msb paid 5, 115 and 120 as 2, 57 and 60 under 5000; a material asked stays at least 1, one not asked (0 below level 50) stays 0.
    public static int OnSale(int amount, int discount) => amount <= 0 || discount <= 0 ? amount : Math.Max(1, amount * (10000 - discount) / 10000);

    private void HandleChange(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        packet.ReadLong();
        bool useLock = packet.ReadBool();

        bool isSpecialAttribute = false;
        short attribute = -1;
        if (useLock) {
            isSpecialAttribute = packet.ReadBool();
            attribute = packet.ReadShort();
        }

        lock (session.Item) {
            // Validate item being changed + lock attributes/lock item if necessary.
            Item? item = session.Item.GetGear(itemUid)
                         ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_not_in_inven));
                return;
            }
            if (!IsValidItem(item)) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                return;
            }
            if (item.Stats == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_null_status));
                return;
            }

            LockOption[] locks = [];
            if (useLock) {
                ItemStats.Option itemOption = item.Stats[ItemStats.Type.Random];
                if (isSpecialAttribute) {
                    if (!itemOption.Special.ContainsKey((SpecialAttribute) attribute)) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                        return;
                    }
                    locks = [new LockOption((SpecialAttribute) attribute, true)];
                } else {
                    if (!itemOption.Basic.ContainsKey((BasicAttribute) attribute)) {
                        session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_impossible));
                        return;
                    }
                    locks = [new LockOption((BasicAttribute) attribute, true)];
                }
            }

            // /autoreroll: a press keeps rolling for a line this item can roll. Any other press is one ordinary attempt.
            LockOption? target = OfflineAutoReroll.Target(session.CharacterId);
            bool keepRolling = target != null && ItemStatsCalc.CanRoll(item, target.Value, locks);
            Item? changeItem = null;
            OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(keepRolling ? OfflineAutoReroll.MaxAttempts : 1,
                () => Attempt(session, item, locks, ref changeItem),
                () => keepRolling && OfflineAutoReroll.HasLine(changeItem!.Stats![ItemStats.Type.Random], target!.Value));
            if (keepRolling) {
                Logger.Debug("Auto reroll: {Attempts} attempts, line found: {Hit}, stopped by {Error}", run.Attempts, run.Hit, run.Error);
            }
            if (run.Attempts == 0) {
                session.Send(ChangeAttributesPacket.Error(run.Error ?? s_itemremake_error_server_default));
                return;
            }

            session.ChangeAttributesItem = changeItem!;
            session.Send(ChangeAttributesPacket.PreviewItem(session.ChangeAttributesItem));

            // As the original, after the preview: the attempts, then the attempts on this one item. Keeping a roll counts nothing (pet_change_talents.msb).
            // A run reports once: the first adds up and the second is a state, so its totals equal one report per attempt, and RankUp gives every grade they cross.
            if (item.Type.IsPet) {
                session.ConditionUpdate(ConditionType.pet_remake_option, counter: run.Attempts);
                session.ConditionUpdate(ConditionType.pet_remake_option_record, counter: item.TimeChangedOption);
            } else {
                session.ConditionUpdate(ConditionType.item_remake_option, counter: run.Attempts);
                session.ConditionUpdate(ConditionType.item_remake_option_record, counter: item.TimeChangedOption);
            }
        }
    }

    private static void HandleSelect(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        if (session.ChangeAttributesItem == null || session.ChangeAttributesItem.Uid != itemUid) {
            session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_fail_apply_option));
            return;
        }

        lock (session.Item) {
            Item? item = session.Item.GetGear(itemUid)
                         ?? session.Item.Inventory.Get(itemUid, InventoryType.Pets);
            if (item == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_not_in_inven));
                return;
            }

            if (item.Stats == null || session.ChangeAttributesItem.Stats == null) {
                session.Send(ChangeAttributesPacket.Error(s_itemremake_error_server_null_status));
                return;
            }

            item.Stats[ItemStats.Type.Random] = session.ChangeAttributesItem.Stats[ItemStats.Type.Random];
            session.ChangeAttributesItem = null;
            session.Send(ChangeAttributesPacket.SelectItem(item));
        }
    }

    private static void HandleUnknown5(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        packet.ReadLong(); // unknown
        bool useLock = packet.ReadBool();
        if (useLock) {
            byte index = packet.ReadByte();
            var attribute = (BasicAttribute) packet.ReadShort();
        }

        packet.ReadBool();
        packet.ReadInt();
    }

    private static void HandleForceFill(GameSession session, IByteReader packet) {
        long itemUid = packet.ReadLong();
        int count = packet.ReadInt();
    }

    // It needs to be epic or better armor and accessories at level 50 and above.
    private static bool IsValidItem(Item item) {
        if (item.Rarity is < Constant.ChangeAttributesMinRarity or > Constant.ChangeAttributesMaxRarity) {
            return false;
        }
        if (!item.Type.IsWeapon && !item.Type.IsArmor && !item.Type.IsAccessory && !item.Type.IsCombatPet) {
            return false;
        }
        if (item.Metadata.Limit.Level < Constant.ChangeAttributesMinLevel) {
            return false;
        }

        return true;
    }

    // TODO: Rather than selecting first time, select best option (e.g. earliest expiring,untradeable)
    public static Item? GetLockConsumeItem(GameSession session, Item changeItem) {
        if (!IsValidItem(changeItem)) {
            return null;
        }

        return session.Item.Inventory.Filter(
            item => !item.IsExpired() && LockScrollFits(item.Metadata.Property.Tag, item.Metadata.Option?.ConstantId, changeItem.Type, changeItem.Rarity),
            InventoryType.Misc).FirstOrDefault();
    }

    /// <summary>Whether a lock scroll serves this gear: its tag names the kind of gear, its data rarity (Option.ConstantId, the Epic, Legendary or Ascendant of its name) the gear's rarity. A pet candy serves every pet.</summary>
    public static bool LockScrollFits(ItemTag scrollTag, int? scrollRarity, ItemType gear, int gearRarity) => scrollTag switch {
        ItemTag.LockItemOptionWeapon => gear.IsWeapon && scrollRarity == gearRarity,
        ItemTag.LockItemOptionArmor => gear.IsArmor && scrollRarity == gearRarity,
        ItemTag.LockItemOptionAccessory => gear.IsAccessory && scrollRarity == gearRarity,
        ItemTag.LockItemOptionPet => gear.IsCombatPet,
        _ => false,
    };
}
