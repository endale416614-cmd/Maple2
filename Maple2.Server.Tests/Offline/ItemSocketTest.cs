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
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using NUnit.Framework;
using Serilog;
using static Maple2.Server.Game.PacketHandlers.ItemSocketHandler;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// SO-01 (BT-175, R-21): which socket a slot names, and an unlock with no copy, on a session with no server or database.
/// No capture holds a socket packet. Golden: the KMS v81 client: FUN_14062a3d0 says locked for slot >= unlocked and shows slot + 1,
/// and its Unlock press refuses no catalyst ("Select a material item.").
/// </summary>
public class ItemSocketTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Target = 1, CopyA = 2, CopyB = 3, GemUid = 4, CrystalUid = 5;
    private const int ItemId = 11250046, GemId = 40200001, CrystalId = 40100026;
    private const int UnlockCost = 200; // Lua.CalcItemSocketUnlockIngredient at level 50, rank 4
    private const byte CommandUnlock = 0, CommandEquip = 8, CommandUnequip = 10;
    private const byte ReplyUnlock = 1, ReplyEquip = 9, ReplyError = 18;

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
        public required GameSession Session { get; init; }
        public required Item Item { get; init; }
        public required ItemCollection Bag { get; init; }
        public required ItemCollection Gems { get; init; }
        public required Item Crystals { get; init; }
        public required BlockingCollection<(byte[], int)> Queue { get; init; }

        public void Press(byte command, Action<ByteWriter> body) {
            var writer = new ByteWriter();
            writer.WriteByte(command);
            body(writer);
            new ItemSocketHandler { ItemMetadata = null!, TableMetadata = null!, ItemStatsCalc = null! }.Handle(Session, new ByteReader(writer.Buffer[..writer.Length]));
        }

        public void Unlock(params long[] copies) => Press(CommandUnlock, writer => {
            writer.WriteLong(Target);
            writer.WriteByte((byte) copies.Length);
            foreach (long copy in copies) {
                writer.WriteLong(copy);
            }
        });

        public void Equip(byte slot) => Press(CommandEquip, writer => {
            writer.WriteLong(Target);
            writer.WriteLong(GemUid);
            writer.WriteByte(slot);
        });

        public void Unequip(byte slot) => Press(CommandUnequip, writer => {
            writer.WriteLong(Target);
            writer.WriteByte(slot);
        });

        // The one socket packet sent since the last call, whole: op (2 bytes), command, the rest.
        public byte[] Reply() {
            var packets = new List<byte[]>();
            while (Queue.TryTake(out (byte[] Packet, int Length) sent)) {
                if ((SendOp) (sent.Packet[1] << 8 | sent.Packet[0]) == SendOp.ItemSocketSystem) {
                    packets.Add(sent.Packet);
                }
            }
            return packets.Single();
        }

        public void AssertError(byte code, ItemSocketError error) {
            byte[] reply = Reply();
            Assert.That(reply[2], Is.EqualTo(ReplyError));
            Assert.That(reply[3], Is.EqualTo(code));
            Assert.That(BitConverter.ToInt32(reply, 4), Is.EqualTo((int) error));
        }
    }

    // Rank 4 gear with 1 of 3 sockets open, 2 clean copies, a gem and the fragments for one unlock.
    private static Rig Build() {
        var queue = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", queue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());

        ItemMetadata gear = Meta(ItemId, 1, ItemTag.None, 1);
        var item = new Item(gear, 4) { Uid = Target, Socket = new ItemSocket(3, 1) };
        var bag = new ItemCollection(100);
        Assert.That(bag.Add(item), Is.Not.Empty);
        foreach (long uid in new[] { CopyA, CopyB }) {
            Assert.That(bag.Add(new Item(gear, 4) { Uid = uid, Socket = new ItemSocket(3, 1) }), Is.Not.Empty);
        }
        var gems = new ItemCollection(100);
        Assert.That(gems.Add(new Item(Meta(GemId, 13, ItemTag.None, 1), 1) { Uid = GemUid, Stats = new ItemStats() }), Is.Not.Empty);
        var crystals = new Item(Meta(CrystalId, 15, ItemTag.CrystalPiece, int.MaxValue), 1, UnlockCost) { Uid = CrystalUid };
        var catalysts = new ItemCollection(100);
        Assert.That(catalysts.Add(crystals), Is.Not.Empty);

        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> {
            [InventoryType.Gear] = bag,
            [InventoryType.Gemstone] = gems,
            [InventoryType.Catalyst] = catalysts,
        });
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

        return new Rig { Session = session, Item = item, Bag = bag, Gems = gems, Crystals = crystals, Queue = queue };
    }

    [TestCase(1, SocketState.Empty, TestName = "Slot: the last open slot")]
    [TestCase(2, SocketState.Locked, TestName = "Slot: one past the last open slot is locked")]
    [TestCase(3, SocketState.None, TestName = "Slot: MaxSlots is past the last slot")]
    public void SomeSocketsOpen(int slot, SocketState expected) {
        Assert.That(CheckSocketSlot(new ItemSocket(3, 2), (byte) slot), Is.EqualTo(expected));
    }

    [TestCase(2, SocketState.Empty, TestName = "Slot: all open, the last slot")]
    [TestCase(3, SocketState.None, TestName = "Slot: all open, one past the last slot")]
    public void AllSocketsOpen(int slot, SocketState expected) {
        Assert.That(CheckSocketSlot(new ItemSocket(3, 3), (byte) slot), Is.EqualTo(expected));
    }

    [Test]
    public void NoSocketOpen_SlotZeroIsLocked() {
        Assert.That(CheckSocketSlot(new ItemSocket(3, 0), 0), Is.EqualTo(SocketState.Locked));
    }

    [Test]
    public void NoSockets_IsNone() {
        Assert.That(CheckSocketSlot(null, 0), Is.EqualTo(SocketState.None));
        Assert.That(CheckSocketSlot(ItemSocket.Default, 0), Is.EqualTo(SocketState.None));
    }

    [Test]
    public void AGemInTheLastOpenSlot_IsUsed() {
        var socket = new ItemSocket(3, 2);
        socket.Sockets[1] = new ItemGemstone(GemId, stats: new ItemStats());
        Assert.That(CheckSocketSlot(socket, 1), Is.EqualTo(SocketState.Used));
    }

    [Test]
    public void Equip_TheLastOpenSlot_TakesTheGem() {
        Rig rig = Build();

        rig.Equip(0);

        Assert.That(rig.Reply()[2], Is.EqualTo(ReplyEquip));
        Assert.That(rig.Item.Socket!.Sockets[0]!.ItemId, Is.EqualTo(GemId));
        Assert.That(rig.Gems, Is.Empty);
    }

    [Test]
    public void Equip_OnePastTheLastOpenSlot_SaysLocked_AndKeepsTheGem() {
        Rig rig = Build();

        rig.Equip(1);

        rig.AssertError(0, ItemSocketError.s_itemsocketsystem_error_socket_lock);
        Assert.That(rig.Gems.Select(gem => gem.Uid), Is.EqualTo(new[] { GemUid }));
        Assert.That(rig.Item.Socket!.UnlockSlots, Is.EqualTo(1));
    }

    [Test]
    public void Equip_AtMaxSlots_IsNoSocket() {
        Rig rig = Build();

        rig.Equip(3);

        rig.AssertError(8, ItemSocketError.s_itemsocketsystem_error_server_default);
        Assert.That(rig.Gems.Select(gem => gem.Uid), Is.EqualTo(new[] { GemUid }));
    }

    [Test]
    public void Unequip_OnePastTheLastOpenSlot_SaysLocked() {
        Rig rig = Build();

        rig.Unequip(1);

        rig.AssertError(0, ItemSocketError.s_itemsocketsystem_error_socket_lock);
    }

    [Test]
    public void Unlock_WithNoCopy_IsRefused_AndNothingIsPaid() {
        Rig rig = Build();

        rig.Unlock();

        rig.AssertError(0, ItemSocketError.s_itemsocketsystem_error_invalid_target_ingredient_count);
        Assert.That(rig.Item.Socket!.UnlockSlots, Is.EqualTo(1));
        Assert.That(rig.Crystals.Amount, Is.EqualTo(UnlockCost));
        Assert.That(rig.Bag.Count(), Is.EqualTo(3));
    }

    [TestCase(1, TestName = "Unlock: one copy opens a socket")]
    [TestCase(2, TestName = "Unlock: the two copies the KMS client asks open one socket")]
    public void Unlock_WithCopies_OpensOneSocket_AndSpendsThem(int copies) {
        Rig rig = Build();

        rig.Unlock(new[] { CopyA, CopyB }.Take(copies).ToArray());

        Assert.That(rig.Reply()[2], Is.EqualTo(ReplyUnlock));
        Assert.That(rig.Item.Socket!.UnlockSlots, Is.EqualTo(2));
        Assert.That(rig.Session.Item.Inventory.Get(CrystalUid), Is.Null, "the fragments are paid");
        Assert.That(rig.Bag.Count(), Is.EqualTo(3 - copies));
    }
}
