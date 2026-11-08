using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>IV-01 (BT-85): an item above its slot maximum counts its slots before any fill, so a refused add changes nothing, and an add that fits uses up its source.
/// Golden: Crystal Fragments (300,000) 20303291 gives 40100039 x300000, SlotMax 99999; quest 10002793 gives the dagger 13100254 x2, SlotMax 1.</summary>
public class InventoryStackAddTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int CrystalFragment = 40100039; // SlotMax 99999, Untradeable, Catalyst tab
    private const int Dagger = 13100254; // (Intro) Lightweight Main-gauche: SlotMax 1, BindOnLoot, Gear tab
    private const int GuildCoin = 30000861; // SlotMax 9999, BindOnLoot, Currency tab

    private sealed class Rig {
        public required GameSession Session { get; init; }
        public required InventoryManager Inventory { get; init; }
        public required ItemCollection Bag { get; init; }
        public required List<Item> Delete { get; init; }
    }

    private SqliteConnection metadataConnection = null!;
    private SqliteConnection saveConnection = null!;
    private MetadataContext metadata = null!;
    private ItemMetadataStorage items = null!;
    private AchievementMetadataStorage achievements = null!;
    private GameStorage storage = null!;

    [OneTimeSetUp]
    public void SetUp() {
        metadataConnection = new SqliteConnection("Data Source=:memory:");
        metadataConnection.Open();
        metadata = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(metadataConnection).Options);
        metadata.Database.EnsureCreated();
        metadata.ItemMetadata.AddRange(Meta(CrystalFragment, 99999, 15, TransferType.Untradeable), Meta(Dagger, 1, 1, TransferType.BindOnLoot), Meta(GuildCoin, 9999, 20, TransferType.BindOnLoot));
        metadata.SaveChanges();
        items = new ItemMetadataStorage(metadata);
        achievements = new AchievementMetadataStorage(metadata);

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

    private static ItemMetadata Meta(int id, int slotMax, int type, TransferType transfer) {
        var property = new ItemMetadataProperty(false, 0, slotMax, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, transfer, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, new ItemMetadataHousing(0, 0, 0, HousingCategory.None, false), new ItemMetadataInstall(true, 0, 0, false, MapAttribute.none));
    }

    private Item New(int itemId, int amount, long uid = 0) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? item), Is.True, $"item {itemId}");
        return new Item(item!, 1, amount) { Uid = uid };
    }

    // An inventory tab of this size with the real item creation and an empty save; no server. Trophies and quests are empty.
    private Rig Build(int itemId, int slots) {
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>()));
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());

        var player = Bare<FieldPlayer>();
        SetField(player, "<Value>k__BackingField", new Player(new Account { Username = "stocker" }, new Character { Name = "Stocker", Mastery = null! }, 0) {
            Home = null!,
            Currency = null!,
            Unlock = null!,
        });
        SetProperty(session, "Player", player);
        SetProperty(session, "GameStorage", storage);
        SetProperty(session, "ItemMetadata", items);
        SetProperty(session, "AchievementMetadata", achievements);

        var field = Bare<FieldManager>();
        SetProperty(field, "ItemMetadata", items);
        SetProperty(field, "ItemStatsCalc", new ItemStatsCalculator { TableMetadata = null! });
        SetField(field, "<ItemDrop>k__BackingField", new ItemDropManager(field));
        SetProperty(session, "Field", field);

        var bag = new ItemCollection((short) slots);
        var delete = new List<Item>();
        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", delete);
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [New(itemId, 1).Inventory] = bag });
        var itemManager = Bare<ItemManager>();
        SetField(itemManager, "Inventory", inventory);
        SetProperty(session, "Item", itemManager);

        var achievement = Bare<AchievementManager>();
        SetField(achievement, "session", session);
        SetProperty(session, "Achievement", achievement);
        var quest = Bare<QuestManager>();
        SetField(quest, "session", session);
        SetField(quest, "accountValues", new Dictionary<int, Quest>());
        SetField(quest, "characterValues", new Dictionary<int, Quest>());
        SetProperty(session, "Quest", quest);

        return new Rig { Session = session, Inventory = inventory, Bag = bag, Delete = delete };
    }

    private Item Hold(ItemCollection bag, int itemId, int amount, long uid) {
        Item held = New(itemId, amount, uid);
        Assert.That(bag.Add(held), Is.Not.Empty);
        return held;
    }

    [Test]
    public void SlotsNeeded_OnAnEmptyTab_IsTheStacks() {
        var bag = new ItemCollection(20);
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 99999)), Is.EqualTo(1));
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 100000)), Is.EqualTo(2));
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 300000)), Is.EqualTo(4), "99999 x3 and 3");
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 1000000)), Is.EqualTo(11), "20303283: 99999 x10 and 10");
        Assert.That(bag.SlotsNeeded(New(Dagger, 2)), Is.EqualTo(2));
    }

    [Test]
    public void SlotsNeeded_CountsOnlyWhatDoesNotStack() {
        var bag = new ItemCollection(20);
        Hold(bag, CrystalFragment, 50000, 55);
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 49999)), Is.Zero);
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 50000)), Is.EqualTo(1));
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 300000)), Is.EqualTo(3), "49999 on the stack, then 99999 x2 and 50003");
        Assert.That(bag.SlotsNeeded(New(CrystalFragment, 300000), 49999), Is.Zero, "the amount asked for");
    }

    [Test]
    public void RefusedAdd_ChangesNothing() {
        // Two open slots, and the box's 300000 on a stack of 50000 needs 3: before, the stack took 49999 and the whole item was mailed too.
        Rig rig = Build(CrystalFragment, 3);
        Item stack = Hold(rig.Bag, CrystalFragment, 50000, 55);
        Item add = New(CrystalFragment, 300000);

        Assert.That(rig.Inventory.Add(add, true), Is.False);

        Assert.That(stack.Amount, Is.EqualTo(50000));
        Assert.That(add.Amount, Is.EqualTo(300000), "the caller mails or refunds the whole item");
        Assert.That(rig.Bag.Count, Is.EqualTo(1));
        Assert.That(rig.Delete, Is.Empty);
        var error = ItemInventoryPacket.Error(ItemInventoryError.s_err_inventory);
        Assert.That(rig.Session.GetLastSentPacket(SendOp.ItemInventory), Is.EqualTo(error.Buffer.Take(error.Length).ToArray()));
        Assert.That(rig.Inventory.CanAdd(add), Is.False, "CanAdd counts as Add does");
    }

    [Test]
    public void AddThatFits_EndsInFullStacks() {
        Rig rig = Build(CrystalFragment, 4);
        Hold(rig.Bag, CrystalFragment, 50000, 55);
        Item add = New(CrystalFragment, 300000);

        Assert.That(rig.Inventory.CanAdd(add), Is.True);
        Assert.That(rig.Inventory.Add(add, true), Is.True);

        Assert.That(rig.Bag.Select(item => item.Amount), Is.EqualTo(new[] { 99999, 99999, 99999, 50003 }));
    }

    // Whatever the tab holds, the count is exactly the slots the add takes: with no slot to spare no later step can refuse.
    [TestCase(0, 99999 + 1)]
    [TestCase(0, 300000)]
    [TestCase(1, 300000)]
    [TestCase(50000, 300000)]
    [TestCase(99998, 300000)]
    [TestCase(99998, 99999 * 3 + 1)]
    public void TheCount_IsTheSlotsTheAddTakes(int held, int amount) {
        var count = new ItemCollection(20);
        if (held > 0) {
            Hold(count, CrystalFragment, held, 55);
        }
        int needed = count.SlotsNeeded(New(CrystalFragment, amount));

        Rig rig = Build(CrystalFragment, needed + (held > 0 ? 1 : 0));
        if (held > 0) {
            Hold(rig.Bag, CrystalFragment, held, 55);
        }

        Assert.That(rig.Inventory.Add(New(CrystalFragment, amount), true), Is.True);
        Assert.That(rig.Bag.OpenSlots, Is.Zero);
        Assert.That(rig.Bag.Sum(item => (long) item.Amount), Is.EqualTo(held + amount));
        Assert.That(rig.Bag.Max(item => item.Amount), Is.LessThanOrEqualTo(99999));
    }

    [Test]
    public void AStackBoundToTheCharacter_Counts() {
        // What the add creates binds on loot, so it joins a bound stack: 9 on the stack and 9991 in the 1 open slot.
        Rig rig = Build(GuildCoin, 2);
        Item stack = Hold(rig.Bag, GuildCoin, 9990, 55);
        stack.Transfer!.Bind(rig.Session.Player.Value.Character);

        Assert.That(rig.Inventory.Add(New(GuildCoin, 10000), true), Is.True);

        Assert.That(rig.Bag.Select(item => item.Amount), Is.EqualTo(new[] { 9999, 9991 }));
    }

    [Test]
    public void AStackItCannotJoin_CostsNoExtraSlot() {
        // The fill step offers the unbound stack 9, which lands in a slot of its own; the later stacks finish that slot. Before, this was refused after that fill.
        Rig rig = Build(GuildCoin, 3);
        rig.Bag[1] = New(GuildCoin, 9990, 55);

        Assert.That(rig.Inventory.Add(New(GuildCoin, 9999 * 2), true), Is.True);

        Assert.That(rig.Bag.Select(item => item.Amount), Is.EqualTo(new[] { 9999, 9990, 9999 }));
    }

    [Test]
    public void AddThatFits_UsesUpItsSource() {
        // A mail's attachment of x2: two new daggers replace its row, which must leave the mail at the next save or the next login offers it again.
        Rig rig = Build(Dagger, 2);
        Item attachment = New(Dagger, 2, 77);

        Assert.That(rig.Inventory.Add(attachment, notifyNew: true, commit: false), Is.True);

        Assert.That(rig.Bag.Select(item => item.Amount), Is.EqualTo(new[] { 1, 1 }));
        Assert.That(rig.Delete, Does.Contain(attachment));
    }

    [Test]
    public void TheDaggers_NeedTwoSlots() {
        Rig rig = Build(Dagger, 1);
        Item add = New(Dagger, 2);

        Assert.That(rig.Inventory.Add(add, true), Is.False);
        Assert.That(add.Amount, Is.EqualTo(2));
        Assert.That(rig.Bag.Count, Is.Zero);
        Assert.That(rig.Inventory.CanAdd(add), Is.False, "a pickup would take them off the ground and lose them");
    }

    [Test]
    public void CanAdd_WithinOneStack_IsAsBefore() {
        Rig rig = Build(CrystalFragment, 1);
        Hold(rig.Bag, CrystalFragment, 50000, 55);
        Assert.That(rig.Inventory.CanAdd(New(CrystalFragment, 49999)), Is.True, "fits on the stack");
        Assert.That(rig.Inventory.CanAdd(New(CrystalFragment, 50000)), Is.False, "the tab is full");
        Assert.That(rig.Inventory.CanAdd(New(CrystalFragment, 50000), 49999), Is.True, "the amount asked for");

        rig = Build(CrystalFragment, 2);
        Hold(rig.Bag, CrystalFragment, 50000, 55);
        Assert.That(rig.Inventory.CanAdd(New(CrystalFragment, 99999)), Is.True, "an open slot");
    }
}
