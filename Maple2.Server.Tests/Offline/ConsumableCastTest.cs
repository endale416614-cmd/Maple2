using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Session;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>IT-04 (BT-109): a cast of a consumable's own skill uses up one of it, once per cast, whether or not the skill data has useItem.</summary>
[Category("Data")]
[NonParallelizable]
public class ConsumableCastTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Elixir = 20000665; // PS potion; skill 90000662 has no useItem
    private const int ElixirSkill = 90000662;
    private const int MiniElixir = 20000021; // PS potion; skill 90000015 has useItem
    private const int MiniElixirSkill = 90000015;
    private const int Fireworks = 20000068; // FUN, skill 90000078
    private const int PetCandy = 63000000; // Dryad Pet Candy, throw skill 94000001
    private const int CandySplash = 94000002; // a skill the candy's throw sets off, not the candy's own
    private const int Voucher = 20000592; // Auto-Performance Voucher (30 Min), PS, skill 90000599
    private const int HeliumBalloon = 20300046; // Consumable tab, function AddAdditionalEffect, skill 90000237
    private const int LapentaEssence = 20300012; // Consumable tab, function AddAdditionalEffect, skill 100000003
    private const int Mount = 50600001; // Plucky Ducky, ground mount
    private const int Glide = 40400001; // Flying Balloon, air mount (glide skill)
    private const int Emote = 20200001; // Martial Shout Emote
    private const int Pet = 60000001; // Pengy
    private const int SkillBook = 40000010; // Action Skill Book: Swift Swim
    private const int Lapenshard = 41000011; // Celine's Heart Red Lapenshard (Tier 1)
    private const long StackUid = 1001;

    private SqliteConnection connection = null!;
    private MetadataContext context = null!;
    private SkillMetadataStorage skills = null!;
    private Dictionary<int, ItemMetadata> items = [];

    [OneTimeSetUp]
    public void Load() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        int[] ids = [Elixir, MiniElixir, Fireworks, PetCandy, Voucher, HeliumBalloon, LapentaEssence, Mount, Glide, Emote, Pet, SkillBook, Lapenshard];
        items = context.ItemMetadata.AsNoTracking().Where(item => ids.Contains(item.Id)).ToDictionary(item => item.Id);
        skills = new SkillMetadataStorage(context);
    }

    [OneTimeTearDown]
    public void TearDown() {
        context?.Dispose();
        connection?.Dispose();
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

    // A player whose inventory holds one stack of 11 in the item's own tab; no server or database.
    private (FieldPlayer Player, Item Stack) Build(int itemId) {
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>()));
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());

        var stack = new Item(items[itemId], 1, 11) { Uid = StackUid };
        var tab = new ItemCollection(10);
        Assert.That(tab.Add(stack), Is.Not.Empty);
        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [stack.Inventory] = tab });
        var itemManager = Bare<ItemManager>();
        SetField(itemManager, "Inventory", inventory);
        SetProperty(session, "Item", itemManager);

        var player = Bare<FieldPlayer>();
        SetField(player, "Session", session);
        SetField(player, "Logger", Log.Logger);
        return (player, stack);
    }

    private SkillRecord Cast(FieldPlayer player, int skillId, long castUid, long itemUid) {
        Assert.That(skills.TryGet(skillId, 1, out SkillMetadata? skill), Is.True, $"skill {skillId}");
        return new SkillRecord(skill!, castUid, player) { ItemUid = itemUid };
    }

    [Test]
    public void Elixir_EachCastUsesUpOne_ItsTickSyncDoesNot() {
        (FieldPlayer player, Item stack) = Build(Elixir);
        SkillRecord fromInventory = Cast(player, ElixirSkill, 1, StackUid);

        Assert.That(player.SkillCastConsume(fromInventory), Is.True);
        Assert.That(stack.Amount, Is.EqualTo(10));
        Assert.That(player.SkillCastConsume(fromInventory), Is.True, "TickSync and the state-skill tick pass the same cast again");
        Assert.That(stack.Amount, Is.EqualTo(10));

        // The quickslot sends the uid of the stack it holds (KeyTable QuickSlot.ItemUid), as a cast of its own.
        Assert.That(player.SkillCastConsume(Cast(player, ElixirSkill, 2, StackUid)), Is.True);
        Assert.That(stack.Amount, Is.EqualTo(9));
    }

    [Test]
    public void NoItem_AnotherSkill_OrAFunctionItem_UsesNothingUp() {
        (FieldPlayer player, Item stack) = Build(Elixir);
        Assert.That(player.SkillCastConsume(Cast(player, ElixirSkill, 1, 0)), Is.True, "a GM or trigger cast names no item");
        Assert.That(player.SkillCastConsume(Cast(player, CandySplash, 2, StackUid)), Is.True, "not the item's own skill");
        Assert.That(stack.Amount, Is.EqualTo(11));

        (player, stack) = Build(HeliumBalloon);
        Assert.That(player.SkillCastConsume(Cast(player, 90000662, 3, StackUid)), Is.True);
        Assert.That(stack.Amount, Is.EqualTo(11), "ItemUseHandler uses up a function item");
    }

    [Test]
    public void UseItemSkill_StillUsesUpOne() {
        (FieldPlayer player, Item stack) = Build(MiniElixir);
        Assert.That(player.SkillCastConsume(Cast(player, MiniElixirSkill, 1, StackUid)), Is.True);
        Assert.That(stack.Amount, Is.EqualTo(10));
    }

    // IT-05 (BT-113): the client casts the buff items' own skill and sends no ItemUse, so the cast uses one up, once per cast.
    [Test]
    public void BuffItems_TheirOwnSkillCastUsesUpOne() {
        foreach (int id in new[] { HeliumBalloon, LapentaEssence }) {
            (FieldPlayer player, Item stack) = Build(id);
            SkillRecord cast = Cast(player, stack.Metadata.Skill!.Id, 1, StackUid);
            Assert.That(player.SkillCastConsume(cast), Is.True, $"{id}");
            Assert.That(player.SkillCastConsume(cast), Is.True, $"{id}: TickSync passes the same cast again");
            Assert.That(stack.Amount, Is.EqualTo(10), $"{id}");
        }
    }

    [Test]
    public void OnlyConsumables_AreUsedUpByTheirOwnSkill() {
        foreach (int id in new[] { Elixir, MiniElixir, Fireworks, PetCandy, Voucher, HeliumBalloon, LapentaEssence }) {
            var item = new Item(items[id]);
            Assert.That(FieldPlayer.UsesUpOnCast(item, item.Metadata.Skill!.Id), Is.True, $"{id}");
        }
        Assert.That(FieldPlayer.UsesUpOnCast(new Item(items[PetCandy]), CandySplash), Is.False, "not the candy's own skill");
        foreach (int id in new[] { Mount, Glide, Emote, Pet, SkillBook, Lapenshard }) {
            var item = new Item(items[id]);
            Assert.That(FieldPlayer.UsesUpOnCast(item, item.Metadata.Skill!.Id), Is.False, $"{id}");
        }
    }
}
