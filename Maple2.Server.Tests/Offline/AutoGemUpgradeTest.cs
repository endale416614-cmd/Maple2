using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using static Maple2.Model.Metadata.GemstoneUpgradeTable;

namespace Maple2.Server.Tests.Offline;

/// <summary>Golden: the data's row for Tier 1 Wisdom Gemstone 40200001, the chain to Tier 10; AutoGemUpgradeDataTest holds these rows to the game's table.</summary>
internal static class GemChain {
    public const int Tier1 = 40200001;

    public static int Tier(int tier) => Tier1 + tier - 1;

    // The data's Intelligence at tiers 1 to 10; AutoGemUpgradeDataTest holds them to the game's table.
    public static readonly int[] Intelligence = [2, 4, 7, 10, 13, 16, 20, 25, 30, 36];

    public static ItemMetadataOption Option(int tier) => new(0, ItemOptionMakeType.Base, 0, ItemOptionMakeType.Base, Tier(tier), ItemOptionMakeType.Base, 0, 0);

    public static ItemStats Stats(int tier) {
        var stats = new ItemStats();
        stats[ItemStats.Type.Constant].Basic[BasicAttribute.Intelligence] = new BasicOption(Intelligence[tier - 1]);
        return stats;
    }

    public static ItemOptionConstantTable Constants() => new(Enumerable.Range(1, 10).ToDictionary(Tier, tier =>
        (IReadOnlyDictionary<int, ItemOptionConstant>) new Dictionary<int, ItemOptionConstant> {
            [Constant.GemstoneGrade] = new(new Dictionary<BasicAttribute, int> { [BasicAttribute.Intelligence] = Intelligence[tier - 1] }, new Dictionary<BasicAttribute, float>(),
                new Dictionary<SpecialAttribute, int>(), new Dictionary<SpecialAttribute, float>()),
        }));

    public static int IntelligenceOf(ItemStats? stats) => stats![ItemStats.Type.Constant].Basic[BasicAttribute.Intelligence].Value;

    public static readonly Dictionary<int, Entry> Rows = new() {
        [Tier(1)] = Row(1, 80, 24, 16, 40),
        [Tier(2)] = Row(2, 100, 30, 22, 68),
        [Tier(3)] = Row(3, 126, 38, 34, 105),
        [Tier(4)] = Row(4, 163, 49, 55, 156),
        [Tier(5)] = Row(5, 200, 60, 86, 214),
        [Tier(6)] = Row(6, 267, 80, 133, 292),
        [Tier(7)] = Row(7, 360, 108, 208, 400),
        [Tier(8)] = Row(8, 444, 133, 293, 500),
        [Tier(9)] = Row(9, 550, 165, 410, 625),
        [Tier(10)] = new Entry(10, 0, []),
    };

    private static Entry Row(short tier, int blue, int cyan, int orange, int crystal) => new(tier, Tier(tier + 1), [
        new Ingredient(ItemTag.DustBlue, blue),
        new Ingredient(ItemTag.DustCyan, cyan),
        new Ingredient(ItemTag.DustOrange, orange),
        new Ingredient(ItemTag.CrystalPiece, crystal),
    ]);

    // Gem dust and Crystal Fragments for these rows: the sums the tiers cost.
    public static (int Dust, int Crystal) Cost(IEnumerable<Entry> paid) {
        Ingredient[] all = paid.SelectMany(entry => entry.Ingredients).ToArray();
        return (all.Where(ingredient => ingredient.ItemTag != ItemTag.CrystalPiece).Sum(ingredient => ingredient.Amount),
            all.Where(ingredient => ingredient.ItemTag == ItemTag.CrystalPiece).Sum(ingredient => ingredient.Amount));
    }
}

/// <summary>EN-02 (R-21): the upgrades one press buys. A gem upgrade has no roll, so a run is only how far it goes and what it pays.</summary>
public class AutoGemUpgradeTest {
    private static List<Entry> Run(int tier, int goal, Func<Entry, bool>? pay = null) =>
        ItemSocketHandler.GemstoneUpgradeRun(GemChain.Rows, GemChain.Rows[GemChain.Tier(tier)], goal, pay ?? (_ => true));

    [Test]
    public void OneTier_IsOneUpgrade() {
        List<Entry> paid = Run(1, 2);

        Assert.That(paid, Has.Count.EqualTo(1));
        Assert.That(paid[^1].NextItemId, Is.EqualTo(GemChain.Tier(2)));
    }

    [Test]
    public void GoldenCost_ToTierFive() {
        List<Entry> paid = Run(1, 5);

        Assert.That(paid, Has.Count.EqualTo(4));
        Assert.That(paid[^1].NextItemId, Is.EqualTo(GemChain.Tier(5)));
        Assert.That(GemChain.Cost(paid), Is.EqualTo((737, 369)));
    }

    [Test]
    public void GoldenCost_ToTheTop_AndNumbersAboveItStopThere() {
        foreach (int goal in new[] { 10, 11, 15 }) {
            List<Entry> paid = Run(1, goal);

            Assert.That(paid, Has.Count.EqualTo(9), $"target {goal}");
            Assert.That(paid[^1].NextItemId, Is.EqualTo(GemChain.Tier(10)));
            Assert.That(GemChain.Cost(paid), Is.EqualTo((4234, 2400)));
        }
    }

    [Test]
    public void EachUpgradePaysItsOwnRow_InOrder_Once() {
        var asked = new List<Entry>();
        List<Entry> paid = Run(3, 6, entry => {
            asked.Add(entry);
            return true;
        });

        Entry[] expected = [GemChain.Rows[GemChain.Tier(3)], GemChain.Rows[GemChain.Tier(4)], GemChain.Rows[GemChain.Tier(5)]];
        Assert.That(asked, Is.EqualTo(expected));
        Assert.That(paid, Is.EqualTo(expected));
    }

