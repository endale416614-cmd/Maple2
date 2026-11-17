using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Caching;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Game.Shop;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// TA-03 (BT-74): item_break, lapenshard_upgrade_try, lapenshard_upgrade_success, lapenshard_upgrade_result and the two shop token types
/// are reported where the action happens, with the target the trophy data names. Each case runs the real handler on a session with no server:
/// trophies of the type stand in for ConditionUpdate and their counters show what was reported.
/// No capture holds these reports: the code and target of each type are read from the achievement table (the lapenshard tier is inferred).
/// </summary>
public class TrophyReportTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int LapenshardId = 41000011, NextLapenshardId = 41000012, BrokenGearId = 11250046, BoughtId = 11250047;
    private const long GearUid = 1, ShardUid = 2, FodderUid = 3;

    private SqliteConnection metadataConnection = null!;
    private SqliteConnection saveConnection = null!;
    private MetadataContext metadata = null!;
    private ItemMetadataStorage itemStorage = null!;
    private GameStorage gameStorage = null!;

    [OneTimeSetUp]
    public void SetUp() {
        metadataConnection = new SqliteConnection("Data Source=:memory:");
        metadataConnection.Open();
        metadata = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(metadataConnection).Options);
        metadata.Database.EnsureCreated();
        metadata.ItemMetadata.Add(Meta(NextLapenshardId, 21, 99));
        metadata.SaveChanges();
        itemStorage = new ItemMetadataStorage(metadata);

        saveConnection = new SqliteConnection("Data Source=:memory:");
        saveConnection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(saveConnection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        gameStorage = new GameStorage(options, itemStorage, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
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

    private static ItemMetadata Meta(int id, int type, int slotMax = 1) {
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
    }

    // A trophy that stays at its first grade whatever is counted, so its counter shows how often its type was reported with a matching target.
    private static Achievement Trophy(int id, ConditionType type, params int[] target) {
        var grades = new Dictionary<int, AchievementMetadataGrade> {
            [1] = new(1, new ConditionMetadata(type, 1_000_000, null, target.Length > 0 ? new ConditionMetadata.Parameters(Integers: target) : null), null),
        };
        return new Achievement(new AchievementMetadata(id, $"{id}", false, AchievementCategory.Life, [], grades)) { CurrentGrade = 1 };
    }

    private sealed class Rig {
        public required GameSession Session { get; init; }
        public required BlockingCollection<(byte[], int)> Queue { get; init; }
        public required Dictionary<ConditionType, Achievement> Trophies { get; init; }
        public required Achievement WrongTier { get; init; }

        public long Counted(ConditionType type) => Trophies[type].Counter;
    }

    private Rig Session(Dictionary<int, Item>? gear = null, Dictionary<int, Item>? shards = null, bool field = false) {
        var queue = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", queue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());

        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "trophy" }, new Character { Name = "Trophy", Mastery = null! }, 0) {
            Home = null!,
            Currency = new Currency { ValorToken = 100, Treva = 100 },
            Unlock = null!,
        });
        SetProperty(session, "Player", player);
        if (field) {
            SetProperty(session, "Field", Bare<FieldManager>());
        }
        SetProperty(session, "GameStorage", gameStorage);
        SetProperty(session, "ItemMetadata", itemStorage);

        // One trophy per type; the lapenshard result also has a trophy for another tier, which must stay at zero.
        ConditionType[] types = [
            ConditionType.item_break, ConditionType.lapenshard_upgrade_try, ConditionType.lapenshard_upgrade_success,
            ConditionType.lapenshard_upgrade_result, ConditionType.shop_buy_honor_token, ConditionType.shop_buy_karma_token,
        ];
        var trophies = new Dictionary<ConditionType, Achievement>();
        foreach (ConditionType type in types) {
            trophies[type] = type == ConditionType.lapenshard_upgrade_result ? Trophy(2 + (int) type, type, 2) : Trophy(2 + (int) type, type);
        }
        Achievement wrongTier = Trophy(1, ConditionType.lapenshard_upgrade_result, 6);

        var cache = new LRUCache<int, AchievementMetadata>(20, 1);
        foreach (Achievement trophy in trophies.Values.Append(wrongTier)) {
            cache.AddReplace(trophy.Id, trophy.Metadata);
        }
        var storage = Bare<AchievementMetadataStorage>();
        SetField(storage, "Context", Bare<MetadataContext>());
        SetField(storage, "Cache", cache);
        SetField(storage, "cachedTypes", new HashSet<ConditionType>(Enum.GetValues<ConditionType>()));
        SetProperty(session, "AchievementMetadata", storage);
        session.Achievement = Bare<AchievementManager>();
        SetField(session.Achievement, "session", session);
        SetField(session.Achievement, "accountValues", new Dictionary<int, Achievement>());
        SetField(session.Achievement, "characterValues", trophies.Values.Append(wrongTier).ToDictionary(trophy => trophy.Id));
        session.Quest = Bare<QuestManager>();
        SetField(session.Quest, "session", session);
        SetField(session.Quest, "accountValues", new Dictionary<int, Quest>());
        SetField(session.Quest, "characterValues", new Dictionary<int, Quest>());

        var gearTab = new ItemCollection(100);
        foreach (Item item in (gear ?? []).Values) {
            Assert.That(gearTab.Add(item), Is.Not.Empty);
        }
        var shardTab = new ItemCollection(100);
        foreach (Item item in (shards ?? []).Values) {
            Assert.That(shardTab.Add(item), Is.Not.Empty);
        }
        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [InventoryType.Gear] = gearTab, [InventoryType.Lapenshard] = shardTab });
        var items = Bare<ItemManager>();
        SetField(items, "Inventory", inventory);
        SetProperty(session, "Item", items);

        var currency = new CurrencyManager(session);
        SetProperty(session, "Currency", currency);
        var tables = Bare<ServerTableMetadataStorage>();
        SetField(tables, "constantsTable", new Lazy<ConstantsTable>(() => new ConstantsTable(HonorTokenMax: 1000, KarmaTokenMax: 1000)));
        SetProperty(session, "ServerTableMetadata", tables);

        return new Rig { Session = session, Queue = queue, Trophies = trophies, WrongTier = wrongTier };
    }

    [Test]
    public void Dismantle_ReportsTheItemsBroken() {
        var gear = new Item(Meta(BrokenGearId, 1, int.MaxValue), 4, 3) { Uid = GearUid };
        Rig rig = Session(gear: new Dictionary<int, Item> { [0] = gear });
        var tables = Bare<TableMetadataStorage>();
        SetField(tables, "itemBreakTable", new Lazy<ItemBreakTable>(() => new ItemBreakTable(new Dictionary<int, IReadOnlyList<ItemBreakTable.Ingredient>>())));
        var handler = new ItemDismantleHandler { ItemMetadata = null!, TableMetadata = tables };
        rig.Session.DismantleOpened = true;
        SetField(rig.Session, "DismantleStaging", new (long, int)[8]);
        rig.Session.DismantleStaging[0] = (GearUid, 2);

        handler.Handle(rig.Session, new ByteReader([3]));

        Assert.That(rig.Counted(ConditionType.item_break), Is.EqualTo(2), "the amount dismantled, not the rows");
    }

    [Test]
    public void LapenshardUpgrade_ReportsTryAndSuccessAndTheTierReached() {
        var shard = new Item(Meta(LapenshardId, 21, 99), 1) { Uid = ShardUid };
        var fodder = new Item(Meta(LapenshardId, 21, 99), 1, 3) { Uid = FodderUid };
        Rig rig = Session(shards: new Dictionary<int, Item> { [0] = shard, [1] = fodder }, field: true);
        LapenshardHandler handler = LapenshardHandlerFor(level: 1);

        handler.Handle(rig.Session, UpgradePacket(ShardUid, FodderUid, 3));

        Assert.That(rig.Counted(ConditionType.lapenshard_upgrade_try), Is.EqualTo(1));
        Assert.That(rig.Counted(ConditionType.lapenshard_upgrade_success), Is.EqualTo(1));
        Assert.That(rig.Counted(ConditionType.lapenshard_upgrade_result), Is.EqualTo(1), "a level 1 shard becomes tier 2");
        Assert.That(rig.WrongTier.Counter, Is.Zero, "the trophy for tier 6 is not touched by a tier 2");
    }

    [Test]
    public void LapenshardUpgrade_WithTheWrongFodderCount_ReportsNothing() {
        var shard = new Item(Meta(LapenshardId, 21, 99), 1) { Uid = ShardUid };
        var fodder = new Item(Meta(LapenshardId, 21, 99), 1, 3) { Uid = FodderUid };
        Rig rig = Session(shards: new Dictionary<int, Item> { [0] = shard, [1] = fodder }, field: true);
        LapenshardHandler handler = LapenshardHandlerFor(level: 1);

        handler.Handle(rig.Session, UpgradePacket(ShardUid, FodderUid, 2));

        Assert.That(rig.Trophies.Values.Concat([rig.WrongTier]).Sum(trophy => trophy.Counter), Is.Zero);
    }

    private LapenshardHandler LapenshardHandlerFor(short level) {
        var tables = Bare<TableMetadataStorage>();
        var entries = new Dictionary<int, LapenshardUpgradeTable.Entry> {
            [LapenshardId] = new(level, LapenshardId, NextLapenshardId, 3, [], 0),
        };
        SetField(tables, "lapenshardUpgradeTable", new Lazy<LapenshardUpgradeTable>(() => new LapenshardUpgradeTable(entries)));
        return new LapenshardHandler { TableMetadata = tables, ItemMetadata = itemStorage };
    }

    private static ByteReader UpgradePacket(long shardUid, long fodderUid, int fodderAmount) {
        var writer = new ByteWriter();
        writer.WriteByte(5); // Command.Upgrade
        writer.WriteLong(shardUid);
        writer.WriteInt(LapenshardId);
        writer.Write(default(LapenshardSlot)); // unused for an inventory shard
        writer.WriteInt(1);
        writer.WriteLong(fodderUid);
        writer.WriteInt(fodderAmount);
        return new ByteReader(writer.Buffer[..writer.Length]);
    }

    [TestCase(ShopCurrencyType.ValorToken, ConditionType.shop_buy_honor_token, ConditionType.shop_buy_karma_token)]
    [TestCase(ShopCurrencyType.Treva, ConditionType.shop_buy_karma_token, ConditionType.shop_buy_honor_token)]
    public void ShopBuy_ReportsTheTokensSpent(ShopCurrencyType paid, ConditionType reported, ConditionType other) {
        Rig rig = Session(gear: new Dictionary<int, Item>(), field: true);
        var shopManager = Bare<ShopManager>();
        var cost = new ShopCost { Type = paid, Amount = 14 };
        var requirement = new ShopItemMetadata.Requirement(0, new ShopItemMetadata.Achievement(0, 0), new ShopItemMetadata.Championship(0, 0),
            new ShopItemMetadata.GuildNpc(default, 0), new ShopItemMetadata.QuestAlliance(default, 0));
        var itemMetadata = new ShopItemMetadata(5, 1, BoughtId, 1, cost, 0, string.Empty, requirement, null, 1, default, string.Empty, false, false, 0, false);
        var shop = new Shop(new ShopMetadata(1, 0, "shop", default, false, false, false, false, false, false, false, 0, false,
            new ShopRestockData(default, default, default, 0, 0, 0, false, true, false)));
        shop.Items[5] = new ShopItem(itemMetadata) { Item = new Item(Meta(BoughtId, 1), 1) { Uid = 99 } };
        SetField(shopManager, "session", rig.Session);
        SetField(shopManager, "activeShop", shop);
        SetField(shopManager, "logger", Log.Logger);

        shopManager.Buy(5, 1);

        Assert.That(rig.Counted(reported), Is.EqualTo(14), "the price, the tokens spent");
        Assert.That(rig.Counted(other), Is.Zero);
        Assert.That(rig.Session.Currency[paid == ShopCurrencyType.ValorToken ? CurrencyType.ValorToken : CurrencyType.Treva], Is.EqualTo(86));
    }
}
