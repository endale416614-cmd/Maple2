using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Serilog;

namespace Maple2.Server.Game.Manager;

public class ItemEnchantManager {
    private const int MaxRate = 100;
    private const int MaxFodderRate = 30;
    private const int MaxExp = 10000;
    private const int ChargeRate = 1;
    private const int MaxEnchants = 15;

    private static readonly int[] RequireFodder = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 3, 3, 4];
    private static readonly int[] GainExp = [10000, 10000, 10000, 5000, 5000, 5000, 2500, 2500, 2500, 2000, 3334, 2000, 2000, 1250, 1250];
    private static readonly int[] SuccessRate = [100, 100, 100, 95, 90, 80, 70, 60, 50, 40, 30, 20, 15, 10, 5];
    private static readonly int[] FodderRate = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 10, 7, 5, 4, 2];
    private static readonly int[] FailCharge = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 4, 4, 5];

    private static readonly IngredientInfo[][] PeachyCost = new IngredientInfo[15][];

    // TODO: Dynamic catalysts
    static ItemEnchantManager() {
        PeachyCost[0] = Build(802, 4, 20); // 1x
        PeachyCost[1] = Build(802, 4, 20); // 1x
        PeachyCost[2] = Build(802, 4, 20); // 1x
        PeachyCost[3] = Build(579, 2, 15); // 2x
        PeachyCost[4] = Build(611, 2, 16); // 2x
        PeachyCost[5] = Build(872, 4, 21); // 2x
        PeachyCost[6] = Build(604, 2, 15); // 4x
        PeachyCost[7] = Build(826, 3, 21); // 4x
        PeachyCost[8] = Build(1133, 3, 29); // 4x
        PeachyCost[9] = Build(2238, 6, 31); // 5x
        PeachyCost[10] = Build(4828, 13, 78); // 3x
        PeachyCost[11] = Build(6148, 16, 66); // 5x
        PeachyCost[12] = Build(7347, 19, 79); // 5x
        PeachyCost[13] = Build(6374, 17, 69); // 8x
        PeachyCost[14] = Build(7784, 20, 84); // 8x

        IngredientInfo[] Build(int onyx, int chaosOnyx, int crystalFragment) {
            return [
                new IngredientInfo(ItemTag.Onix, onyx),
                new IngredientInfo(ItemTag.ChaosOnix, chaosOnyx),
                new IngredientInfo(ItemTag.CrystalPiece, crystalFragment),
            ];
        }
    }

    private readonly GameSession session;
    private readonly ILogger logger = Log.Logger.ForContext<ItemEnchantManager>();

    public EnchantType Type { get; private set; }

    private Item? upgradeItem;
    private readonly List<IngredientInfo> catalysts;
    private readonly Dictionary<long, Item> fodders;
    private readonly Dictionary<BasicAttribute, BasicOption> attributeDeltas;
    private readonly EnchantRates rates;
    private int fodderWeight;
    private int useCharges;
    private EnchantResult enchantResult;

    // Limit break values only
    private long mesoCost;
    private Item? limitBreakItemUpgrade;
    private bool upgraded;

    public ItemEnchantManager(GameSession session) {
        this.session = session;

        catalysts = [];
        fodders = new Dictionary<long, Item>();
        attributeDeltas = new Dictionary<BasicAttribute, BasicOption>();
        rates = new EnchantRates();
    }

    public void Reset() {
        Type = EnchantType.None;
        upgradeItem = null;
        catalysts.Clear();
        fodders.Clear();
        attributeDeltas.Clear();
        rates.Clear();
        fodderWeight = 0;
        useCharges = 0;
        enchantResult = EnchantResult.None;
        mesoCost = 0;
        limitBreakItemUpgrade = null;
        upgraded = false;
    }

    /// <summary>What the window warns of: Ophelia from +9 as captured at +9 and +10, nothing else. Only a warning: 8 captured failures at +9 left the item as it was.</summary>
    public static EnchantFailType FailType(EnchantType type, int enchants) =>
        type is EnchantType.Ophelia && enchants >= 9 ? EnchantFailType.Unstabilize : EnchantFailType.None;

    public bool StageItem(EnchantType enchantType, long itemUid) {
        if (enchantType is not (EnchantType.Ophelia or EnchantType.Peachy)) {
            return false;
        }

        Item? item = session.Item.GetGear(itemUid);
        if (item == null || !item.Metadata.Limit.EnableEnchant) {
            session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_invalid_item));
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_invalid_item);
            return false;
        }

        Reset();
        // +15 is the top: nothing to stage, and the client gets no reply, as it did when this threw.
        if (item.Enchant?.Enchants >= MaxEnchants) {
            return false;
        }

        Type = enchantType;
        upgradeItem = item;

        // Ensure this item has an Enchant field set.
        upgradeItem.Enchant ??= new ItemEnchant();
        int enchants = Math.Clamp(upgradeItem.Enchant.Enchants, 0, 14);
        int minFodder = RequiredFodder(enchants);

        foreach (IngredientInfo ingredient in GetRequiredCatalysts(Type, upgradeItem)) {
            catalysts.Add(ingredient);
        }
        foreach ((BasicAttribute attribute, BasicOption option) in GetEnchant(session, upgradeItem).BasicOptions) {
            attributeDeltas[attribute] = option;
        }

        switch (Type) {
            case EnchantType.Ophelia:
                rates.Success = SuccessRate[enchants];
                break;
            case EnchantType.Peachy:
                rates.Success = MaxRate;
                break;
        }
        NpcTalkEvent(ScriptEventType.EnchantSelect);
        session.Send(ItemEnchantPacket.StageItem(Type, upgradeItem, catalysts, attributeDeltas, rates, minFodder, FailType(Type, enchants)));
        return true;
    }

    public bool UpdateFodder(long itemUid, bool add) {
        if (upgradeItem == null) {
            return false;
        }

        int enchants = Math.Clamp(upgradeItem.Enchant?.Enchants ?? 0, 0, 14);
        if (FodderRate[enchants] <= 0) {
            return false; // If adding fodder does not improve rate, restrict it.
        }

        if (add) {
            // Prevent adding more fodder if it won't help.
            if (Type is EnchantType.Ophelia && rates.Total >= MaxRate) {
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.max_fodder);
                return false;
            }

            // Can't go over 30% rate with fodders
            if (Type is EnchantType.Ophelia && fodders.Count >= RequiredFodder(enchants) && rates.Success + rates.Fodder >= MaxFodderRate) {
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.max_fodder);
                return false;
            }

            // Cannot add the same fodder twice.
            if (fodders.ContainsKey(itemUid)) {
                return false;
            }

            Item? item = session.Item.Inventory.Get(itemUid);
            if (item == null) {
                session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_lack_ingredient);
                return false;
            }
            if (!IsValidFodder(upgradeItem, item)) {
                logger.Debug("Can't add fodder, invalid item: {Id}", item.Id);
                return false;
            }

            fodders.Add(itemUid, item);
            fodderWeight += GetFodderWeight(upgradeItem, item);
        } else {
            if (!fodders.Remove(itemUid, out Item? removed)) {
                return false;
            }

            fodderWeight -= GetFodderWeight(upgradeItem, removed);
        }

        switch (Type) {
            case EnchantType.Ophelia:
                int extra = fodderWeight - RequiredFodder(enchants);
                int maxFodder = Math.Max(0, MaxFodderRate - rates.Success);
                rates.Fodder = Math.Clamp(extra * FodderRate[enchants], 0, maxFodder);

                // Recompute charges in case we are over max.
                SetCharges(useCharges);
                session.Send(ItemEnchantPacket.UpdateCharges(fodders.Keys, useCharges, fodderWeight, rates));
                break;
            case EnchantType.Peachy:
                session.Send(ItemEnchantPacket.UpdateFodder(fodders.Keys));
                break;
        }
        return true;
    }

    public bool UpdateCharges(int count) {
        if (upgradeItem == null || count < 0 || count > upgradeItem.Enchant?.Charges) {
            return false;
        }

        SetCharges(count);
        session.Send(ItemEnchantPacket.UpdateCharges(fodders.Keys, useCharges, fodderWeight, rates));
        return true;
    }

    public bool Enchant(long itemUid) {
        Item? item = session.Item.GetGear(itemUid);
        if (item == null || upgradeItem == null || upgradeItem.Uid != item.Uid) {
            return false;
        }

        upgradeItem.Enchant ??= new ItemEnchant();
        if (upgradeItem.Enchant.Enchants >= MaxEnchants) {
            return false;
        }
        if (OfflineAutoEnchant.Goal(session.CharacterId, upgradeItem.Enchant.Enchants) is { } goal) {
            return AutoEnchant(itemUid, goal);
        }

        int enchants = Math.Clamp(upgradeItem.Enchant.Enchants, 0, 14);
        if (fodderWeight < RequiredFodder(enchants)) {
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.not_enough_fodder);
            return false;
        }

        if (!ConsumeMaterial()) {
            NpcTalkEvent(ScriptEventType.EnchantFail, ItemEnchantError.s_itemenchant_lack_ingredient);
            return false;
        }

        switch (Type) {
            case EnchantType.Ophelia:
                float roll = Random.Shared.NextSingle() * MaxRate;
                int totalRate = rates.Total;
                enchantResult = roll < totalRate ? EnchantResult.Success : EnchantResult.Fail;
                logger.Debug("Enchant result: {Roll} / {Total} = {Result}", roll, totalRate, enchantResult.ToString());

                if (enchantResult == EnchantResult.Success) {
                    ApplyEnchantStats();

                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Success, targetLong: upgradeItem.Enchant.Enchants);
                    session.Send(ItemEnchantPacket.Success(upgradeItem, attributeDeltas));
                    ChatUtil.SendEnchantAnnouncement(session, upgradeItem);
                } else {
                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Fail, targetLong: upgradeItem.Enchant.Enchants);
                    upgradeItem.Enchant.Charges += FailCharge[enchants];
                    session.Send(ItemEnchantPacket.Failure(upgradeItem, FailCharge[enchants]));
                }

                return true;
            case EnchantType.Peachy:
                upgradeItem.Enchant.EnchantExp += GainExp[enchants];
                if (upgradeItem.Enchant.EnchantExp >= MaxExp) {
                    upgradeItem.Enchant.EnchantExp = 0;
                    ApplyEnchantStats();

                    session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) EnchantResult.Success, targetLong: upgradeItem.Enchant.Enchants);
                    session.Send(ItemEnchantPacket.Success(upgradeItem, attributeDeltas));
                    session.Send(ItemEnchantPacket.UpdateExp(itemUid, 0));
                    ChatUtil.SendEnchantAnnouncement(session, upgradeItem);
                    Reset();
                } else {
                    // Just clear the fodder as the same details are reused.
                    fodders.Clear();
                    fodderWeight = 0;

                    session.Send(ItemEnchantPacket.UpdateExp(itemUid, upgradeItem.Enchant.EnchantExp));
                }

                return true;
            default:
                return false;
        }
    }

    private void ApplyEnchantStats() {
        Item item = upgradeItem!;
        ItemEnchant enchant = item.Enchant!;
        // GetBasicOptions() again to ensure rates match those in table.
        // This *MUST* be called before incrementing Enchants.
        foreach ((BasicAttribute attribute, BasicOption option) in GetEnchant(session, item).BasicOptions) {
            if (enchant.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                enchant.BasicOptions[attribute] = option + existing;
            } else {
                enchant.BasicOptions[attribute] = option;
            }
        }
        enchant.Enchants++;
    }

    // /autoenchant: one request runs many attempts, each paid and applied as a single attempt is, and the client gets one result.
    private bool AutoEnchant(long itemUid, int goal) {
        Item item = upgradeItem!;
        ItemEnchant enchant = item.Enchant!;
        var deltas = new Dictionary<BasicAttribute, BasicOption>();
        bool first = true;

        RunStop? Pay() {
            int missing = RequiredFodder(enchant.Enchants) - fodderWeight;
            List<Item> picked = missing <= 0 ? [] : session.Item.Inventory
                .Filter(copy => !fodders.ContainsKey(copy.Uid) && IsCleanCopy(item, copy), InventoryType.Gear)
                .Take(missing)
                .ToList();
            if (picked.Count < missing) {
                return RunStop.Copies;
            }

            foreach (Item copy in picked) {
                fodders[copy.Uid] = copy;
            }
            fodderWeight += picked.Count;
            if (!ConsumeMaterial(quiet: !first)) {
                foreach (Item copy in picked) {
                    fodders.Remove(copy.Uid);
                }
                fodderWeight -= picked.Count;
                return RunStop.Materials;
            }

            // The copies and charges the player picked go on the first attempt only.
            first = false;
            fodders.Clear();
            fodderWeight = 0;
            useCharges = 0;
            rates.Fodder = 0;
            rates.Charge = 0;
            return null;
        }

        void Applied(Attempt attempt) {
            enchant.EnchantExp = attempt.Exp;
            enchant.Charges += attempt.AddCharges;
            if (attempt.LevelUp) {
                foreach ((BasicAttribute attribute, BasicOption option) in GetEnchant(session, item).BasicOptions) {
                    deltas[attribute] = deltas.TryGetValue(attribute, out BasicOption sum) ? option + sum : option;
                }
                ApplyEnchantStats();
                catalysts.Clear();
                catalysts.AddRange(GetRequiredCatalysts(Type, item));
            }
            // As a single attempt: Ophelia per roll, Peachy per level.
            if (attempt.LevelUp || Type == EnchantType.Ophelia) {
                session.ConditionUpdate(ConditionType.enchant_result, codeLong: (int) (attempt.LevelUp ? EnchantResult.Success : EnchantResult.Fail), targetLong: enchant.Enchants);
            }
        }

        RunResult run = AutoRun(Type, enchant.Enchants, enchant.EnchantExp, goal, rates.Fodder + rates.Charge, Random.Shared.NextSingle, Pay, Applied);
        logger.Debug("Auto enchant: {Attempts} attempts, {Levels} levels, now +{Enchants}, stopped by {Stop}", run.Attempts, run.Levels, run.Enchants, run.Stop);
        if (run.Attempts == 0) {
            NpcTalkEvent(ScriptEventType.EnchantFail, run.Stop == RunStop.Copies ? ItemEnchantError.not_enough_fodder : ItemEnchantError.s_itemenchant_lack_ingredient);
            return false;
        }

        if (run.Levels > 0) {
            session.Send(ItemEnchantPacket.Success(item, deltas));
        } else if (Type == EnchantType.Ophelia) {
            session.Send(ItemEnchantPacket.Failure(item, run.AddCharges));
        }
        if (Type == EnchantType.Peachy) {
            session.Send(ItemEnchantPacket.UpdateExp(itemUid, enchant.EnchantExp));
        }
        if (run.Levels > 0) {
            ChatUtil.SendEnchantAnnouncement(session, item);
        }

        if (Type == EnchantType.Ophelia) {
            enchantResult = run.Levels > 0 ? EnchantResult.Success : EnchantResult.Fail;
        } else if (run.Levels > 0) {
            Reset();
        }
        return true;
    }

    /// <summary>Why a /autoenchant run ended.</summary>
    public enum RunStop { Goal, Top, Materials, Copies }

    /// <summary>One attempt's outcome: AddCharges is what a failed Ophelia roll adds, Exp the item's Peachy exp after it.</summary>
    public readonly record struct Attempt(bool LevelUp, int Exp, int AddCharges);

    /// <summary>What a /autoenchant run did. AddCharges sums its failed rolls.</summary>
    public readonly record struct RunResult(int Enchants, int Exp, int Attempts, int Levels, int AddCharges, RunStop Stop);

    /// <summary>The copies an attempt asks for: none below the rank that opens them (constants: EnchantEquipIngredientOpenRank 4; captures: rank 3 at +10 asks 0, rank 5 at +10 asks 2).</summary>
    public static int RequiredFodder(int rarity, int enchants, int openRank) =>
        rarity >= openRank ? RequireFodder[Math.Clamp(enchants, 0, MaxEnchants - 1)] : 0;

    private int RequiredFodder(int enchants) =>
        RequiredFodder(upgradeItem?.Rarity ?? 0, enchants, session.ServerTableMetadata.ConstantsTable.EnchantEquipIngredientOpenRank);

    /// <summary>A copy /autoenchant may spend by itself: not the item, and nothing on it the player put there or could want back.</summary>
    public static bool IsCleanCopy(Item item, Item copy) =>
        copy.Uid != item.Uid && copy.Id == item.Id && copy.Rarity == item.Rarity
        && !copy.IsLocked && !copy.IsExpired() && copy.Amount >= 1
        && copy.TimeChangedOption == 0
        && copy.Enchant is null or { Enchants: 0, EnchantExp: 0, Charges: 0 }
        && copy.LimitBreak is null or { Level: 0 }
        && (copy.Socket == null || copy.Socket.Sockets.All(gem => gem == null))
        && !IsValidToolkit(item, copy);

    /// <summary>One attempt at this level, with nothing paid and nothing applied. bonusRate is what the player's own copies and charges add to the table's rate.</summary>
    public static Attempt Step(EnchantType type, int enchants, int exp, int bonusRate, Func<float> random) {
        if (type == EnchantType.Peachy) {
            int gained = exp + GainExp[enchants];
            return gained >= MaxExp ? new Attempt(true, 0, 0) : new Attempt(false, gained, 0);
        }

        bool success = random() * MaxRate < SuccessRate[enchants] + bonusRate;
        return new Attempt(success, exp, success ? 0 : FailCharge[enchants]);
    }

    /// <summary>
    /// The attempts of one /autoenchant request: until goal, never past +15, or until pay says why the next attempt can not be paid.
    /// pay returns null when the attempt is paid; applied takes each attempt's outcome. bonusRate counts for the first attempt only.
    /// No cap on attempts: every level's rate is at least 5, so a run ends, and materials end it sooner.
    /// </summary>
    public static RunResult AutoRun(EnchantType type, int enchants, int exp, int goal, int bonusRate, Func<float> random, Func<RunStop?> pay, Action<Attempt> applied) {
        int start = enchants;
        int attempts = 0;
        int addCharges = 0;
        RunStop? stop = null;
        while (enchants < goal) {
            stop = enchants >= MaxEnchants ? RunStop.Top : pay();
            if (stop != null) {
                break;
            }

            Attempt attempt = Step(type, enchants, exp, attempts == 0 ? bonusRate : 0, random);
            applied(attempt);
            attempts++;
            exp = attempt.Exp;
            addCharges += attempt.AddCharges;
            if (attempt.LevelUp) {
                enchants++;
            }
        }
        return new RunResult(enchants, exp, attempts, enchants - start, addCharges, stop ?? RunStop.Goal);
    }

    // session.Item must be locked before calling.
    // Consumes materials and charges for this upgrade.
    private bool ConsumeMaterial(bool quiet = false) {
        if (upgradeItem?.Enchant == null) {
            return false;
        }

        lock (session.Item) {
            // Validate charges, fodder, catalyst
            if (upgradeItem.Enchant.Charges < useCharges) {
                return false;
            }
            foreach ((long fodderUid, _) in fodders) {
                // Same conditions Consume(uid, 1) fails on, so no fodder consume can fail once the catalysts are gone
                Item? item = session.Item.Inventory.Get(fodderUid);
                if (item == null || item.IsExpired() || item.Amount < 1) {
                    session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                    return false;
                }
            }

            // Note: All validation should be above this point.
            if (!session.Item.Inventory.Consume(catalysts)) {
                if (!quiet) {
                    session.Send(ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient));
                }
                return false;
            }

            foreach ((long fodderUid, _) in fodders) {
                if (!session.Item.Inventory.Consume(fodderUid, 1)) {
                    logger.Fatal("Failed to consume item {ItemUid}", fodderUid);
                    throw new InvalidOperationException($"Fatal: Consuming item: {fodderUid}");
                }
            }

            upgradeItem.Enchant.Charges -= useCharges;

            return true;
        }
    }

    public static IEnumerable<IngredientInfo> GetRequiredCatalysts(EnchantType type, Item item) {
        int enchants = item.Enchant?.Enchants ?? 0;
        if (enchants >= MaxEnchants) {
            yield break;
        }

        IEnumerable<IngredientInfo> costs = type == EnchantType.Peachy ? PeachyCost[enchants] : Core.Formulas.Enchant.GetEnchantCost(item);
        foreach (IngredientInfo cost in costs) {
            yield return cost;
        }
    }

    public static ItemEnchant GetEnchant(GameSession session, Item upgradeItem, int target = -1) {
        return GetEnchant(session.ServerTableMetadata.EnchantOptionTable, upgradeItem, target);
    }

    public static ItemEnchant GetEnchant(EnchantOptionTable table, Item upgradeItem, int target = -1) {
        int enchantLevel = Math.Clamp(target >= 0 ? target : (upgradeItem.Enchant?.Enchants ?? 0) + 1, 1, 15);
        var itemEnchant = new ItemEnchant();
        EnchantOptionMetadata? entry = table.Entries.Values.FirstOrDefault(
            e => e.Slot == upgradeItem.Type.Type &&
                 e.EnchantLevel == enchantLevel &&
                 e.Rarity == upgradeItem.Rarity &&
                 e.MinLevel <= upgradeItem.Metadata.Limit.Level &&
                 e.MaxLevel >= upgradeItem.Metadata.Limit.Level);
        if (entry == null) {
            return itemEnchant;
        }
        foreach (BasicAttribute attribute in entry.Attributes) {
            itemEnchant.BasicOptions[attribute] = new BasicOption(entry.Rate);
        }
        itemEnchant.Enchants = enchantLevel;
        return itemEnchant;
    }

    // Prevent user from using more charges than needed
    private void SetCharges(int count) {
        // Charges are only relevant to Ophelia.
        if (Type != EnchantType.Ophelia) {
            useCharges = 0;
            return;
        }

        int rateWithoutCharges = Math.Clamp(rates.Total - rates.Charge, 0, MaxRate);
        int maxCharges = (int) Math.Ceiling((MaxRate - rateWithoutCharges) / (float) ChargeRate);
        useCharges = Math.Clamp(count, 0, maxCharges);
        rates.Charge = Math.Clamp(useCharges * ChargeRate, 0, MaxRate);
    }

    private static int GetFodderWeight(Item item, Item fodder) {
        if (!item.Type.IsDagger && !item.Type.IsThrowingStar) {
            return 1;
        }

        // For Dagger/ThrowingStar, Toad's Toolkit is valued as 2 fodder.
        return IsValidToolkit(item, fodder) ? 2 : 1;
    }

    private static bool IsValidFodder(Item item, Item fodder) {
        return fodder.Id == item.Id && fodder.Rarity == item.Rarity || IsValidToolkit(item, fodder);
    }

    private static bool IsValidToolkit(Item item, Item fodder) {
        return fodder.Metadata.Property.Tag switch {
            ItemTag.EnchantJockerItemNormal => item.Rarity == 1,
            ItemTag.EnchantJockerItemRare => item.Rarity == 2,
            ItemTag.EnchantJockerItemElite => item.Rarity == 3,
            ItemTag.EnchantJockerItemExcellent => item.Rarity == 4,
            ItemTag.EnchantJockerItemLegendary => item.Rarity == 5,
            ItemTag.EnchantJockerItemEpic => item.Rarity == 6,
            _ => false,
        };
    }

    public void NpcTalkEvent(ScriptEventType scriptEventType, ItemEnchantError error = ItemEnchantError.s_itemenchant_unknown_err) {
        session.NpcScript?.EnchantResultScript(scriptEventType, enchantResult, error, upgradeItem?.Enchant?.Enchants ?? 0, (short) (upgradeItem?.Rarity ?? 0));
        if (scriptEventType == ScriptEventType.EnchantComplete) {
            Reset();
        }
    }

    #region Limit Break
    public bool StageLimitBreakItem(long itemUid) {
        // Must happen due to this function triggering automatically upon upgrading. Without it, the animation does not play.
        if (upgraded) {
            upgraded = false;
            return false;
        }
        Item? item = session.Item.GetGear(itemUid);
        if (item == null || item.Metadata.Property.LimitBreakMaxLevel == 0 ||
            (item.LimitBreak?.Level == 0 && item.Enchant?.Enchants < 15)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        item.LimitBreak ??= new ItemLimitBreak();
        if (item.LimitBreak.Level >= item.Metadata.Property.LimitBreakMaxLevel) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
            return false;
        }

        Reset();
        upgradeItem = item;

        foreach (IngredientInfo ingredient in Core.Formulas.LimitBreak.GetCatalysts(item.LimitBreak.Level)) {
            catalysts.Add(ingredient);
        }

        mesoCost = Core.Formulas.LimitBreak.MesoCost(item.LimitBreak.Level);

        limitBreakItemUpgrade = item.Clone();

        if (!session.ServerTableMetadata.UnlimitedEnchantOptionTable.Entries.TryGetValue(limitBreakItemUpgrade.Type.Type, out Dictionary<int, UnlimitedEnchantOptionTable.Option>? levelDictionary) ||
            !levelDictionary.TryGetValue(limitBreakItemUpgrade.LimitBreak!.Level + 1, out UnlimitedEnchantOptionTable.Option? optionMetadata)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        foreach ((BasicAttribute attribute, int value) in optionMetadata.Values) {
            if (limitBreakItemUpgrade.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = existing + new BasicOption(value);
            } else {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = new BasicOption(value);
            }
        }
        foreach ((BasicAttribute attribute, float rate) in optionMetadata.Rates) {
            if (limitBreakItemUpgrade.LimitBreak.BasicOptions.TryGetValue(attribute, out BasicOption existing)) {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = existing + new BasicOption(rate);
            } else {
                limitBreakItemUpgrade.LimitBreak.BasicOptions[attribute] = new BasicOption(rate);
            }
        }
        foreach ((SpecialAttribute attribute, int value) in optionMetadata.SpecialValues) {
            if (limitBreakItemUpgrade.LimitBreak.SpecialOptions.TryGetValue(attribute, out SpecialOption existing)) {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = existing + new SpecialOption(value);
            } else {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = new SpecialOption(value);
            }
        }
        foreach ((SpecialAttribute attribute, float rate) in optionMetadata.SpecialRates) {
            if (limitBreakItemUpgrade.LimitBreak.SpecialOptions.TryGetValue(attribute, out SpecialOption existing)) {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = existing + new SpecialOption(rate);
            } else {
                limitBreakItemUpgrade.LimitBreak.SpecialOptions[attribute] = new SpecialOption(rate);
            }
        }

        limitBreakItemUpgrade.LimitBreak.Level++;
        limitBreakItemUpgrade.Enchant!.Enchants = 0;
        session.Send(LimitBreakPacket.StageItem(item, limitBreakItemUpgrade, mesoCost, catalysts));

        return true;
    }

    public bool LimitBreakEnchant(long itemUid) {
        Item? item = session.Item.GetGear(itemUid);
        if (item == null || upgradeItem == null || limitBreakItemUpgrade?.LimitBreak == null || upgradeItem.Uid != item.Uid ||
            item.Metadata.Property.LimitBreakMaxLevel == 0 ||
            (item.LimitBreak?.Level == 0 && item.Enchant?.Enchants < 15)) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_invalid_item));
            return false;
        }

        item.LimitBreak ??= new ItemLimitBreak();
        if (item.LimitBreak.Level >= item.Metadata.Property.LimitBreakMaxLevel) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
            return false;
        }

        if (!ConsumeLimitBreakMaterial()) {
            return false;
        }

        upgradeItem.LimitBreak = limitBreakItemUpgrade.LimitBreak.Clone();
        upgradeItem.Enchant!.Enchants = 0;
        session.Send(LimitBreakPacket.LimitBreak(upgradeItem));
        if (upgradeItem.LimitBreak.Level == upgradeItem.Metadata.Property.LimitBreakMaxLevel) {
            session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_max_unlimited_grade));
        }
        session.ConditionUpdate(ConditionType.unlimited_enchant);
        upgraded = true;
        return true;
    }

    private bool ConsumeLimitBreakMaterial() {
        if (upgradeItem?.LimitBreak == null) {
            return false;
        }

        lock (session.Item) {
            if (session.Currency.CanAddMeso(-mesoCost) != -mesoCost) {
                session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_lack_meso));
                return false;
            }

            if (!session.Item.Inventory.Consume(catalysts)) {
                session.Send(LimitBreakPacket.Error(LimitBreakError.s_unlimited_enchant_err_lack_ingredient));
                return false;
            }

            session.Currency.Meso -= mesoCost;
            return true;
        }
    }
    #endregion
}