    [Test]
    public void ARefusal_EndsTheRun_AndKeepsWhatWasPaid() {
        int asked = 0;
        List<Entry> paid = Run(1, 10, _ => ++asked < 3);

        Assert.That(asked, Is.EqualTo(3), "nothing is asked after the refusal");
        Assert.That(paid, Has.Count.EqualTo(2));
        Assert.That(paid[^1].NextItemId, Is.EqualTo(GemChain.Tier(3)));
    }

    [Test]
    public void NothingPaid_IsNoUpgrade() {
        Assert.That(Run(1, 10, _ => false), Is.Empty);
    }

    [Test]
    public void AtTheTop_NothingIsAsked() {
        Assert.That(Run(10, 15, _ => throw new AssertionException("the top has no price")), Is.Empty);
    }

    [Test]
    public void ANextTierWithNoRow_EndsTheRun() {
        var table = new Dictionary<int, Entry> { [1] = new(1, 2, []) };

        Assert.That(ItemSocketHandler.GemstoneUpgradeRun(table, table[1], 10, _ => true), Has.Count.EqualTo(1));
    }
}

/// <summary>EN-02: a press on a session with no server, with the real inventory and an empty save: what it pays, where the gem lands and the one result the client gets.</summary>
public class AutoGemUpgradeRunTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Target = 1, GemUid = 4, ClientUid = 777;
    private const int GearId = 11250046, NoItemTier = 40299003;
    private const byte CommandUpgrade = 4, ReplyUpgrade = 5, ReplyError = 18;
    private const byte InventoryAdd = 0, InventoryRemove = 1, InventoryUpdateAmount = 2;
    private const int GemIdOffset = 23; // op 2, command 1, uid 8, byte, success, grade, client uid 8, has gem
    private const int Plenty = 100000;
    private static long lastCharacter = -175100;

    // A chain whose tier 3 is in the table and is no item.
    private static readonly Dictionary<int, Entry> Table = new(GemChain.Rows) {
        [40299001] = new Entry(1, 40299002, [new Ingredient(ItemTag.DustBlue, 10)]),
        [40299002] = new Entry(2, NoItemTier, [new Ingredient(ItemTag.DustBlue, 20)]),
        [NoItemTier] = new Entry(3, 0, []),
    };

    private SqliteConnection metadataConnection = null!;
    private SqliteConnection saveConnection = null!;
    private MetadataContext metadata = null!;
    private ItemMetadataStorage items = null!;
    private GameStorage storage = null!;

    [OneTimeSetUp]
    public void SetUp() {
        metadataConnection = new SqliteConnection("Data Source=:memory:");
        metadataConnection.Open();
        metadata = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(metadataConnection).Options);
        metadata.Database.EnsureCreated();
        metadata.ItemMetadata.AddRange(Enumerable.Range(1, 10).Select(tier => Meta(GemChain.Tier(tier), 13, ItemTag.None, 9999, GemChain.Option(tier))));
        metadata.ItemMetadata.AddRange(Meta(40299001, 13, ItemTag.None, 9999), Meta(40299002, 13, ItemTag.None, 9999));
        metadata.SaveChanges();
        items = new ItemMetadataStorage(metadata);

        saveConnection = new SqliteConnection("Data Source=:memory:");
        saveConnection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(saveConnection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        storage = new GameStorage(options, items, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    [OneTimeTearDown]
    public void TearDown() {
        metadata.Dispose();
        metadataConnection.Dispose();
        saveConnection.Dispose();
    }

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

    private static ItemMetadata Meta(int id, int type, ItemTag tag, int slotMax, ItemMetadataOption? option = null) {
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], option, null, new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false), new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none));
    }

    private static Achievement Trophy(int id, ConditionType type, ConditionMetadata.Parameters? target, params long[] values) {
        Dictionary<int, AchievementMetadataGrade> grades = values.Select((value, index) => new AchievementMetadataGrade(index + 1, new ConditionMetadata(type, value, null, target), null)).ToDictionary(grade => grade.Grade);
        return new Achievement(new AchievementMetadata(id, $"{id}", false, AchievementCategory.Life, [], grades)) {
            CurrentGrade = values.Length,
            Grades = grades.Keys.ToDictionary(grade => grade, _ => 1L),
        };
    }

    private sealed class Rig {
        public required GameSession Session { get; init; }
        public required ItemSocketHandler Handler { get; init; }
        public required ItemStatsCalculator Calculator { get; init; }
        public required ItemCollection Gear { get; init; }
        public required ItemCollection Gems { get; init; }
        public required ItemCollection Catalysts { get; init; }
        public required Item Accessory { get; init; }
        public required BlockingCollection<(byte[], int)> Queue { get; init; }
        public required Dictionary<ItemTag, int> Given { get; init; }
        public required long Character { get; init; }
        public required Achievement Tries { get; init; }
        public required Achievement Successes { get; init; }
        public required Achievement Perfection { get; init; }
        public required Func<int, ItemMetadata> GemMeta { get; init; }

        public void Press(long itemUid, sbyte slot, long gemUid) {
            var writer = new ByteWriter();
            writer.WriteByte(CommandUpgrade);
            writer.WriteLong(itemUid);
            writer.Write<sbyte>(slot);
            writer.WriteLong(gemUid);
            Handler.Handle(Session, new ByteReader(writer.Buffer[..writer.Length]));
        }

        public void PressSocket() => Press(Target, 0, ClientUid);

        public void PressBag() => Press(0, -1, GemUid);

        // The packets of this op since the last call, whole; the queue keeps the others for their own call.
        public List<byte[]> Sent(SendOp op) {
            var packets = new List<byte[]>();
            var others = new List<(byte[], int)>();
            while (Queue.TryTake(out (byte[] Packet, int Length) sent)) {
                if ((SendOp) (sent.Packet[1] << 8 | sent.Packet[0]) == op) {
                    packets.Add(sent.Packet);
                } else {
                    others.Add(sent);
                }
            }
            foreach ((byte[], int) other in others) {
                Queue.Add(other);
            }
            return packets;
        }

        // The one result of a press, whole.
        public byte[] Result() {
            byte[] reply = Sent(SendOp.ItemSocketSystem).Single();
            Assert.That(reply[2], Is.EqualTo(ReplyUpgrade));
            return reply;
        }

        public void AssertError(ItemSocketError error) {
            byte[] reply = Sent(SendOp.ItemSocketSystem).Single();
            Assert.That(reply[2], Is.EqualTo(ReplyError));
            Assert.That(BitConverter.ToInt32(reply, 4), Is.EqualTo((int) error));
        }

        public int Spent(ItemTag tag) => Given[tag] - Catalysts.Where(item => item.Metadata.Property.Tag == tag).Sum(item => item.Amount);

        public (int Dust, int Crystal) Cost() => (Spent(ItemTag.DustBlue) + Spent(ItemTag.DustCyan) + Spent(ItemTag.DustOrange), Spent(ItemTag.CrystalPiece));

        public int SocketGem() => Accessory.Socket!.Sockets[0]!.ItemId;

        public Dictionary<int, int> GemsHeld() => Gems.GroupBy(gem => gem.Id).ToDictionary(group => group.Key, group => group.Sum(gem => gem.Amount));

        public void Hold(int id, int amount, long uid) {
            Assert.That(Gems.Add(new Item(GemMeta(id), Constant.GemstoneGrade, amount) { Uid = uid, Stats = new ItemStats() }), Is.Not.Empty);
        }
    }

    // Accessory with one open socket holding a gem of this tier, and the materials. Off for this character until the test sets it.
    private Rig Build(int socketTier, params (ItemTag Tag, int Amount)[] materials) {
        Assume.That(OfflineAutoEnchant.Enabled, Is.True, "the command is off in this environment");
        var queue = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", queue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        long character = Interlocked.Decrement(ref lastCharacter);
        SetProperty(session, "CharacterId", character);
        SetProperty(session, "GameStorage", storage);
        SetProperty(session, "ItemMetadata", items);

        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "polisher" }, new Character { Name = "Polisher", Mastery = null! }, 0) {
            Home = null!,
            Currency = null!,
            Unlock = null!,
        });
        SetProperty(session, "Player", player);

        SetProperty(session, "Field", Bare<FieldManager>());

        var accessory = new Item(Meta(GearId, 1, ItemTag.None, 1), 4) { Uid = Target, Socket = new ItemSocket(3, 1) };
        accessory.Socket.Sockets[0] = new ItemGemstone(GemChain.Tier(socketTier), stats: GemChain.Stats(socketTier));
        var gear = new ItemCollection(100);
        Assert.That(gear.Add(accessory), Is.Not.Empty);
        var gems = new ItemCollection(100);
        var catalysts = new ItemCollection(100);
        var given = new Dictionary<ItemTag, int> { [ItemTag.DustBlue] = 0, [ItemTag.DustCyan] = 0, [ItemTag.DustOrange] = 0, [ItemTag.CrystalPiece] = 0 };
        long uid = 100;
        foreach ((ItemTag tag, int amount) in materials) {
            given[tag] = amount;
            Assert.That(catalysts.Add(new Item(Meta(40100001 + (int) uid, 15, tag, int.MaxValue), 1, amount) { Uid = uid++ }), Is.Not.Empty);
        }

        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [InventoryType.Gear] = gear, [InventoryType.Gemstone] = gems, [InventoryType.Catalyst] = catalysts });
        var itemManager = Bare<ItemManager>();
        SetField(itemManager, "Inventory", inventory);
        SetProperty(session, "Item", itemManager);

        // The trophies with every grade earned, so a press only moves their counters. Golden: the grades of 23200026, 23200027 and 23200029 (reach tier 10).
        Achievement tries = Trophy(23200026, ConditionType.gemstone_upgrade_try, null, 1, 10, 50, 100, 500);
        Achievement successes = Trophy(23200027, ConditionType.gemstone_upgrade_success, null, 1, 10, 100);
        Achievement perfection = Trophy(23200029, ConditionType.gemstone_upgrade, new ConditionMetadata.Parameters(Integers: [10]), 1);
        var cache = new LRUCache<int, AchievementMetadata>(3, 1);
        foreach (Achievement trophy in new[] { tries, successes, perfection }) {
            cache.AddReplace(trophy.Id, trophy.Metadata);
        }
        var trophies = Bare<AchievementMetadataStorage>();
        SetField(trophies, "Context", Bare<MetadataContext>());
        SetField(trophies, "Cache", cache);
        SetField(trophies, "cachedTypes", new HashSet<ConditionType>(Enum.GetValues<ConditionType>()));
        SetProperty(session, "AchievementMetadata", trophies);
        session.Achievement = Bare<AchievementManager>();
        SetField(session.Achievement, "session", session);
        SetField(session.Achievement, "accountValues", new Dictionary<int, Achievement>());
        SetField(session.Achievement, "characterValues", new Dictionary<int, Achievement> { [tries.Id] = tries, [successes.Id] = successes, [perfection.Id] = perfection });
        session.Quest = Bare<QuestManager>();
        SetField(session.Quest, "session", session);
        SetField(session.Quest, "accountValues", new Dictionary<int, Quest>());
        SetField(session.Quest, "characterValues", new Dictionary<int, Quest>());

        var tables = Bare<TableMetadataStorage>();
        var table = new GemstoneUpgradeTable(Table);
        SetField(tables, "gemstoneUpgradeTable", new Lazy<GemstoneUpgradeTable>(() => table));
        SetField(tables, "itemOptionConstantTable", new Lazy<ItemOptionConstantTable>(GemChain.Constants));
        SetField(tables, "itemOptionPickTable", new Lazy<ItemOptionPickTable>(() => new ItemOptionPickTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionPickTable.Option>>())));
        SetField(tables, "itemOptionStaticTable", new Lazy<ItemOptionStaticTable>(() => new ItemOptionStaticTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>())));
        SetField(tables, "itemOptionRandomTable", new Lazy<ItemOptionRandomTable>(() => new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>())));
        var calculator = new ItemStatsCalculator { TableMetadata = tables };

        return new Rig {
            Session = session,
            Handler = new ItemSocketHandler { ItemMetadata = items, TableMetadata = tables, ItemStatsCalc = calculator },
            Calculator = calculator,
            Gear = gear,
            Gems = gems,
            Catalysts = catalysts,
            Accessory = accessory,
            Queue = queue,
            Given = given,
            Character = character,
            Tries = tries,
            Successes = successes,
            Perfection = perfection,
            GemMeta = id => {
                Assert.That(items.TryGet(id, out ItemMetadata? gem), Is.True, $"gem {id}");
                return gem!;
            },
        };
    }

    private Rig Rich(int socketTier = 1) => Build(socketTier, (ItemTag.DustBlue, Plenty), (ItemTag.DustCyan, Plenty), (ItemTag.DustOrange, Plenty), (ItemTag.CrystalPiece, Plenty));

    // Exactly the first upgrades of the chain from tier 1.
    private Rig ForUpgrades(int upgrades) {
        List<Entry> rows = ItemSocketHandler.GemstoneUpgradeRun(GemChain.Rows, GemChain.Rows[GemChain.Tier1], upgrades + 1, _ => true);
        return Build(1, rows.SelectMany(row => row.Ingredients).GroupBy(row => row.ItemTag).Select(group => (group.Key, group.Sum(row => row.Amount))).ToArray());
    }

    private static void AssertResult(byte[] result, long itemUid, long gemUid, int gemId) {
        Assert.That(BitConverter.ToInt64(result, 3), Is.EqualTo(itemUid));
        Assert.That(result[12], Is.EqualTo(1), "success");
        Assert.That(BitConverter.ToInt64(result, 14), Is.EqualTo(gemUid));
        Assert.That(BitConverter.ToInt32(result, GemIdOffset), Is.EqualTo(gemId));
    }

    [Test]
    public void Socket_Off_OnePressIsOneTier() {
        Rig rig = Rich();

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(2)));
        AssertResult(rig.Result(), Target, ClientUid, GemChain.Tier(2));
        Assert.That(rig.Cost(), Is.EqualTo((120, 40)), "the first row of the chain");
        Assert.That(rig.Tries.Counter, Is.EqualTo(1));
        Assert.That(rig.Successes.Counter, Is.EqualTo(1));
    }

    [Test]
    public void Socket_WithNoNumber_StaysOneTierPerPress() {
        Rig rig = Rich();
        OfflineAutoEnchant.Set(rig.Character, null);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(2)));
        Assert.That(rig.Cost(), Is.EqualTo((120, 40)));
    }

    [Test]
    public void Socket_ATarget_RunsToItsTier_OneResult_EachTierPaid_EachTierCounted() {
        Rig rig = Rich();
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(5)));
        AssertResult(rig.Result(), Target, ClientUid, GemChain.Tier(5));
        Assert.That(rig.Cost(), Is.EqualTo((737, 369)));
        Assert.That(rig.Tries.Counter, Is.EqualTo(4));
        Assert.That(rig.Successes.Counter, Is.EqualTo(4));
        Assert.That(rig.Perfection.Counter, Is.Zero);
    }

    [Test]
    public void Socket_ANumberAboveTheTop_RunsToTheTop_AndTheTrophyReadsTierTen() {
        Rig rig = Rich();
        OfflineAutoEnchant.Set(rig.Character, 12);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(10)));
        AssertResult(rig.Result(), Target, ClientUid, GemChain.Tier(10));
        Assert.That(rig.Cost(), Is.EqualTo((4234, 2400)));
        Assert.That(rig.Tries.Counter, Is.EqualTo(9));
        Assert.That(rig.Successes.Counter, Is.EqualTo(9));
        Assert.That(rig.Perfection.Counter, Is.EqualTo(1));
    }

    [Test]
    public void Socket_ATierAtTheTarget_GetsOneOrdinaryUpgrade() {
        Rig rig = Rich(5);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(6)));
        Assert.That(rig.Cost(), Is.EqualTo((200 + 60 + 86, 214)));
    }

    [Test]
    public void Socket_MaterialsEnd_TheTiersPaidStay_NoErrorLine_AndTheNextPressIsRefused() {
        Rig rig = ForUpgrades(2);
        OfflineAutoEnchant.Set(rig.Character, 10);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(3)));
        AssertResult(rig.Result(), Target, ClientUid, GemChain.Tier(3));
        Assert.That(rig.Catalysts, Is.Empty, "both rows paid in full");
        Assert.That(rig.Tries.Counter, Is.EqualTo(2));

        rig.PressSocket();

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_invalid_target_ingredient_count);
        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(3)));
        Assert.That(rig.Tries.Counter, Is.EqualTo(2));
    }

    [Test]
    public void Socket_PartOfATiersPrice_IsNotTaken() {
        // Enough dust for tier 1 and tier 2, and Crystal Fragments for tier 1 and one short of tier 2.
        Rig rig = Build(1, (ItemTag.DustBlue, Plenty), (ItemTag.DustCyan, Plenty), (ItemTag.DustOrange, Plenty), (ItemTag.CrystalPiece, 40 + 67));
        OfflineAutoEnchant.Set(rig.Character, 10);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(2)));
        Assert.That(rig.Cost(), Is.EqualTo((120, 40)));
    }

    [Test]
    public void Socket_NoMaterials_IsRefusedAsAClickIs() {
        Rig rig = Build(1);
        OfflineAutoEnchant.Set(rig.Character, 10);

        rig.PressSocket();

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_invalid_target_ingredient_count);
        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(1)));
        Assert.That(rig.Tries.Counter, Is.Zero);
    }

    [Test]
    public void Socket_AtTheTop_SaysSo_AndNothingIsPaid() {
        Rig rig = Rich(10);
        OfflineAutoEnchant.Set(rig.Character, 15);

        rig.PressSocket();

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_gemstone_maxlevel);
        Assert.That(rig.Cost(), Is.EqualTo((0, 0)));
    }

    [Test]
    public void Bag_Off_OnePressIsOneTier() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier(2)] = 1 }));
        AssertResult(rig.Result(), 0, GemUid, GemChain.Tier(2));
        Assert.That(rig.Cost(), Is.EqualTo((120, 40)));
        Assert.That(rig.Tries.Counter, Is.EqualTo(1));
    }

    [Test]
    public void Bag_ATarget_RunsToItsTier_OneRemove_OneAdd_OneResult() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier(5)] = 1 }));
        AssertResult(rig.Result(), 0, GemUid, GemChain.Tier(5));
        Assert.That(rig.Cost(), Is.EqualTo((737, 369)));
        Assert.That(rig.Tries.Counter, Is.EqualTo(4));
        Assert.That(rig.Successes.Counter, Is.EqualTo(4));

        byte[][] inventory = rig.Sent(SendOp.ItemInventory).ToArray();
        byte[][] removes = inventory.Where(packet => packet[2] == InventoryRemove).ToArray();
        Assert.That(removes, Has.Length.EqualTo(1), "the gem's own: the materials keep a stack");
        Assert.That(BitConverter.ToInt64(removes[0], 3), Is.EqualTo(GemUid));
        byte[][] adds = inventory.Where(packet => packet[2] == InventoryAdd).ToArray();
        Assert.That(adds, Has.Length.EqualTo(1));
        Assert.That(BitConverter.ToInt32(adds[0], 3), Is.EqualTo(GemChain.Tier(5)));
    }

    [Test]
    public void Bag_AStack_GivesOneGem_TheRestStay() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 3, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier1] = 2, [GemChain.Tier(5)] = 1 }));
        Assert.That(rig.Cost(), Is.EqualTo((737, 369)));
    }

    [Test]
    public void Bag_ARun_LeavesTheStacksItPasses_AndJoinsTheOneItLandsOn() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        rig.Hold(GemChain.Tier(3), 2, 300);
        rig.Hold(GemChain.Tier(5), 2, 301);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier(3)] = 2, [GemChain.Tier(5)] = 3 }));
        long[] moved = rig.Sent(SendOp.ItemInventory).Where(packet => packet[2] == InventoryUpdateAmount).Select(packet => BitConverter.ToInt64(packet, 3)).ToArray();
        Assert.That(moved, Does.Not.Contain(300L), "a stack it only passes hears nothing");
        Assert.That(moved, Does.Contain(301L));
    }

    [Test]
    public void Bag_MaterialsEnd_TheGemLandsAtTheTierPaid() {
        Rig rig = ForUpgrades(2);
        rig.Hold(GemChain.Tier1, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 10);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier(3)] = 1 }));
        AssertResult(rig.Result(), 0, GemUid, GemChain.Tier(3));
        Assert.That(rig.Catalysts, Is.Empty);
    }

    [Test]
    public void Bag_NoMaterials_IsRefused_AndTheGemStays() {
        Rig rig = Build(1);
        rig.Hold(GemChain.Tier1, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 10);

        rig.PressBag();

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_invalid_target_ingredient_count);
        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [GemChain.Tier1] = 1 }));
    }

    [Test]
    public void Bag_ATierThatIsNoItem_IsNotPaidFor() {
        Rig rig = Build(1, (ItemTag.DustBlue, Plenty));
        rig.Hold(40299001, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 3);

        rig.PressBag();

        Assert.That(rig.GemsHeld(), Is.EqualTo(new Dictionary<int, int> { [40299002] = 1 }));
        Assert.That(rig.Spent(ItemTag.DustBlue), Is.EqualTo(10), "the 20 of the tier with no item stay");
    }

    // SO-02 (BT-190): a gem gives its new tier's stats at once. Golden: the data's Intelligence, 2, 4, 13 and 36 at tiers 1, 2, 5 and 10 of 40200001.

    [Test]
    public void Stats_AClick_InASocket_GivesTheNextTier() {
        Rig rig = Rich();

        rig.PressSocket();

        Assert.That(GemChain.IntelligenceOf(rig.Accessory.Socket!.Sockets[0]!.Stats), Is.EqualTo(4));
    }

    [Test]
    public void Stats_AClick_InTheBag_GivesTheNextTier() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        rig.Gems.Single().Stats = GemChain.Stats(1);

        rig.PressBag();

        Assert.That(GemChain.IntelligenceOf(rig.Gems.Single().Stats), Is.EqualTo(4));
    }

    [TestCase(5, 13)]
    [TestCase(10, 36)]
    public void Stats_ARun_InASocket_GivesTheTierItLandsOn(int tier, int intelligence) {
        Rig rig = Rich();
        OfflineAutoEnchant.Set(rig.Character, tier);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(tier)));
        Assert.That(GemChain.IntelligenceOf(rig.Accessory.Socket!.Sockets[0]!.Stats), Is.EqualTo(intelligence));
    }

    [TestCase(5, 13)]
    [TestCase(10, 36)]
    public void Stats_ARun_InTheBag_GivesTheTierItLandsOn(int tier, int intelligence) {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, tier);

        rig.PressBag();

        Item gem = rig.Gems.Single();
        Assert.That(gem.Id, Is.EqualTo(GemChain.Tier(tier)));
        Assert.That(GemChain.IntelligenceOf(gem.Stats), Is.EqualTo(intelligence));
    }

    [Test]
    public void Stats_TheBagsInventoryPacket_CarriesTheNewNumbers() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressBag();

        byte[] add = rig.Sent(SendOp.ItemInventory).Single(packet => packet[2] == InventoryAdd);
        byte[] line = [.. BitConverter.GetBytes((short) BasicAttribute.Intelligence), .. BitConverter.GetBytes(13), .. BitConverter.GetBytes(0f)];
        Assert.That(add.AsSpan().IndexOf(line), Is.GreaterThan(0));
    }

    [Test]
    public void Stats_AStack_TheRestKeepTheirTier() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 3, GemUid);
        rig.Gems.Single().Stats = GemChain.Stats(1);
        OfflineAutoEnchant.Set(rig.Character, 5);

        rig.PressBag();

        Assert.That(GemChain.IntelligenceOf(rig.Gems.Single(gem => gem.Id == GemChain.Tier1).Stats), Is.EqualTo(2));
        Assert.That(GemChain.IntelligenceOf(rig.Gems.Single(gem => gem.Id == GemChain.Tier(5)).Stats), Is.EqualTo(13));
    }

    // A stats manager that records a rebuild: one asked for during a rebuild runs no pass, it only marks another (StatsManager.RunRebuild).
    private static StatsManager WatchRebuilds(Rig rig) {
        var stats = Bare<StatsManager>();
        SetProperty(stats, "Actor", rig.Session.Player);
        SetField(stats, "rebuildThread", Environment.CurrentManagedThreadId);
        rig.Session.Stats = stats;
        return stats;
    }

    private static bool Rebuilt(StatsManager stats) => (bool) typeof(StatsManager).GetField("rebuildAgain", Any)!.GetValue(stats)!;

    private static void Wear(Rig rig) {
        Assert.That(rig.Gear.Remove(Target, out _), Is.True);
        var equips = Bare<EquipManager>();
        SetField(equips, "Gear", new ConcurrentDictionary<EquipSlot, Item> { [EquipSlot.EA] = rig.Accessory });
        SetField(rig.Session.Item, "Equips", equips);
    }

    [Test]
    public void Totals_WornGear_AreRebuiltAfterAnUpgrade() {
        Rig rig = Rich();
        Wear(rig);
        StatsManager stats = WatchRebuilds(rig);

        rig.PressSocket();

        Assert.That(rig.SocketGem(), Is.EqualTo(GemChain.Tier(2)));
        Assert.That(GemChain.IntelligenceOf(rig.Accessory.Socket!.Sockets[0]!.Stats), Is.EqualTo(4));
        Assert.That(Rebuilt(stats), Is.True);
    }

    [Test]
    public void Totals_WornGear_ARefusedPress_RebuildsNothing() {
        Rig rig = Build(1);
        Wear(rig);
        StatsManager stats = WatchRebuilds(rig);

        rig.PressSocket();

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_invalid_target_ingredient_count);
        Assert.That(Rebuilt(stats), Is.False);
    }

    [Test]
    public void Totals_GearInTheBag_RebuildNothing() {
        Rig rig = Rich();
        StatsManager stats = WatchRebuilds(rig);

        rig.PressSocket();

        Assert.That(Rebuilt(stats), Is.False);
    }

    [Test]
    public void Totals_AGemInTheBag_RebuildNothing() {
        Rig rig = Rich();
        rig.Hold(GemChain.Tier1, 1, GemUid);
        StatsManager stats = WatchRebuilds(rig);

        rig.PressBag();

        Assert.That(Rebuilt(stats), Is.False);
    }

    // SO-02: a save from before the fix, by the login pass's rule.

    [Test]
    public void Save_ASocketGem_GetsItsTiersStats_Once() {
        Rig rig = Rich();
        rig.Accessory.Socket!.Sockets[0] = new ItemGemstone(GemChain.Tier(5), stats: GemChain.Stats(1));

        Assert.That(OfflineGearRepair.RepairGemstones(rig.Accessory, rig.Calculator, items), Is.True);
        Assert.That(GemChain.IntelligenceOf(rig.Accessory.Socket.Sockets[0]!.Stats), Is.EqualTo(13));
        Assert.That(OfflineGearRepair.RepairGemstones(rig.Accessory, rig.Calculator, items), Is.False);
    }

    [Test]
    public void Save_ABagGem_GetsItsTiersStats_Once() {
        Rig rig = Rich();
        var gem = new Item(rig.GemMeta(GemChain.Tier(10)), Constant.GemstoneGrade) { Uid = GemUid, Stats = GemChain.Stats(9) };

        Assert.That(OfflineGearRepair.RepairGemstones(gem, rig.Calculator, items), Is.True);
        Assert.That(GemChain.IntelligenceOf(gem.Stats), Is.EqualTo(36));
        Assert.That(OfflineGearRepair.RepairGemstones(gem, rig.Calculator, items), Is.False);
    }

    [Test]
    public void Save_AGemWithNoStats_GetsThem() {
        Rig rig = Rich();
        var gem = new Item(rig.GemMeta(GemChain.Tier(2)), Constant.GemstoneGrade) { Uid = GemUid };

        Assert.That(gem.Stats, Is.Null);
        Assert.That(OfflineGearRepair.RepairGemstones(gem, rig.Calculator, items), Is.True);
        Assert.That(GemChain.IntelligenceOf(gem.Stats), Is.EqualTo(4));
    }

    [Test]
    public void Save_AnExtraLine_IsNotTheTiersStats() {
        Rig rig = Rich();
        ItemStats stats = GemChain.Stats(2);
        stats[ItemStats.Type.Constant].Basic[BasicAttribute.Luck] = new BasicOption(1);
        var gem = new Item(rig.GemMeta(GemChain.Tier(2)), Constant.GemstoneGrade) { Uid = GemUid, Stats = stats };

        Assert.That(OfflineGearRepair.RepairGemstones(gem, rig.Calculator, items), Is.True);
        Assert.That(gem.Stats![ItemStats.Type.Constant].Count, Is.EqualTo(1));
    }

    [Test]
    public void Save_GemDust_IsNotAGem() {
        Rig rig = Rich();
        ItemStats held = GemChain.Stats(1);
        var dust = new Item(Meta(40300001, 14, ItemTag.DustBlue, 9999, GemChain.Option(5)), 1) { Uid = 9, Stats = held };

        Assert.That(OfflineGearRepair.RepairGemstones(dust, rig.Calculator, items), Is.False);
        Assert.That(dust.Stats, Is.SameAs(held));
    }

    [Test]
    public void Save_AGemTheDataHasNoRowFor_KeepsWhatItHas() {
        Rig rig = Rich();
        ItemStats noRow = GemChain.Stats(1), noItem = GemChain.Stats(1);
        rig.Accessory.Socket = new ItemSocket(3, 2);
        rig.Accessory.Socket.Sockets[0] = new ItemGemstone(40299001, stats: noRow); // an item with no Option
        rig.Accessory.Socket.Sockets[1] = new ItemGemstone(NoItemTier, stats: noItem);

        Assert.That(OfflineGearRepair.RepairGemstones(rig.Accessory, rig.Calculator, items), Is.False);
        Assert.That(rig.Accessory.Socket.Sockets[0]!.Stats, Is.SameAs(noRow));
        Assert.That(rig.Accessory.Socket.Sockets[1]!.Stats, Is.SameAs(noItem));
    }

    [Test]
    public void Save_GearWithEmptySockets_IsLeftAlone() {
        Rig rig = Rich();
        rig.Accessory.Socket = new ItemSocket(3, 2);

        Assert.That(OfflineGearRepair.RepairGemstones(rig.Accessory, rig.Calculator, items), Is.False);
    }

    // SO-03 (BT-196): a gem put into worn gear changes the totals at that press. Taking one out needs a field's item maker: criterion 5 pins that line.
    private static void PressPutOn(Rig rig, byte slot) {
        var writer = new ByteWriter();
        writer.WriteByte(8); // the handler's equip command
        writer.WriteLong(Target);
        writer.WriteLong(GemUid);
        writer.WriteByte(slot);
        rig.Handler.Handle(rig.Session, new ByteReader(writer.Buffer[..writer.Length]));
    }

    private Rig EmptySocket() {
        Rig rig = Rich();
        rig.Accessory.Socket!.Sockets[0] = null;
        rig.Hold(GemChain.Tier1, 1, GemUid);
        return rig;
    }

    [Test]
    public void Totals_WornGear_AreRebuiltAfterAGemGoesIn() {
        Rig rig = EmptySocket();
        Wear(rig);
        StatsManager stats = WatchRebuilds(rig);

        PressPutOn(rig, 0);

        Assert.That(rig.Accessory.Socket!.Sockets[0]!.ItemId, Is.EqualTo(GemChain.Tier1));
        Assert.That(Rebuilt(stats), Is.True);
    }

    [Test]
    public void Totals_WornGear_ARefusedGem_RebuildsNothing() {
        Rig rig = EmptySocket();
        Wear(rig);
        StatsManager stats = WatchRebuilds(rig);

        PressPutOn(rig, 1); // locked: 1 of 3 sockets is open

        rig.AssertError(ItemSocketError.s_itemsocketsystem_error_socket_lock);
        Assert.That(Rebuilt(stats), Is.False);
    }

    [Test]
    public void Totals_GearInTheBag_AGemGoingIn_RebuildsNothing() {
        Rig rig = EmptySocket();
        StatsManager stats = WatchRebuilds(rig);

        PressPutOn(rig, 0);

        Assert.That(rig.Accessory.Socket!.Sockets[0]!.ItemId, Is.EqualTo(GemChain.Tier1));
        Assert.That(Rebuilt(stats), Is.False);
    }
}

