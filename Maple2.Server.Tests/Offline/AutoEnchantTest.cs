using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// EN-01 (BT-174, R-21): /autoenchant's run, its stops and the rules it spends by. Golden: Ophelia_1_to_10_exc.msb (item 15050293, rank 3) and
/// Peachy_1_to_10.msb (item 15050298, rank 5): rates and hits +0 to +10, copies asked at +10, the charge a failed roll adds.
/// </summary>
public class AutoEnchantTest {
    private const float AlwaysFail = 0.999f;
    private const float AlwaysHit = 0f;
    private const int OpenRank = 4; // constants: EnchantEquipIngredientOpenRank

    private static readonly int[] OpheliaRate = [100, 100, 100, 95, 90, 80, 70, 60, 50, 40, 30, 20, 15, 10, 5];
    private static readonly int[] PeachyHits = [1, 1, 1, 2, 2, 2, 4, 4, 4, 5, 3, 5, 5, 8, 8];
    private static readonly int[] FailCharge = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 3, 4, 4, 5];

    private static Func<float> Rolls(params float[] rolls) {
        var queue = new Queue<float>(rolls);
        return queue.Dequeue;
    }

    // Pays this many attempts, then says why not.
    private static Func<ItemEnchantManager.RunStop?> Pays(int attempts, ItemEnchantManager.RunStop then = ItemEnchantManager.RunStop.Materials) {
        return () => attempts-- > 0 ? null : then;
    }

    private static ItemEnchantManager.RunResult Ophelia(int enchants, int goal, Func<float> random, Func<ItemEnchantManager.RunStop?>? pay = null, int bonusRate = 0) {
        return ItemEnchantManager.AutoRun(EnchantType.Ophelia, enchants, 0, goal, bonusRate, random, pay ?? Pays(1000), _ => { });
    }

    [Test]
    public void NextLevel_StopsAtTheLevel_AndSumsTheCharges() {
        var applied = new List<ItemEnchantManager.Attempt>();
        ItemEnchantManager.RunResult run = ItemEnchantManager.AutoRun(EnchantType.Ophelia, 9, 0, 10, 0, Rolls(AlwaysFail, AlwaysFail, AlwaysHit), Pays(1000), applied.Add);

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(10, 0, 3, 1, 2, ItemEnchantManager.RunStop.Goal)));
        // enchant_result fires from applied, once per attempt.
        Assert.That(applied.Select(attempt => attempt.LevelUp), Is.EqualTo(new[] { false, false, true }));
    }

    [Test]
    public void Target_RunsPastTheNextLevel() {
        ItemEnchantManager.RunResult run = Ophelia(9, 12, Rolls(AlwaysHit, AlwaysFail, AlwaysHit, AlwaysHit));

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(12, 0, 4, 3, FailCharge[10], ItemEnchantManager.RunStop.Goal)));
    }

    [Test]
    public void Materials_StopTheRun_WithTheAttemptsPaid() {
        ItemEnchantManager.RunResult run = Ophelia(10, 11, Rolls(AlwaysFail, AlwaysFail, AlwaysHit), Pays(2));

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(10, 0, 2, 0, 2 * FailCharge[10], ItemEnchantManager.RunStop.Materials)));
    }

    [Test]
    public void Copies_StopTheRun_WithTheAttemptsPaid() {
        ItemEnchantManager.RunResult run = Ophelia(11, 12, Rolls(AlwaysFail), Pays(1, ItemEnchantManager.RunStop.Copies));

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(11, 0, 1, 0, FailCharge[11], ItemEnchantManager.RunStop.Copies)));
    }

    [Test]
    public void NothingPaid_IsNoAttempt() {
        ItemEnchantManager.RunResult run = Ophelia(10, 11, Rolls(), Pays(0));

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(10, 0, 0, 0, 0, ItemEnchantManager.RunStop.Materials)));
    }

    [Test]
    public void ChargesOfEveryLevel_AreSummed_WhenTheRunStopsAfterALevel() {
        ItemEnchantManager.RunResult run = Ophelia(10, 15, Rolls(AlwaysFail, AlwaysHit, AlwaysFail, AlwaysFail), Pays(4));

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(11, 0, 4, 1, FailCharge[10] + 2 * FailCharge[11], ItemEnchantManager.RunStop.Materials)));
    }

    [Test]
    public void NeverPastFifteen() {
        ItemEnchantManager.RunResult atFourteen = Ophelia(14, 99, Rolls(AlwaysHit));
        Assert.That(atFourteen, Is.EqualTo(new ItemEnchantManager.RunResult(15, 0, 1, 1, 0, ItemEnchantManager.RunStop.Top)));

        ItemEnchantManager.RunResult atFifteen = Ophelia(15, 16, Rolls(), () => throw new AssertionException("nothing is paid at +15"));
        Assert.That(atFifteen, Is.EqualTo(new ItemEnchantManager.RunResult(15, 0, 0, 0, 0, ItemEnchantManager.RunStop.Top)));
    }

    [Test]
    public void TheBonus_CountsForTheFirstAttemptOnly() {
        // +14 is 5 %. With 10 from the player's own copies and charges a roll of 14 hits, on the first attempt and not after it.
        Assert.That(Ophelia(14, 15, Rolls(0.14f), bonusRate: 10).Attempts, Is.EqualTo(1));
        Assert.That(Ophelia(14, 15, Rolls(0.99f, 0.14f, 0.04f), bonusRate: 10).Attempts, Is.EqualTo(3));
    }

    [Test]
    public void GoldenOdds_ByLevel() {
        for (int level = 0; level < OpheliaRate.Length; level++) {
            int rate = OpheliaRate[level];
            ItemEnchantManager.Attempt below = ItemEnchantManager.Step(EnchantType.Ophelia, level, 0, 0, () => (rate - 0.01f) / 100f);
            Assert.That(below.LevelUp, Is.True, $"+{level} below {rate}");
            Assert.That(below.AddCharges, Is.EqualTo(0));
            if (rate < 100) {
                ItemEnchantManager.Attempt at = ItemEnchantManager.Step(EnchantType.Ophelia, level, 0, 0, () => rate / 100f);
                Assert.That(at.LevelUp, Is.False, $"+{level} at {rate}");
                Assert.That(at.AddCharges, Is.EqualTo(FailCharge[level]), $"+{level} charges");
            }
        }
    }

    [Test]
    public void GoldenPeachyHits_ByLevel() {
        for (int level = 0; level < PeachyHits.Length; level++) {
            ItemEnchantManager.RunResult run = ItemEnchantManager.AutoRun(EnchantType.Peachy, level, 0, level + 1, 0, () => throw new AssertionException("Peachy rolls nothing"), Pays(1000), _ => { });
            Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(level + 1, 0, PeachyHits[level], 1, 0, ItemEnchantManager.RunStop.Goal)), $"+{level}");
        }
    }

    [Test]
    public void Peachy_KeepsItsExp_WhenMaterialsRunOut() {
        // Capture: +9 gains 2000 a hit.
        ItemEnchantManager.RunResult run = ItemEnchantManager.AutoRun(EnchantType.Peachy, 9, 0, 10, 0, Rolls(), Pays(2), _ => { });

        Assert.That(run, Is.EqualTo(new ItemEnchantManager.RunResult(9, 4000, 2, 0, 0, ItemEnchantManager.RunStop.Materials)));
    }

    [TestCase(3, 10, 0, TestName = "Capture: rank 3 at +10 asks 0")]
    [TestCase(5, 10, 2, TestName = "Capture: rank 5 at +10 asks 2")]
    [TestCase(4, 10, 2, TestName = "Rank 4 is the first rank asked")]
    [TestCase(1, 14, 0, TestName = "Rank 1 at +14 asks 0")]
    [TestCase(3, 14, 0, TestName = "Rank 3 at +14 asks 0")]
    [TestCase(4, 9, 0, TestName = "Rank 4 at +9 asks 0")]
    [TestCase(6, 12, 3, TestName = "Rank 6 at +12 asks 3")]
    [TestCase(6, 14, 4, TestName = "Rank 6 at +14 asks 4")]
    [TestCase(6, 15, 4, TestName = "+15 does not index past the table")]
    public void CopiesAsked(int rarity, int enchants, int expected) {
        Assert.That(ItemEnchantManager.RequiredFodder(rarity, enchants, OpenRank), Is.EqualTo(expected));
    }

    private static ItemMetadata Metadata(int id = 15050293, int level = 50, ItemTag tag = ItemTag.None) {
        var property = new ItemMetadataProperty(false, 0, 1, 0, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, level, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private static Item Gear(long uid, int rarity = 4, ItemMetadata? metadata = null) => new(metadata ?? Metadata(), rarity) { Uid = uid };

    [Test]
    public void CleanCopy_SameIdAndRarity() {
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(2)), Is.True);
    }

    [Test]
    public void CleanCopy_NotTheItemItself() {
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(1)), Is.False);
    }

    [Test]
    public void CleanCopy_NotAnotherIdOrRarity() {
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(2, 4, Metadata(15050294))), Is.False);
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(2, 5)), Is.False);
    }

    [Test]
    public void CleanCopy_NotLocked() {
        Item copy = Gear(2);
        copy.IsLocked = true;
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [Test]
    public void CleanCopy_NoGems() {
        Item copy = Gear(2);
        copy.Socket = new ItemSocket(3, 1);
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.True, "an empty socket");
        copy.Socket.Sockets[0] = new ItemGemstone(40200001, stats: new ItemStats());
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [TestCase(1, 0, 0, TestName = "A copy at +1 is not clean")]
    [TestCase(0, 2500, 0, TestName = "A copy with Peachy exp is not clean")]
    [TestCase(0, 0, 3, TestName = "A copy with charges is not clean")]
    public void CleanCopy_PlusZeroWithNothingOnIt(int enchants, int exp, int charges) {
        Item copy = Gear(2);
        copy.Enchant = new ItemEnchant(enchants, exp, charges: charges);
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [Test]
    public void CleanCopy_NoLimitBreak() {
        Item copy = Gear(2);
        copy.LimitBreak = new ItemLimitBreak();
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.True, "level 0");
        copy.LimitBreak.Level = 1;
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [Test]
    public void CleanCopy_NeverRerolled() {
        Item copy = Gear(2);
        copy.TimeChangedOption = 1;
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [Test]
    public void CleanCopy_NotExpired() {
        Item copy = Gear(2);
        copy.ExpiryTime = 1;
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), copy), Is.False);
    }

    [Test]
    public void CleanCopy_NoToolkit() {
        ItemMetadata toolkit = Metadata(tag: ItemTag.EnchantJockerItemExcellent);
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(2, 4, toolkit)), Is.False, "same id");
        Assert.That(ItemEnchantManager.IsCleanCopy(Gear(1), Gear(2, 4, Metadata(40100099, 0, ItemTag.EnchantJockerItemExcellent))), Is.False, "the real kind");
    }

    [TestCase(EnchantType.Ophelia)]
    [TestCase(EnchantType.Peachy)]
    public void StagingAtFifteen_AsksNothing_AndDoesNotThrow(EnchantType type) {
        Item item = Gear(1);
        item.Enchant = new ItemEnchant(15);
        Assert.That(ItemEnchantManager.GetRequiredCatalysts(type, item), Is.Empty);
    }

    [TestCase(EnchantType.Ophelia)]
    [TestCase(EnchantType.Peachy)]
    public void AtFourteen_ItStillAsks(EnchantType type) {
        Item item = Gear(1);
        item.Enchant = new ItemEnchant(14);
        Assert.That(ItemEnchantManager.GetRequiredCatalysts(type, item), Is.Not.Empty);
    }

    [TestCase(0, 9, 10, TestName = "Next level")]
    [TestCase(0, 14, 15, TestName = "Next level at +14")]
    [TestCase(12, 9, 12, TestName = "A target above")]
    [TestCase(15, 14, 15, TestName = "The top target")]
    public void Goal_RunsTo(int target, int enchants, int expected) {
        Assert.That(OfflineAutoEnchant.Goal(target, enchants), Is.EqualTo(expected));
    }

    [TestCase(10, 10, TestName = "Target reached: one ordinary attempt")]
    [TestCase(10, 12, TestName = "Target passed: one ordinary attempt")]
    public void Goal_NoneOnceTheTargetIsReached(int target, int enchants) {
        Assert.That(OfflineAutoEnchant.Goal(target, enchants), Is.Null);
    }

    [Test]
    public void Set_TogglesAndTakesATarget() {
        const long character = -174001;
        Assert.That(OfflineAutoEnchant.Set(character, null), Is.EqualTo(0), "off until typed; the first toggle turns it on");
        Assert.That(OfflineAutoEnchant.Set(character, null), Is.Null, "the second turns it off");
        Assert.That(OfflineAutoEnchant.Set(character, 12), Is.EqualTo(12));
        Assert.That(OfflineAutoEnchant.Set(character, 13), Is.EqualTo(13), "a new target stays on");
        Assert.That(OfflineAutoEnchant.Set(character, null), Is.Null);
    }

    [Test]
    public void Message_PrintsTheStateAndTheTarget() {
        Assert.That(OfflineAutoEnchant.Message(null), Does.Contain("OFF"));
        Assert.That(OfflineAutoEnchant.Message(0), Does.Contain("ON").And.Contain("next level").And.Contain("materials"));
        Assert.That(OfflineAutoEnchant.Message(12), Does.Contain("ON").And.Contain("+12").And.Contain("materials"));
    }

    [Test]
    public void Message_SaysWhatAGemDoes() {
        Assert.That(OfflineAutoEnchant.Message(0), Does.Contain("gem upgrade stays one tier per press"));
        Assert.That(OfflineAutoEnchant.Message(5), Does.Contain("gem upgrade runs the gem to tier 5 or its highest tier"));
        Assert.That(OfflineAutoEnchant.Message(12), Does.Contain("tier 12 or its highest tier"), "no gem has tier 12: the run goes to the gem's highest");
    }

    [TestCase("Legacy", null, false, TestName = "Legacy: the command is off")]
    [TestCase("Adapted", null, true, TestName = "Adapted: the command is on")]
    [TestCase(null, null, true, TestName = "No mode: the command is on")]
    public void TheFlag_FollowsTheMode(string? mode, string? flag, bool expected) {
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Util", "OfflineAutoEnchant.cs"));
        Assert.That(source, Does.Contain("OfflineProfile.Flag(\"OFFLINE_AUTOENCHANT\", true)"));
        Assert.That(OfflineProfile.Resolve(mode, flag, adaptedDefault: true), Is.EqualTo(expected));
    }

    [Test]
    public void Command_PrintsThatItIsOff() {
        var console = new TestConsole();
        int code = new AutoEnchantCommand(null!, enabled: false).Invoke(["autoenchant"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("off in Legacy"));
    }

    [TestCase("0")]
    [TestCase("16")]
    [TestCase("-1")]
    public void Command_RefusesAnotherTarget(string target) {
        var console = new TestConsole();
        int code = new AutoEnchantCommand(null!, enabled: true).Invoke(["autoenchant", target], console);
        Assert.That(code, Is.Not.EqualTo(0));
        Assert.That(console.Error.ToString(), Is.Not.Empty);
    }

    [Test]
    public void Command_RejectsANonNumericTarget() {
        var console = new TestConsole();
        int code = new AutoEnchantCommand(null!, enabled: false).Invoke(["autoenchant", "high"], console);
        Assert.That(code, Is.Not.EqualTo(0));
        Assert.That(console.Error.ToString(), Does.Contain("high"));
    }
}

