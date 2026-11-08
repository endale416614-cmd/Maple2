using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>RR-01: a lock scroll serves gear of its kind at the rarity its data gives; a pet candy serves every pet (BT-176).
/// Golden: the item rows (30061172 tag 106, 30061173 tag 107, both ConstantId 6; candy 30000923 tag 108, ConstantId 3) and shop 322 rows 21 and 22.</summary>
[TestFixture]
public class LockScrollTest {
    private static readonly ItemType Weapon = new(1, 30);
    private static readonly ItemType Armor = new(1, 13);
    private static readonly ItemType Accessory = new(1, 12);
    private static readonly ItemType Pet = new(6, 10);

    [Test]
    public void TheSoldScrollsServeAscendantGear() {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, 6, Armor, 6), Is.True);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionAccessory, 6, Accessory, 6), Is.True);
    }

    [TestCase(4)]
    [TestCase(5)]
    public void TheSoldScrollsDoNotServeLowerGear(int gearRarity) {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, 6, Armor, gearRarity), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionAccessory, 6, Accessory, gearRarity), Is.False);
    }

    [TestCase(4, 4, true)]
    [TestCase(5, 5, true)]
    [TestCase(6, 6, true)]
    [TestCase(4, 5, false)]
    [TestCase(4, 6, false)]
    [TestCase(6, 5, false)]
    public void AScrollServesOnlyItsRarity(int scrollRarity, int gearRarity, bool expected) {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionWeapon, scrollRarity, Weapon, gearRarity), Is.EqualTo(expected));
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, scrollRarity, Armor, gearRarity), Is.EqualTo(expected));
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionAccessory, scrollRarity, Accessory, gearRarity), Is.EqualTo(expected));
    }

    [Test]
    public void AScrollServesOnlyItsKind() {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, 6, Weapon, 6), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, 6, Accessory, 6), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, 6, Pet, 6), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionWeapon, 6, Armor, 6), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionAccessory, 6, Armor, 6), Is.False);
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(6)]
    public void APetCandyServesEveryPet(int petRarity) {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionPet, 3, Pet, petRarity), Is.True);
    }

    [Test]
    public void APetCandyServesNoGear() {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionPet, 3, Armor, 3), Is.False);
    }

    [Test]
    public void NoOptionDataOrAnotherTagServesNothing() {
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.LockItemOptionArmor, null, Armor, 6), Is.False);
        Assert.That(ChangeAttributesHandler.LockScrollFits(ItemTag.None, 6, Armor, 6), Is.False);
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void EveryScrollInTheDataHoldsTheRarityOfItsName() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        List<ItemMetadata> scrolls = context.ItemMetadata.Where(item => item.Id >= 30000000 && item.Id < 32000000).AsEnumerable()
            .Where(item => item.Property.Tag is ItemTag.LockItemOptionWeapon or ItemTag.LockItemOptionArmor or ItemTag.LockItemOptionAccessory).ToList();
        Assert.That(scrolls, Has.Count.GreaterThanOrEqualTo(33)); // 33 in the base data, 35 with the pack

        foreach (ItemMetadata scroll in scrolls) {
            int rarity = scroll.Option?.ConstantId ?? 0;
            string name = scroll.Name ?? string.Empty;
            int named = name.StartsWith("Epic", StringComparison.Ordinal) ? 4
                : name.StartsWith("Legendary", StringComparison.Ordinal) ? 5
                : name.StartsWith("Ascendant", StringComparison.Ordinal) ? 6
                : rarity;
            Assert.That(rarity, Is.InRange(4, 6), $"scroll {scroll.Id}");
            Assert.That(rarity, Is.EqualTo(named), $"scroll {scroll.Id}");
        }
    }
}
