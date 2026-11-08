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
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Game.Event;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Commands;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Maple2.Server.Tests.Offline;

/// <summary>RR-02 (R-19): /autoreroll's run, its stops, the line a name gives and the lines an item can roll. There are no charges in this window: the price rising per attempt is AutoRerollRunTest's.</summary>
public class AutoRerollTest {
    private static readonly LockOption Rate = new(BasicAttribute.CriticalRate);
    private static readonly LockOption Boss = new(SpecialAttribute.BossNpcDamage);

    private static Func<bool> Rolls(params bool[] rolls) {
        var queue = new Queue<bool>(rolls);
        return queue.Dequeue;
    }

    // Pays this many attempts, then says why not.
    private static Func<ChangeAttributesError?> Pays(int attempts, ChangeAttributesError then = ChangeAttributesError.s_itemremake_error_server_lack_price) {
        return () => attempts-- > 0 ? null : then;
    }

    [Test]
    public void TheLine_StopsTheRun() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(100, Pays(1000), Rolls(false, false, true));
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(3, true, null)));
    }

    [Test]
    public void Materials_StopTheRun_WithTheAttemptsPaid() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(100, Pays(2), () => false);
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(2, false, ChangeAttributesError.s_itemremake_error_server_lack_price)));
    }

    [Test]
    public void TheLockScrolls_StopTheRun_WithTheAttemptsPaid() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(100, Pays(1, ChangeAttributesError.s_itemremake_error_server_default), () => false);
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(1, false, ChangeAttributesError.s_itemremake_error_server_default)));
    }

    [Test]
    public void NothingPaid_IsNoAttempt_AndNoRollIsRead() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(100, Pays(0), () => throw new AssertionException("nothing was rolled"));
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(0, false, ChangeAttributesError.s_itemremake_error_server_lack_price)));
    }

    [Test]
    public void TheCap_StopsTheRun_AndAttemptsNoMore() {
        int attempts = 0;
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(OfflineAutoReroll.MaxAttempts, () => {
            attempts++;
            return null;
        }, () => false);
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(OfflineAutoReroll.MaxAttempts, false, null)));
        Assert.That(attempts, Is.EqualTo(OfflineAutoReroll.MaxAttempts));
    }

    [Test]
    public void TheLine_OnTheLastAttemptCounts() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(2, Pays(1000), Rolls(false, true));
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(2, true, null)));
    }

    [Test]
    public void AClick_IsARunOfOne() {
        OfflineAutoReroll.RunResult run = OfflineAutoReroll.Run(1, Pays(1000), () => false);
        Assert.That(run, Is.EqualTo(new OfflineAutoReroll.RunResult(1, false, null)));
    }

    [Test]
    public void HasLine_ReadsTheRightKind() {
        var lines = new ItemStats.Option(
            new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.CriticalRate] = new(5) },
            new Dictionary<SpecialAttribute, SpecialOption> { [SpecialAttribute.BossNpcDamage] = new(0.01f) });
        Assert.That(OfflineAutoReroll.HasLine(lines, Rate), Is.True);
        Assert.That(OfflineAutoReroll.HasLine(lines, Boss), Is.True);
        Assert.That(OfflineAutoReroll.HasLine(lines, new LockOption(BasicAttribute.Health)), Is.False);
        Assert.That(OfflineAutoReroll.HasLine(lines, new LockOption(SpecialAttribute.TotalDamage)), Is.False);

        // CriticalDamage is in both enums: the name of a basic line is not the special line.
        var basicOnly = new ItemStats.Option(new Dictionary<BasicAttribute, BasicOption> { [BasicAttribute.CriticalDamage] = new(5) });
        Assert.That(OfflineAutoReroll.HasLine(basicOnly, new LockOption(SpecialAttribute.CriticalDamage)), Is.False);
    }

    [TestCase("CriticalRate", "CriticalRate")]
    [TestCase("criticalrate", "CriticalRate")]
    [TestCase("BOSSNPCDAMAGE", "BossNpcDamage")]
    [TestCase("CriticalDamage", "CriticalDamage")]
    public void Parse_TakesANameInAnyCase(string name, string expected) {
        LockOption? line = OfflineAutoReroll.Parse(name);
        Assert.That(line, Is.Not.Null);
        Assert.That(OfflineAutoReroll.Name(line!.Value), Is.EqualTo(expected));
    }

    [Test]
    public void Parse_CriticalDamage_IsTheBasicLine() {
        Assert.That(OfflineAutoReroll.Parse("CriticalDamage")!.Value.TryGet(out BasicAttribute basic, out _), Is.True);
        Assert.That(basic, Is.EqualTo(BasicAttribute.CriticalDamage));
    }

    [TestCase("")]
    [TestCase("7")]
    [TestCase("None")]
    [TestCase("Unknown")]
    [TestCase("Strength,Luck")]
    [TestCase("high")]
    public void Parse_RefusesAnythingElse(string name) {
        Assert.That(OfflineAutoReroll.Parse(name), Is.Null);
    }

    [Test]
    public void Set_TurnsOnAtALine_AndOffAlone() {
        const long character = -176001;
        Assert.That(OfflineAutoReroll.Set(character, null), Is.Null, "off until typed; alone it stays off");
        Assert.That(OfflineAutoReroll.Name(OfflineAutoReroll.Set(character, Rate)!.Value), Is.EqualTo("CriticalRate"));
        Assert.That(OfflineAutoReroll.Name(OfflineAutoReroll.Set(character, Boss)!.Value), Is.EqualTo("BossNpcDamage"), "a new line stays on");
        Assert.That(OfflineAutoReroll.Set(character, null), Is.Null);
    }

    [Test]
    public void Message_PrintsTheStateTheTargetAndTheBurn() {
        Assert.That(OfflineAutoReroll.Message(null), Does.Contain("OFF"));
        Assert.That(OfflineAutoReroll.Message(Rate), Does.Contain("ON").And.Contain("CriticalRate").And.Contain("materials").And.Contain("lock scrolls").And.Contain("100").And.Contain("/autoreroll alone"));
    }

    [TestCase("Legacy", null, false, TestName = "Legacy: the command is off")]
    [TestCase("Adapted", null, true, TestName = "Adapted: the command is on")]
    [TestCase(null, null, true, TestName = "No mode: the command is on")]
    public void TheFlag_FollowsTheMode(string? mode, string? flag, bool expected) {
        string source = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "Util", "OfflineAutoReroll.cs"));
        Assert.That(source, Does.Contain("OfflineProfile.Flag(\"OFFLINE_AUTOREROLL\", true)"));
        Assert.That(OfflineProfile.Resolve(mode, flag, adaptedDefault: true), Is.EqualTo(expected));
    }

    [Test]
    public void Command_PrintsThatItIsOff() {
        var console = new TestConsole();
        int code = new AutoRerollCommand(null!, enabled: false).Invoke(["autoreroll", "CriticalRate"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("off in Legacy"));
    }

    [Test]
    public void Command_RefusesAnUnknownLine() {
        var console = new TestConsole();
        int code = new AutoRerollCommand(null!, enabled: true).Invoke(["autoreroll", "high", "rate"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("high rate").And.Contain("guide"));
    }

    private static ItemOption Table(params ItemOption.Entry[] entries) => new(1f, new ItemOption.Range<int>(2, 2), entries);

    private static ItemOption.Entry Row(BasicAttribute attribute) => new(BasicAttribute: attribute, Values: new ItemOption.Range<int>(1, 1));

    private static ItemOption.Entry Row(SpecialAttribute attribute) => new(SpecialAttribute: attribute, Rates: new ItemOption.Range<float>(0.01f, 0.01f));

    private static readonly ItemType Armor = new(1, 13);
    private static readonly ItemType Weapon = new(1, 50);
    private static readonly ItemType Accessory = new(1, 12);
    private static readonly ItemType Pet = new(6, 10);
    private static readonly ItemOption Pool = Table(Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes), Row(BasicAttribute.CriticalRate), Row(BasicAttribute.Piercing),
        Row(SpecialAttribute.PhysicalPiercing), Row(SpecialAttribute.FireDamage), Row(SpecialAttribute.IceDamage), Row(SpecialAttribute.ReduceStun));

    private static bool CanRoll(ItemType type, int lines, LockOption line, params LockOption[] locks) => ItemStatsCalculator.CanRoll(Pool, type, lines, line, locks);

    [Test]
    public void CanRoll_ALineInTheTable() {
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.Health)), Is.True);
        Assert.That(CanRoll(Armor, 2, new LockOption(SpecialAttribute.ReduceStun)), Is.True);
    }

    [Test]
    public void CanRoll_NotALineOutsideTheTable() {
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.Strength)), Is.False);
        Assert.That(CanRoll(Armor, 2, new LockOption(SpecialAttribute.TotalDamage)), Is.False);
    }

    [Test]
    public void CanRoll_NotTheOtherKindOfTheSameName() {
        ItemOption basicOnly = Table(Row(BasicAttribute.CriticalDamage));
        Assert.That(ItemStatsCalculator.CanRoll(basicOnly, Weapon, 2, new LockOption(BasicAttribute.CriticalDamage)), Is.True);
        Assert.That(ItemStatsCalculator.CanRoll(basicOnly, Weapon, 2, new LockOption(SpecialAttribute.CriticalDamage)), Is.False);
    }

    [Test]
    public void CanRoll_Golden_OneLineArmorNeverRollsAnOffenseLine() {
        // Data: armor with 1 line holds Physical Piercing and Magic Piercing in its table, and the 50 % rule refuses them.
        Assert.That(CanRoll(Armor, 1, new LockOption(SpecialAttribute.PhysicalPiercing)), Is.False);
        Assert.That(CanRoll(Armor, 1, new LockOption(BasicAttribute.Piercing)), Is.False);
        Assert.That(CanRoll(Armor, 1, new LockOption(BasicAttribute.Health)), Is.True);
    }

    [Test]
    public void CanRoll_ArmorHoldsOneOffenseLineInTwo() {
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.CriticalRate)), Is.True);
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.CriticalRate), new LockOption(BasicAttribute.Health, true)), Is.True);
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.CriticalRate), new LockOption(BasicAttribute.Piercing, true)), Is.False);
    }

    [TestCase(1, TestName = "Weapon")]
    [TestCase(2, TestName = "Accessory")]
    [TestCase(3, TestName = "Pet")]
    public void CanRoll_TheOffenseRuleIsArmorsOnly(int kind) {
        ItemType type = kind == 1 ? Weapon : kind == 2 ? Accessory : Pet;
        Assert.That(CanRoll(type, 1, new LockOption(BasicAttribute.CriticalRate)), Is.True);
        Assert.That(CanRoll(type, 2, new LockOption(BasicAttribute.CriticalRate), new LockOption(BasicAttribute.Piercing, true)), Is.True);
    }

    [Test]
    public void CanRoll_OneDamageTypeLineOnly() {
        Assert.That(CanRoll(Weapon, 2, new LockOption(SpecialAttribute.FireDamage), new LockOption(SpecialAttribute.IceDamage, true)), Is.False);
        Assert.That(CanRoll(Weapon, 2, new LockOption(SpecialAttribute.FireDamage), new LockOption(BasicAttribute.Health, true)), Is.True);
    }

    [Test]
    public void CanRoll_ALockedLineComesBack() {
        Assert.That(CanRoll(Armor, 1, new LockOption(BasicAttribute.Health), new LockOption(BasicAttribute.Health, true)), Is.True);
        Assert.That(CanRoll(Armor, 2, new LockOption(BasicAttribute.Strength), new LockOption(BasicAttribute.Strength, true)), Is.True, "outside the table too: the lock restores it");
    }

    [Test]
    public void CanRoll_NoLineLeftAfterTheLocks() {
        Assert.That(CanRoll(Armor, 1, new LockOption(BasicAttribute.MagicalRes), new LockOption(BasicAttribute.Health, true)), Is.False);
        Assert.That(CanRoll(Armor, 0, new LockOption(BasicAttribute.Health)), Is.False, "no line to roll: the window refuses as it does");
    }
}