/// <summary>EN-01: a press on a session with no server or database: what a run pays, what stays in the bag, and the one result the client gets.</summary>
public class AutoEnchantRunTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Upgrade = 1;
    private const int ItemId = 15050298;
    private const byte Stage = 5, Exp = 6, Fodder = 8, Success = 10, Failure = 11, Error = 12;
    private static readonly ItemTag[] Materials = [ItemTag.Onix, ItemTag.ChaosOnix, ItemTag.CrystalPiece];
    private static long lastCharacter = -174100;

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetProperty(object target, string name, object? value) {
        target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static ItemMetadata Meta(int id, int type, ItemTag tag, int slotMax) {
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 50, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    private sealed class Rig {
        public required ItemEnchantManager Manager { get; init; }
        public required ItemCollection Bag { get; init; }
        public required ItemCollection Catalysts { get; init; }
        public required Item Item { get; init; }
        public required BlockingCollection<(byte[], int)> Queue { get; init; }
        public required long Character { get; init; }
        public required int[] Given { get; init; }
        public required IngredientInfo[] Cost { get; init; }
        public byte[] StagePacket { get; set; } = [];

        // The enchant packets sent since the last call, as full packets: op (2 bytes), command, the rest.
        public List<byte[]> Packets() {
            var packets = new List<byte[]>();
            while (Queue.TryTake(out (byte[] Packet, int Length) sent)) {
                if ((SendOp) (sent.Packet[1] << 8 | sent.Packet[0]) == SendOp.ItemEnchant) {
                    packets.Add(sent.Packet);
                }
            }
            return packets;
        }

        public byte[] Commands() => Packets().Select(packet => packet[2]).ToArray();

        public int Spent(ItemTag tag) => Given[Array.IndexOf(Materials, tag)] - Catalysts.Where(item => item.Metadata.Property.Tag == tag).Sum(item => item.Amount);

        // How many attempts the materials paid for at the staged level's price; every material must agree.
        public int Paid() {
            int[] paid = Cost.Select(cost => {
                Assert.That(Spent(cost.Tag) % cost.Amount, Is.Zero, $"{cost.Tag} is paid in whole attempts");
                return Spent(cost.Tag) / cost.Amount;
            }).Distinct().ToArray();
            Assert.That(paid, Has.Length.EqualTo(1), "every material pays the same attempts");
            return paid[0];
        }

        public void Copy(long uid, Action<Item>? change = null) {
            var copy = new Item(Item.Metadata, Item.Rarity) { Uid = uid };
            change?.Invoke(copy);
            Assert.That(Bag.Add(copy), Is.Not.Empty);
        }

        public long[] CopiesLeft() => Bag.Where(item => item.Uid != Upgrade).Select(item => item.Uid).OrderBy(uid => uid).ToArray();
    }

    // Materials for this many attempts at the item's level. The item is staged, and nothing else has been sent.
    private static Rig Build(int rarity, int enchants, EnchantType type, int attempts) {
        Assume.That(OfflineAutoEnchant.Enabled, Is.True, "the command is off in this environment");
        var queue = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", queue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        long character = Interlocked.Decrement(ref lastCharacter);
        SetProperty(session, "CharacterId", character);

        var item = new Item(Meta(ItemId, 1, ItemTag.None, 1), rarity) { Uid = Upgrade, Enchant = new ItemEnchant(enchants) };
        IngredientInfo[] cost = ItemEnchantManager.GetRequiredCatalysts(type, item).ToArray();
        var bag = new ItemCollection(100);
        var catalysts = new ItemCollection(100);
        Assert.That(bag.Add(item), Is.Not.Empty);
        var given = new int[Materials.Length];
        foreach (IngredientInfo info in cost) {
            int index = Array.IndexOf(Materials, info.Tag);
            given[index] = info.Amount * attempts;
            if (given[index] > 0) {
                Assert.That(catalysts.Add(new Item(Meta(40100001 + index, 15, info.Tag, int.MaxValue), 1, given[index]) { Uid = 100 + index }), Is.Not.Empty);
            }
        }

        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [InventoryType.Gear] = bag, [InventoryType.Catalyst] = catalysts });
        var items = Bare<ItemManager>();
        SetField(items, "Inventory", inventory);
        SetProperty(session, "Item", items);

        // No trophy or quest listens: ConditionUpdate only has to run.
        var trophies = Bare<AchievementMetadataStorage>();
        SetField(trophies, "Context", Bare<MetadataContext>());
        SetField(trophies, "Cache", new LRUCache<int, AchievementMetadata>(2, 1));
        SetField(trophies, "cachedTypes", new HashSet<ConditionType>(Enum.GetValues<ConditionType>()));
        SetProperty(session, "AchievementMetadata", trophies);
        session.Achievement = Bare<AchievementManager>();
        SetField(session.Achievement, "session", session);
        session.Quest = Bare<QuestManager>();
        SetField(session.Quest, "accountValues", new Dictionary<int, Quest>());
        SetField(session.Quest, "characterValues", new Dictionary<int, Quest>());

        var tables = Bare<ServerTableMetadataStorage>();
        var constants = new ConstantsTable(EnchantEquipIngredientOpenRank: 4, enchantSuccessBroadcastingLevel: 99);
        var options = new EnchantOptionTable(Enumerable.Range(1, 15).ToDictionary(level => level,
            level => new EnchantOptionMetadata(level, item.Type.Type, level, (short) rarity, 0.01f * level, 0, 99, [BasicAttribute.Strength])));
        SetField(tables, "constantsTable", new Lazy<ConstantsTable>(() => constants));
        SetField(tables, "enchantOptionTable", new Lazy<EnchantOptionTable>(() => options));
        SetProperty(session, "ServerTableMetadata", tables);

        var rig = new Rig { Manager = new ItemEnchantManager(session), Bag = bag, Catalysts = catalysts, Item = item, Queue = queue, Character = character, Given = given, Cost = cost };
        Assert.That(rig.Manager.StageItem(type, Upgrade), Is.True);
        rig.StagePacket = rig.Packets().Single();
        return rig;
    }

    [TestCase(3, 0, TestName = "Capture: a rank 3 stage at +10 asks 0, and a press needs none")]
    [TestCase(5, 2, TestName = "Capture: a rank 5 stage at +10 asks 2, and a press with none is refused")]
    public void TheRankRule_ReachesTheStageAndThePress(int rarity, int copies) {
        Rig rig = Build(rarity, 10, EnchantType.Peachy, attempts: 1);
        Assert.That(rig.StagePacket[2], Is.EqualTo(Stage));
        Assert.That(BitConverter.ToInt32(rig.StagePacket, rig.StagePacket.Length - 4), Is.EqualTo(copies));

        Assert.That(rig.Manager.Enchant(Upgrade), Is.EqualTo(copies == 0));
        Assert.That(rig.Paid(), Is.EqualTo(copies == 0 ? 1 : 0));
    }

    [TestCase(EnchantType.Ophelia)]
    [TestCase(EnchantType.Peachy)]
    public void AtFifteen_NoReply_AndNoAttempt(EnchantType type) {
        Rig rig = Build(3, 14, type, attempts: 1); // rank 3 asks no copies, so only the guard stops the press
        rig.Item.Enchant!.Enchants = 15;

        Assert.That(rig.Manager.Enchant(Upgrade), Is.False, "a press on the stage left from +14");
        Assert.That(rig.Manager.StageItem(type, Upgrade), Is.False);
        Assert.That(rig.Commands(), Is.Empty);
        Assert.That(rig.Paid(), Is.Zero);
        Assert.That(rig.Item.Enchant.Enchants, Is.EqualTo(15));
    }

    [Test]
    public void OffUntilTyped_OnePressIsOneHit() {
        Rig rig = Build(5, 9, EnchantType.Peachy, attempts: 7);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Paid(), Is.EqualTo(1));
        Assert.That(rig.Item.Enchant!.EnchantExp, Is.EqualTo(2000));
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Exp }));
    }

    [Test]
    public void Peachy_RunsToTheLevel_PaysEachHit_OneResult() {
        Rig rig = Build(5, 9, EnchantType.Peachy, attempts: 7);
        OfflineAutoEnchant.Set(rig.Character, null);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Item.Enchant!.Enchants, Is.EqualTo(10));
        Assert.That(rig.Item.Enchant.EnchantExp, Is.Zero);
        Assert.That(rig.Paid(), Is.EqualTo(5));
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Success, Exp }));
        Assert.That(rig.Manager.Type, Is.EqualTo(EnchantType.None), "reset, as a Peachy level does; the client stages again");
    }

    [Test]
    public void Peachy_MaterialsEnd_TheExpStays_AndTheNextPressIsRefused() {
        Rig rig = Build(5, 9, EnchantType.Peachy, attempts: 2);
        OfflineAutoEnchant.Set(rig.Character, null);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Item.Enchant!.EnchantExp, Is.EqualTo(4000));
        Assert.That(rig.Paid(), Is.EqualTo(2));
        byte[] exp = rig.Packets().Single();
        Assert.That(exp[2], Is.EqualTo(Exp), "no error line for the attempt that could not be paid");
        Assert.That(BitConverter.ToInt32(exp, exp.Length - 4), Is.EqualTo(4000));
        Assert.That(rig.Manager.Type, Is.EqualTo(EnchantType.Peachy), "no level: the stage stays");

        Assert.That(rig.Manager.Enchant(Upgrade), Is.False);
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Error }), "refused as a click is");
    }

    [Test]
    public void Copies_OnlyCleanOnesGo_AndTheRunStopsWhenTheyEnd() {
        Rig rig = Build(5, 10, EnchantType.Peachy, attempts: 9);
        rig.Copy(10, copy => copy.IsLocked = true);
        rig.Copy(11, copy => copy.Enchant = new ItemEnchant(1));
        rig.Copy(12, copy => copy.TimeChangedOption = 1);
        foreach (long uid in new long[] { 20, 21, 22, 23, 24 }) {
            rig.Copy(uid);
        }
        OfflineAutoEnchant.Set(rig.Character, null);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Paid(), Is.EqualTo(2), "2 copies a hit: 5 clean ones pay for 2 hits and 1 stays");
        Assert.That(rig.Item.Enchant!.EnchantExp, Is.EqualTo(2 * 3334));
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Exp }));
        long[] left = rig.CopiesLeft();
        Assert.That(left.Take(3), Is.EqualTo(new long[] { 10, 11, 12 }));
        Assert.That(left, Has.Length.EqualTo(4));
    }

    [Test]
    public void TheCopiesThePlayerPicked_GoFirst() {
        Rig rig = Build(5, 10, EnchantType.Peachy, attempts: 2);
        rig.Copy(10, copy => copy.IsLocked = true);
        foreach (long uid in new long[] { 20, 21, 22, 23 }) {
            rig.Copy(uid);
        }
        Assert.That(rig.Manager.UpdateFodder(10, true), Is.True);
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Fodder }));
        OfflineAutoEnchant.Set(rig.Character, null);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Paid(), Is.EqualTo(2));
        Assert.That(rig.CopiesLeft(), Has.Length.EqualTo(1), "the picked one and 3 clean ones");
        Assert.That(rig.CopiesLeft(), Does.Not.Contain(10L));
    }

    [Test]
    public void AJump_SumsTheStatsOfItsLevels_InOneResult() {
        Rig rig = Build(5, 0, EnchantType.Ophelia, attempts: 9); // +0 to +2 are 100 %, at one price
        OfflineAutoEnchant.Set(rig.Character, 3);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Item.Enchant!.Enchants, Is.EqualTo(3));
        Assert.That(rig.Paid(), Is.EqualTo(3));
        byte[] result = rig.Packets().Single();
        Assert.That(result[2], Is.EqualTo(Success));
        Assert.That(BitConverter.ToSingle(result, result.Length - 4), Is.EqualTo(0.06f).Within(0.0001f), "the last 4 bytes are the Strength delta's rate: 1 + 2 + 3 levels");
        Assert.That(rig.Item.Enchant.BasicOptions[BasicAttribute.Strength].Rate, Is.EqualTo(0.06f).Within(0.0001f));
    }

    [Test]
    public void AJump_PaysEachLevelsOwnPrice() {
        Rig rig = Build(5, 2, EnchantType.Peachy, attempts: 9);
        OfflineAutoEnchant.Set(rig.Character, 4);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        Assert.That(rig.Item.Enchant!.Enchants, Is.EqualTo(4));
        Assert.That(rig.Spent(ItemTag.Onix), Is.EqualTo(802 + 2 * 579), "+2 is 802 for 1 hit, +3 is 579 for 2 hits");
        Assert.That(rig.Commands(), Is.EqualTo(new[] { Success, Exp }));
    }

    [Test]
    public void TheCommand_PrintsTheState_AndSetsIt() {
        Assume.That(OfflineAutoEnchant.Enabled, Is.True, "the command is off in this environment");
        var session = Bare<GameSession>();
        long character = Interlocked.Decrement(ref lastCharacter);
        SetProperty(session, "CharacterId", character);

        var console = new TestConsole();
        Assert.That(new AutoEnchantCommand(session, enabled: true).Invoke(["autoenchant"], console), Is.Zero);
        Assert.That(console.Out.ToString(), Does.Contain("ON").And.Contain("next level"));
        Assert.That(console.Error.ToString(), Is.Empty);
        Assert.That(OfflineAutoEnchant.Goal(character, 9), Is.EqualTo(10));

        console = new TestConsole();
        Assert.That(new AutoEnchantCommand(session, enabled: true).Invoke(["autoenchant", "12"], console), Is.Zero);
        Assert.That(console.Out.ToString(), Does.Contain("ON").And.Contain("+12").And.Contain("tier 12"));
        Assert.That(OfflineAutoEnchant.Goal(character, 9), Is.EqualTo(12));

        console = new TestConsole();
        Assert.That(new AutoEnchantCommand(session, enabled: true).Invoke(["autoenchant", "16"], console), Is.EqualTo(1));
        Assert.That(OfflineAutoEnchant.Goal(character, 9), Is.EqualTo(12), "a refused target changes nothing");

        console = new TestConsole();
        Assert.That(new AutoEnchantCommand(session, enabled: true).Invoke(["autoenchant"], console), Is.Zero);
        Assert.That(console.Out.ToString(), Does.Contain("OFF"));
        Assert.That(OfflineAutoEnchant.Goal(character, 9), Is.Null);
    }

    [Test]
    [Repeat(20)]
    public void Ophelia_EitherEnd_OneResult_ThePickedChargesOnce() {
        Rig rig = Build(5, 13, EnchantType.Ophelia, attempts: 3); // 10 %, 3 copies each
        foreach (long uid in Enumerable.Range(20, 9)) {
            rig.Copy(uid);
        }
        rig.Item.Enchant!.Charges = 5;
        Assert.That(rig.Manager.UpdateCharges(5), Is.True);
        rig.Packets();
        OfflineAutoEnchant.Set(rig.Character, null);

        Assert.That(rig.Manager.Enchant(Upgrade), Is.True);
        int attempts = rig.Paid();
        byte[] result = rig.Packets().Single();
        Assert.That(rig.CopiesLeft(), Has.Length.EqualTo(9 - 3 * attempts));
        if (rig.Item.Enchant.Enchants == 14) {
            Assert.That(result[2], Is.EqualTo(Success));
            Assert.That(rig.Item.Enchant.Charges, Is.EqualTo((attempts - 1) * 4));
        } else {
            Assert.That(result[2], Is.EqualTo(Failure));
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(BitConverter.ToInt32(result, result.Length - 4), Is.EqualTo(3 * 4), "the charges of all its failures");
            Assert.That(rig.Item.Enchant.Charges, Is.EqualTo(3 * 4), "the 5 picked went on the first attempt only");
        }
    }
}

/// <summary>EN-01: the rank that opens copies comes from the game's constants.</summary>
[Category("Data")]
[NonParallelizable]
public class AutoEnchantDataTest {
    [Test]
    public void CopiesOpenAtRankFour() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        var constants = (ConstantsTable) context.ServerTableMetadata.Find(ServerTableNames.CONSTANTS)!.Table;
        Assert.That(constants.EnchantEquipIngredientOpenRank, Is.EqualTo(4));
    }
}