/// <summary>EN-02: what the run stands on, in the game's own table.</summary>
[Category("Data")]
[NonParallelizable]
public class AutoGemUpgradeDataTest {
    private SqliteConnection connection = null!;
    private MetadataContext context = null!;
    private IReadOnlyDictionary<int, Entry> table = null!;
    private ItemMetadataStorage items = null!;
    private ItemStatsCalculator calculator = null!;

    [OneTimeSetUp]
    public void SetUp() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        table = ((GemstoneUpgradeTable) context.TableMetadata.Find(TableNames.ITEM_GEMSTONE_UPGRADE)!.Table).Entries;
        items = new ItemMetadataStorage(context);
        calculator = new ItemStatsCalculator { TableMetadata = new TableMetadataStorage(context) };
    }

    [OneTimeTearDown]
    public void TearDown() {
        context?.Dispose();
        connection?.Dispose();
    }

    [Test]
    public void TheGoldenChain_IsTheGames() {
        foreach ((int id, Entry golden) in GemChain.Rows) {
            Entry row = table[id];
            Assert.That(row.Level, Is.EqualTo(golden.Level), $"{id}");
            Assert.That(row.NextItemId, Is.EqualTo(golden.NextItemId), $"{id}");
            Assert.That(row.Ingredients.OrderBy(ingredient => ingredient.ItemTag), Is.EqualTo(golden.Ingredients.OrderBy(ingredient => ingredient.ItemTag)), $"{id}");
        }
    }

    [Test]
    public void EveryUpgrade_ClimbsOneTier_AndHasAPrice() {
        foreach ((int id, Entry row) in table.Where(pair => pair.Value.NextItemId != 0)) {
            Assert.That(table.ContainsKey(row.NextItemId), Is.True, $"{id}: the next tier has a row");
            Assert.That(table[row.NextItemId].Level, Is.EqualTo(row.Level + 1), $"{id}");
            Assert.That(row.Ingredients, Is.Not.Empty, $"{id}");
        }
    }

    [Test]
    public void NoTierIsAboveTen_SoATargetAboveTenRunsToTheTop() {
        Assert.That(table.Values.Max(row => row.Level), Is.EqualTo(10));
    }

    [Test]
    public void EveryGemThatExists_HasItsNextTier_AndTheNumberIsTheTierInItsName() {
        Dictionary<int, string?> names = context.ItemMetadata.Where(item => item.Id >= 40000000 && item.Id < 41000000).Select(item => new { item.Id, item.Name }).ToDictionary(item => item.Id, item => item.Name);
        foreach ((int id, Entry row) in table.Where(pair => names.ContainsKey(pair.Key))) {
            if (row.NextItemId != 0) {
                Assert.That(names.ContainsKey(row.NextItemId), Is.True, $"{id}: its next tier is an item");
            }

            Match tier = Regex.Match(names[id] ?? string.Empty, @"\bTier (\d+)\b");
            if (tier.Success) {
                Assert.That(int.Parse(tier.Groups[1].Value), Is.EqualTo(row.Level), $"{id}: {names[id]}");
            }
        }
    }

    private ItemMetadata Gem(int id) {
        Assert.That(items.TryGet(id, out ItemMetadata? gem), Is.True, $"{id}");
        return gem!;
    }

    private ItemStats.Option Row(int id) => calculator.GetGemstoneStats(Gem(id))[ItemStats.Type.Constant];

    [Test]
    public void TheGoldenStats_AreTheGames() {
        for (int tier = 1; tier <= 10; tier++) {
            Assert.That(GemChain.IntelligenceOf(calculator.GetGemstoneStats(Gem(GemChain.Tier(tier)))), Is.EqualTo(GemChain.Intelligence[tier - 1]), $"tier {tier}");
        }
    }

    [Test]
    public void ARateGem_GivesItsRow() {
        // Tier 5 Destruction Gemstone.
        Assert.That(Row(40200205).Basic[BasicAttribute.MaxWeaponAtk], Is.EqualTo(new BasicOption(0.026f)));
    }

    [Test]
    public void AGemWhoseRowHasAnotherId_GivesThatRow() {
        // Tier 5 Wisdom Gemstone 40201605 reads 40200005's row.
        Assert.That(Gem(40201605).Option!.ConstantId, Is.EqualTo(40200005));
        Assert.That(Row(40201605).Basic[BasicAttribute.Intelligence].Value, Is.EqualTo(13));
    }

    // What the fix and the login pass stand on: a gem's stats are one row, nothing is rolled, so nothing a player holds is lost by giving it again.
    [Test]
    public void EveryGem_HasARow_AndRollsNothing() {
        foreach (int id in table.Keys.Where(id => items.TryGet(id, out _))) {
            ItemMetadataOption option = Gem(id).Option!;
            Assert.That((option.StaticId, option.RandomId, option.PickId), Is.EqualTo((0, 0, 0)), $"{id}");
            Assert.That(Row(id).Count, Is.GreaterThan(0), $"{id}");
        }
    }

    // A rebuild on worn gear adds the new tier's effects, and nothing takes the old tier's off: there is nothing to take off.
    [Test]
    public void EveryUpgrade_ChangesTheStats_AndKeepsTheEffects() {
        foreach ((int id, Entry row) in table.Where(pair => items.TryGet(pair.Key, out _) && items.TryGet(pair.Value.NextItemId, out _))) {
            Assert.That(Row(row.NextItemId).ToString(), Is.Not.EqualTo(Row(id).ToString()), $"{id}");
            Assert.That(Gem(row.NextItemId).AdditionalEffects, Is.EqualTo(Gem(id).AdditionalEffects), $"{id}");
        }
    }
}