/// <summary>RR-02: a press on a session with no server or database, through the real handler, roll, inventory and trophies: what a run pays, what stays, and the one result the client gets.</summary>
public class AutoRerollRunTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const long Upgrade = 1;
    private const int Hat = 11300073; // Epic at rarity 4, armor, level 50
    private const int RandomId = 11300073;
    private const int PetId = 61000079; // the captured Balrog
    private const byte Preview = 1, Error = 4;
    private const int Plenty = 100000000;
    private static readonly BasicAttribute[] Basics = [BasicAttribute.Health, BasicAttribute.MagicalRes, BasicAttribute.PhysicalRes, BasicAttribute.Evasion, BasicAttribute.CriticalEvasion, BasicAttribute.MovementSpeed];
    private static readonly SpecialAttribute[] Specials = Enum.GetValues<SpecialAttribute>().Where(attribute => attribute != SpecialAttribute.None).ToArray();
    private static long lastCharacter = -176100;

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

    private static ItemMetadata Meta(int id, ItemTag tag = ItemTag.None, int slotMax = 1, ItemMetadataOption? option = null) {
        var property = new ItemMetadataProperty(false, 0, slotMax, 0, 0, string.Empty, string.Empty, tag, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 50, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], option, null, null, null);
    }

    private static ItemOption.Entry Row(BasicAttribute attribute) => new(BasicAttribute: attribute, Values: new ItemOption.Range<int>(1, 9));

    private static ItemOption.Entry Row(SpecialAttribute attribute) => new(SpecialAttribute: attribute, Rates: new ItemOption.Range<float>(0.01f, 0.09f));

    private sealed class Rig {
        public required GameSession Session { get; init; }
        public required ChangeAttributesHandler Handler { get; init; }
        public required Item Item { get; init; }
        public required ItemCollection Catalysts { get; init; }
        public required ItemCollection Misc { get; init; }
        public required BlockingCollection<(byte[], int)> Queue { get; init; }
        public required Dictionary<ItemTag, int> Given { get; init; }
        public required Achievement Attempts { get; init; }
        public required Achievement Record { get; init; }
        public required Dictionary<int, GameEvent> Events { get; init; }

        public void Press(BasicAttribute? locked = null) {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write((byte) 0); // Change
            writer.Write(Item.Uid);
            writer.Write(0L);
            writer.Write(locked != null);
            if (locked != null) {
                writer.Write(false);
                writer.Write((short) locked.Value);
            }
            writer.Flush();
            Handler.Handle(Session, new ByteReader(stream.ToArray()));
        }

        // The window's packets and the trophy lines since the last call, in order: 1 and 4 are the window's commands, 0 a trophy line.
        public byte[] Sent() {
            var sent = new List<byte>();
            while (Queue.TryTake(out (byte[] Packet, int Length) packet)) {
                var op = (SendOp) (packet.Packet[1] << 8 | packet.Packet[0]);
                if (op == SendOp.ChangeAttributes) {
                    sent.Add(packet.Packet[2]);
                } else if (op == SendOp.Achieve) {
                    sent.Add(0);
                }
            }
            return sent.ToArray();
        }

        public int Spent(ItemTag tag) => Given[tag] - Catalysts.Where(item => item.Metadata.Property.Tag == tag).Sum(item => item.Amount);

        public void Scrolls(ItemTag tag, int rarity, int amount) {
            Assert.That(Misc.Add(new Item(Meta(30061172, tag, 100, new ItemMetadataOption(0, 0, 0, 0, rarity, 0, 0, 0)), rarity, amount) { Uid = 300 }), Is.Not.Empty);
        }

        public int ScrollsLeft() => Misc.Sum(item => item.Amount);

        public ItemStats.Option Rolled() => Session.ChangeAttributesItem!.Stats![ItemStats.Type.Random];

        // Events the servers list, as GameServer.FindEvent reads them (RR-03).
        public void Sale(params GameEventMetadata[] sales) {
            foreach (GameEventMetadata sale in sales) {
                Events[sale.Id] = new GameEvent(sale);
            }
        }
    }

    // The item with the first lines of the pool, the materials given and, when target names a line, the mode on.
    private static Rig Build(int itemId, int rarity, ItemOption.Entry[] pool, int lines, LockOption? target, params (ItemTag Tag, int Amount)[] materials) {
        Assume.That(OfflineAutoReroll.Enabled, Is.True, "the command is off in this environment");
        var queue = new BlockingCollection<(byte[], int)>(new ConcurrentQueue<(byte[], int)>());
        var session = Bare<GameSession>();
        SetField(session, "Logger", Log.Logger);
        SetField(session, "sendQueue", queue);
        SetField(session, "lastSentPackets", new ConcurrentDictionary<SendOp, byte[]>());
        long character = Interlocked.Decrement(ref lastCharacter);
        SetProperty(session, "CharacterId", character);
        OfflineAutoReroll.Set(character, target);
        // A server with no events, so a press pays the list price until a test adds a sale (RR-03).
        var server = Bare<Maple2.Server.Game.GameServer>();
        var events = new Dictionary<int, GameEvent>();
        SetField(server, "eventCache", events);
        SetField(session, "server", server);

        var stats = new ItemStats();
        foreach (ItemOption.Entry entry in pool.Take(lines)) {
            if (entry.BasicAttribute != null) {
                stats[ItemStats.Type.Random].Basic[entry.BasicAttribute.Value] = new BasicOption(1);
            } else {
                stats[ItemStats.Type.Random].Special[entry.SpecialAttribute!.Value] = new SpecialOption(0.01f);
            }
        }
        var item = new Item(Meta(itemId, option: new ItemMetadataOption(0, 0, RandomId, ItemOptionMakeType.Range, 0, 0, 55, 0)), rarity) { Uid = Upgrade, Stats = stats };

        var gear = new ItemCollection(100);
        var pets = new ItemCollection(100);
        var catalysts = new ItemCollection(100);
        var misc = new ItemCollection(100);
        Assert.That((item.Type.IsCombatPet ? pets : gear).Add(item), Is.Not.Empty);
        var given = new Dictionary<ItemTag, int>();
        long uid = 100;
        foreach ((ItemTag tag, int amount) in materials) {
            given[tag] = amount;
            Assert.That(catalysts.Add(new Item(Meta(40100001 + (int) uid, tag, int.MaxValue), 1, amount) { Uid = uid++ }), Is.Not.Empty);
        }

        var inventory = Bare<InventoryManager>();
        SetField(inventory, "session", session);
        SetField(inventory, "delete", new List<Item>());
        SetField(inventory, "logger", Log.Logger);
        SetField(inventory, "tabs", new Dictionary<InventoryType, ItemCollection> { [InventoryType.Gear] = gear, [InventoryType.Pets] = pets, [InventoryType.Catalyst] = catalysts, [InventoryType.Misc] = misc });
        var items = Bare<ItemManager>();
        SetField(items, "Inventory", inventory);
        var equips = Bare<EquipManager>();
        SetField(equips, "Gear", new ConcurrentDictionary<EquipSlot, Item>());
        SetField(items, "Equips", equips);
        SetProperty(session, "Item", items);

        // The trophies with every grade earned, so a press only moves their counters. Golden: the grades of 23200024 and 23200025; 23200038 and 23200039 are the same.
        bool isPet = item.Type.IsCombatPet;
        var attempts = Trophy(isPet ? 23200038 : 23200024, isPet ? ConditionType.pet_remake_option : ConditionType.item_remake_option, 1, 30, 100, 300, 1000);
        var record = Trophy(isPet ? 23200039 : 23200025, isPet ? ConditionType.pet_remake_option_record : ConditionType.item_remake_option_record, 1, 10, 20);
        var cache = new LRUCache<int, AchievementMetadata>(2, 1);
        cache.AddReplace(attempts.Id, attempts.Metadata);
        cache.AddReplace(record.Id, record.Metadata);
        var trophies = Bare<AchievementMetadataStorage>();
        SetField(trophies, "Context", Bare<MetadataContext>());
        SetField(trophies, "Cache", cache);
        SetField(trophies, "cachedTypes", new HashSet<ConditionType>(Enum.GetValues<ConditionType>()));
        SetProperty(session, "AchievementMetadata", trophies);
        session.Achievement = Bare<AchievementManager>();
        SetField(session.Achievement, "session", session);
        SetField(session.Achievement, "accountValues", new Dictionary<int, Achievement>());
        SetField(session.Achievement, "characterValues", new Dictionary<int, Achievement> { [attempts.Id] = attempts, [record.Id] = record });
        session.Quest = Bare<QuestManager>();
        SetField(session.Quest, "accountValues", new Dictionary<int, Quest>());
        SetField(session.Quest, "characterValues", new Dictionary<int, Quest>());

        var table = new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>> {
            [RandomId] = new Dictionary<int, ItemOption> { [rarity] = new(1f, new ItemOption.Range<int>(lines, lines), pool) },
        });
        var none = new ItemEquipVariationTable(new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<int>[]>(), new Dictionary<BasicAttribute, ItemEquipVariationTable.Set<float>[]>(),
            new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<int>[]>(), new Dictionary<SpecialAttribute, ItemEquipVariationTable.Set<float>[]>());
        var tables = Bare<TableMetadataStorage>();
        SetField(tables, "itemOptionRandomTable", new Lazy<ItemOptionRandomTable>(() => table));
        SetField(tables, "armorVariationTable", new Lazy<ItemEquipVariationTable>(() => none));
        SetField(tables, "petVariationTable", new Lazy<ItemEquipVariationTable>(() => none));

        return new Rig {
            Session = session,
            Handler = new ChangeAttributesHandler { ItemStatsCalc = new ItemStatsCalculator { TableMetadata = tables } },
            Item = item,
            Catalysts = catalysts,
            Misc = misc,
            Queue = queue,
            Given = given,
            Attempts = attempts,
            Record = record,
            Events = events,
        };
    }

    private static Achievement Trophy(int id, ConditionType type, params long[] values) {
        Dictionary<int, AchievementMetadataGrade> grades = values.Select((value, index) => new AchievementMetadataGrade(index + 1, new ConditionMetadata(type, value, null, null), null)).ToDictionary(grade => grade.Grade);
        return new Achievement(new AchievementMetadata(id, $"{id}", false, AchievementCategory.Life, [], grades)) {
            CurrentGrade = values.Length,
            Grades = grades.Keys.ToDictionary(grade => grade, _ => 1L),
        };
    }

    private static ItemOption.Entry[] BigPool() => Basics.Select(Row).Concat(Specials.Where(attribute => attribute is not (SpecialAttribute.IceDamage or SpecialAttribute.FireDamage)).Select(Row)).ToArray();

    private static readonly LockOption OneOfMany = new(SpecialAttribute.ReduceStun);

    private static (ItemTag, int)[] PlentyOfMaterials() => [(ItemTag.BlueCrystal, Plenty), (ItemTag.CrystalPiece, Plenty), (ItemTag.MetaCell, Plenty)];

    // Golden: Epic armor piece at n earlier attempts: 1 crystal, floor(200 x 1.25^n) Fragments, 11 + n Metacells; 1.24 from n = 10; n stops at 14 (the client's calcGetItemRemakeIngredientNew).
    private static int Fragments(int attempts, int from = 0) => Enumerable.Range(from, attempts).Sum(n => (int) Math.Floor(200 * Math.Pow(Math.Min(n, 14) < 10 ? 1.25 : 1.24, Math.Min(n, 14))));

    private static int Metacells(int attempts, int from = 0) => Enumerable.Range(from, attempts).Sum(n => 11 + Math.Min(n, 14));

    [Test]
    public void TheModeOff_APressIsOneAttempt_AsAClick() {
        Rig rig = Build(Hat, 4, BigPool(), 2, null, PlentyOfMaterials());

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(200));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(11));
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }), "the preview, then 23200024 and 23200025");
        Assert.That(rig.Attempts.Counter, Is.EqualTo(1));
        Assert.That(rig.Record.Counter, Is.EqualTo(1));
    }

    [Test]
    public void AClickRefused_SpendsNothing_AndKeepsTheLastPreview() {
        Rig rig = Build(Hat, 4, BigPool(), 2, null, (ItemTag.BlueCrystal, 1), (ItemTag.CrystalPiece, 200), (ItemTag.MetaCell, 11));
        rig.Press();
        Item preview = rig.Session.ChangeAttributesItem!;
        rig.Sent();

        rig.Press();

        Assert.That(rig.Sent(), Is.EqualTo(new[] { Error }));
        Assert.That(rig.Session.ChangeAttributesItem, Is.SameAs(preview));
        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
    }

    [Test]
    public void TheLineAlone_HitsOnTheFirstRoll() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(SpecialAttribute.ReduceStun)], 2, new LockOption(SpecialAttribute.ReduceStun), PlentyOfMaterials());

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1), "both lines of 2 come every time");
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));
    }

    [Test]
    [Repeat(30)]
    public void ARun_EndsOnTheLine_PaysEachAttemptsOwnPrice_OneResult() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes), Row(BasicAttribute.PhysicalRes), Row(SpecialAttribute.ReduceStun)], 1, new LockOption(SpecialAttribute.ReduceStun), PlentyOfMaterials());
        Dictionary<BasicAttribute, BasicOption> before = new(rig.Item.Stats![ItemStats.Type.Random].Basic);

        rig.Press();

        int attempts = rig.Item.TimeChangedOption;
        Assert.That(attempts, Is.InRange(1, OfflineAutoReroll.MaxAttempts));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(attempts));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(Fragments(attempts)));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(Metacells(attempts)));
        Assert.That(rig.Rolled().Special.ContainsKey(SpecialAttribute.ReduceStun), Is.True, "1 of 4 within 100 rolls");
        Assert.That(rig.Rolled().Count, Is.EqualTo(1));
        Assert.That(rig.Session.ChangeAttributesItem!.TimeChangedOption, Is.EqualTo(attempts));
        Assert.That(rig.Item.Stats[ItemStats.Type.Random].Basic, Is.EqualTo(before), "the item keeps its lines until Apply");
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }), "one preview and one line per trophy, whatever the attempts");
    }

    [Test]
    [Repeat(30)]
    public void ARun_ReportsItsAttempts_AndTheRecordAsItsState() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes), Row(SpecialAttribute.ReduceStun)], 1, new LockOption(SpecialAttribute.ReduceStun), PlentyOfMaterials());
        rig.Item.TimeChangedOption = 5;
        rig.Attempts.Counter = 300;
        rig.Record.Counter = 3;

        rig.Press();

        int attempts = rig.Item.TimeChangedOption - 5;
        Assert.That(attempts, Is.GreaterThan(0));
        Assert.That(rig.Attempts.Counter, Is.EqualTo(300 + attempts), "224 adds up: what this many clicks add");
        Assert.That(rig.Record.Counter, Is.EqualTo(5 + attempts), "225 is the item's own count: earlier attempts on it count");
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(Fragments(attempts, from: 5)));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(Metacells(attempts, from: 5)));
    }

    [Test]
    public void TheRecord_KeepsAHigherCount() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes)], 2, new LockOption(BasicAttribute.Health), PlentyOfMaterials());
        rig.Record.Counter = 19;

        rig.Press();

        Assert.That(rig.Record.Counter, Is.EqualTo(19));
    }

    [Test]
    [Repeat(10)]
    public void ARun_StopsAtTheCap() {
        // 1 of 162 (183 rows, 21 refused on 1-line armor): about half the runs reach the cap, and none passes it.
        Rig rig = Build(Hat, 4, BigPool(), 1, OneOfMany, PlentyOfMaterials());

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.InRange(1, OfflineAutoReroll.MaxAttempts));
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(Fragments(rig.Item.TimeChangedOption)));
    }

    [Test]
    public void Materials_EndARun_WithTheLastRoll_AndNoError() {
        Rig rig = Build(Hat, 4, BigPool(), 1, OneOfMany, (ItemTag.BlueCrystal, 9), (ItemTag.CrystalPiece, 200), (ItemTag.MetaCell, 9000));

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1), "fragments for one attempt only");
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(11), "nothing is paid for the attempt that fails");
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));
        Assert.That(rig.Session.ChangeAttributesItem, Is.Not.Null);
        Assert.That(rig.Attempts.Counter, Is.EqualTo(1));

        rig.Press();

        Assert.That(rig.Sent(), Is.EqualTo(new[] { Error }), "the next press is refused as a click is");
        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.Attempts.Counter, Is.EqualTo(1));
    }

    [Test]
    public void TheLockScroll_EndsARun_WithTheLineKept() {
        Rig rig = Build(Hat, 6, BigPool(), 2, OneOfMany, PlentyOfMaterials());
        rig.Scrolls(ItemTag.LockItemOptionArmor, 6, 1);
        rig.Item.Stats![ItemStats.Type.Random].Basic[BasicAttribute.Health] = new BasicOption(777);

        rig.Press(BasicAttribute.Health);

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1), "1 scroll is 1 attempt");
        Assert.That(rig.ScrollsLeft(), Is.Zero);
        Assert.That(rig.Rolled().Basic[BasicAttribute.Health], Is.EqualTo(new BasicOption(777)));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(5), "Ascendant armor: 5 crystals");
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));

        rig.Press(BasicAttribute.Health);

        Assert.That(rig.Sent(), Is.EqualTo(new[] { Error }));
        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(5), "no scroll, nothing spent");
    }

    [Test]
    [Repeat(10)]
    public void ALockedRun_PaysOneScrollAnAttempt() {
        Rig rig = Build(Hat, 6, BigPool(), 2, OneOfMany, PlentyOfMaterials());
        rig.Scrolls(ItemTag.LockItemOptionArmor, 6, 3);

        rig.Press(BasicAttribute.Health);

        int attempts = rig.Item.TimeChangedOption;
        Assert.That(attempts, Is.InRange(1, 3));
        Assert.That(rig.ScrollsLeft(), Is.EqualTo(3 - attempts));
        Assert.That(rig.Rolled().Basic.ContainsKey(BasicAttribute.Health), Is.True);
    }

    [Test]
    public void AScrollOfAnotherRarity_RefusesTheFirstAttempt() {
        // Golden: RR-01's rule. 30061172 is rarity 6 in the data.
        Rig rig = Build(Hat, 5, BigPool(), 2, OneOfMany, PlentyOfMaterials());
        rig.Scrolls(ItemTag.LockItemOptionArmor, 6, 3);

        rig.Press(BasicAttribute.Health);

        Assert.That(rig.Sent(), Is.EqualTo(new[] { Error }));
        Assert.That(rig.Item.TimeChangedOption, Is.Zero);
        Assert.That(rig.ScrollsLeft(), Is.EqualTo(3));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.Zero);
    }

    [Test]
    public void ALockedTarget_IsOneAttempt() {
        Rig rig = Build(Hat, 6, BigPool(), 2, new LockOption(BasicAttribute.Health), PlentyOfMaterials());
        rig.Scrolls(ItemTag.LockItemOptionArmor, 6, 3);

        rig.Press(BasicAttribute.Health);

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.ScrollsLeft(), Is.EqualTo(2));
    }

    [Test]
    public void ALineTheItemCanNotRoll_IsOneOrdinaryAttempt() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes), Row(BasicAttribute.PhysicalRes)], 1, new LockOption(BasicAttribute.Strength), PlentyOfMaterials());

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(200));
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));
    }

    [Test]
    public void AnOffenseLineOnOneLineArmor_IsOneOrdinaryAttempt() {
        Rig rig = Build(Hat, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.MagicalRes), Row(SpecialAttribute.PhysicalPiercing)], 1, new LockOption(SpecialAttribute.PhysicalPiercing), PlentyOfMaterials());

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
    }

    [Test]
    [Repeat(10)]
    public void APet_RunsAndReportsAsAPet() {
        // Golden: CalcGetPetRemakeIngredient at rarity 4, n = 0 and 1: 10 crystals, 300 then 342 Fragments, 500 then 600 Metacells; the materials end before n = 2.
        Rig rig = Build(PetId, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.AttackSpeed), Row(BasicAttribute.Accuracy)], 1, new LockOption(BasicAttribute.Accuracy),
            (ItemTag.RedCrystal, 20), (ItemTag.CrystalPiece, 642), (ItemTag.MetaCell, 1100));

        rig.Press();

        int attempts = rig.Item.TimeChangedOption;
        Assert.That(attempts, Is.InRange(1, 2));
        Assert.That(rig.Spent(ItemTag.RedCrystal), Is.EqualTo(10 * attempts));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(attempts == 1 ? 300 : 642));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(attempts == 1 ? 500 : 1100));
        Assert.That(rig.Attempts.Id, Is.EqualTo(23200038));
        Assert.That(rig.Attempts.Counter, Is.EqualTo(attempts));
        Assert.That(rig.Record.Counter, Is.EqualTo(attempts));
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));
    }

    [Test]
    public void TheSale_HalvesALegendaryArmorAttempt() {
        // Golden: a Legendary armor piece asks 5 crystals, 400 Fragments and 165 Metacells at n = 0, and 5, 800 and 180 at n = 1 (Lua.cs, GR-01).
        // Half, rounded down: 2, 200, 82, then 2, 400, 90.
        Rig rig = Build(Hat, 5, BigPool(), 2, null, PlentyOfMaterials());
        rig.Sale(OfflineRemakeSale.Events(true));

        rig.Press();

        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(2));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(200));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(82));
        Assert.That(rig.Sent(), Is.EqualTo(new byte[] { Preview, 0, 0 }));

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(2));
        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(4));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(600));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(172));
    }

    [Test]
    public void TheSale_HalvesAPetAttempt() {
        // Golden: a rarity 4 pet asks 10 crystals, 300 Fragments and 500 Metacells at n = 0 (CalcGetPetRemakeIngredient).
        Rig rig = Build(PetId, 4, [Row(BasicAttribute.Health), Row(BasicAttribute.AttackSpeed), Row(BasicAttribute.Accuracy)], 1, null,
            (ItemTag.RedCrystal, 100), (ItemTag.CrystalPiece, 1000), (ItemTag.MetaCell, 1000));
        rig.Sale(OfflineRemakeSale.Events(true));

        rig.Press();

        Assert.That(rig.Item.TimeChangedOption, Is.EqualTo(1));
        Assert.That(rig.Spent(ItemTag.RedCrystal), Is.EqualTo(5));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(150));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(250));
    }

    [Test]
    public void ASaleForPets_LeavesGearAtItsPrice() {
        Rig rig = Build(Hat, 5, BigPool(), 2, null, PlentyOfMaterials());
        rig.Sale(OfflineRemakeSale.Events(true).Where(sale => sale.Data is SaleRemake { Kind: SaleRemake.Pets }).ToArray());

        rig.Press();

        Assert.That(rig.Spent(ItemTag.BlueCrystal), Is.EqualTo(5));
        Assert.That(rig.Spent(ItemTag.CrystalPiece), Is.EqualTo(400));
        Assert.That(rig.Spent(ItemTag.MetaCell), Is.EqualTo(165));
    }

    [Test]
    public void TheCommand_TurnsOnAtALine_InOneOrMoreWords_AndOffAlone() {
        var session = Bare<GameSession>();
        long character = Interlocked.Decrement(ref lastCharacter);
        SetProperty(session, "CharacterId", character);
        Assume.That(OfflineAutoReroll.Enabled, Is.True, "the command is off in this environment");

        var console = new TestConsole();
        Assert.That(new AutoRerollCommand(session, enabled: true).Invoke(["autoreroll", "critical", "rate"], console), Is.Zero);
        Assert.That(console.Out.ToString(), Does.Contain("ON").And.Contain("CriticalRate").And.Contain("materials"));
        Assert.That(OfflineAutoReroll.Name(OfflineAutoReroll.Target(character)!.Value), Is.EqualTo("CriticalRate"));

        console = new TestConsole();
        Assert.That(new AutoRerollCommand(session, enabled: true).Invoke(["autoreroll", "BossNpcDamage"], console), Is.Zero);
        Assert.That(OfflineAutoReroll.Name(OfflineAutoReroll.Target(character)!.Value), Is.EqualTo("BossNpcDamage"));

        console = new TestConsole();
        Assert.That(new AutoRerollCommand(session, enabled: true).Invoke(["autoreroll", "nothing"], console), Is.EqualTo(1));
        Assert.That(OfflineAutoReroll.Name(OfflineAutoReroll.Target(character)!.Value), Is.EqualTo("BossNpcDamage"), "a wrong name changes nothing");

        console = new TestConsole();
        Assert.That(new AutoRerollCommand(session, enabled: true).Invoke(["autoreroll"], console), Is.Zero);
        Assert.That(console.Out.ToString(), Does.Contain("OFF"));
        Assert.That(OfflineAutoReroll.Target(character), Is.Null);
    }
}

/// <summary>RR-02: every line in the game's random tables has one name, the one /autoreroll takes.</summary>
[Category("Data")]
[NonParallelizable]
public class AutoRerollDataTest {
    [Test]
    public void EveryLineInTheTablesIsItsOwnName() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        var table = (ItemOptionRandomTable) context.TableMetadata.Find(TableNames.ITEM_OPTION_RANDOM)!.Table;

        var lines = new HashSet<string>();
        foreach (ItemOption option in table.Options.Values.SelectMany(byRarity => byRarity.Values)) {
            foreach (ItemOption.Entry entry in option.Entries) {
                if (entry.BasicAttribute is { } basic) {
                    Assert.That(OfflineAutoReroll.Parse(basic.ToString())!.Value.TryGet(out BasicAttribute parsed, out _), Is.True, basic.ToString());
                    Assert.That(parsed, Is.EqualTo(basic));
                    lines.Add(basic.ToString());
                } else if (entry.SpecialAttribute is { } special) {
                    Assert.That(OfflineAutoReroll.Parse(special.ToString())!.Value.TryGet(out SpecialAttribute parsed, out _), Is.True, $"{special}: the basic line of this name would be taken");
                    Assert.That(parsed, Is.EqualTo(special));
                    lines.Add(special.ToString());
                }
            }
        }
        Assert.That(lines, Does.Contain("CriticalDamage").And.Contain("CriticalRate").And.Contain("BossNpcDamage"));
    }
}
