using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>Shops and the black market check their input before taking anything; added save columns get readable defaults.</summary>
[NonParallelizable]
public class EconomyValidationTest {
    private static ItemMetadata Potion(int level, long[] sellPrices, long[] customPrices) {
        var property = new ItemMetadataProperty(false, 0, 0, 0, 0, "", "", ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [],
            sellPrices, customPrices, 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, level, TransferType.Tradable, 0, true, false, false, false, false, false, false, 0, [], []);
        return new ItemMetadata(20000001, "potion", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, null, null);
    }

    [Test]
    public void SellPrice_ScalesWithQuantity_AndRefusesWhatCannotBePriced() {
        ItemMetadata potion = Potion(10, [5, 6, 7], []);
        var type = new ItemType(potion.Id);

        Assert.That(Core.Formulas.Shop.TrySellPrice(potion, type, 1, 99, out long price), Is.True);
        Assert.That(price, Is.EqualTo(5 * 99));

        // quantity <= 0 would otherwise reach Inventory.Remove, which sells the whole stack
        Assert.That(Core.Formulas.Shop.TrySellPrice(potion, type, 1, 0, out _), Is.False);
        Assert.That(Core.Formulas.Shop.TrySellPrice(potion, type, 1, -3, out _), Is.False);
        // rarity 0 and a rarity past the price table used to throw after the item was removed
        Assert.That(Core.Formulas.Shop.TrySellPrice(potion, type, 0, 1, out _), Is.False);
        Assert.That(Core.Formulas.Shop.TrySellPrice(potion, type, 4, 1, out _), Is.False);
        Assert.That(Core.Formulas.Shop.TrySellPrice(Potion(60, [], []), type, 7, 1, out _), Is.False);
        Assert.That(Core.Formulas.Shop.SellPrice(potion, type, 0), Is.Zero);
    }

    [Test]
    public void RefundToken_GivesBackWhatPayTokenTook() {
        long balance = 100;
        Assert.That(ShopManager.TryPayToken(ShopCurrencyType.Treva, 30, _ => balance, (_, value) => balance = value), Is.True);
        Assert.That(balance, Is.EqualTo(70));

        Assert.That(ShopManager.TryRefundToken(ShopCurrencyType.Treva, 30, _ => balance, (_, value) => balance = value), Is.True);
        Assert.That(balance, Is.EqualTo(100));
        Assert.That(ShopManager.TryRefundToken(ShopCurrencyType.Meso, 30, _ => balance, (_, value) => balance = value), Is.False, "not a token");
    }

    [Test]
    public void BlackMarketOffer_RejectsNonPositiveAndOverflowing() {
        Assert.That(BlackMarketManager.IsValidOffer(1000, 5), Is.True);
        Assert.That(BlackMarketManager.IsValidOffer(0, 5), Is.False);
        Assert.That(BlackMarketManager.IsValidOffer(-1000, 5), Is.False);
        Assert.That(BlackMarketManager.IsValidOffer(1000, 0), Is.False);
        Assert.That(BlackMarketManager.IsValidOffer(1000, -5), Is.False, "a negative quantity would give a negative fee");
        Assert.That(BlackMarketManager.IsValidOffer(long.MaxValue, 2), Is.False);
    }

    [Test]
    public void BlackMarketOffer_EnforcesPriceLimits() {
        Assert.That(BlackMarketManager.IsValidOffer(99, 1, 100, 10_000_000_000), Is.False);
        Assert.That(BlackMarketManager.IsValidOffer(100, 1, 100, 10_000_000_000), Is.True);
        Assert.That(BlackMarketManager.IsValidOffer(10_000_000_000, 1, 100, 10_000_000_000), Is.True);
        Assert.That(BlackMarketManager.IsValidOffer(10_000_000_001, 1, 100, 10_000_000_000), Is.False);
        Assert.That(BlackMarketManager.IsValidOffer(5, 1, 0, 0), Is.True);
    }

    [Test]
    public void SaveUpgrade_AddedColumnsGetValuesTheirReadersCanParse() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var context = new Ms2Context(options);
        context.Database.EnsureCreated();

        long accountId;
        using (var db = new GameStorage.Request(null!, new Ms2Context(options), NullLogger.Instance)) {
            accountId = db.CreateAccount(new Account { Username = "upgrader", MachineId = Guid.NewGuid() }, "password").Id;
        }

        // A save from before these columns existed: a JSON object, a JSON list, a Guid and a DateTime column.
        foreach (string column in new[] { "MarketLimits", "PremiumRewardsClaimed", "MachineId", "CreationTime" }) {
            context.Database.ExecuteSqlRaw($"ALTER TABLE \"account\" DROP COLUMN \"{column}\"");
        }
        Assert.That(DbProvider.UpgradeSqliteSchema(context), Does.Contain("column account.MarketLimits"));

        using var reader = new GameStorage.Request(null!, new Ms2Context(options), NullLogger.Instance);
        Account? loaded = reader.GetAccount(accountId);
        Assert.That(loaded, Is.Not.Null, "the upgraded row loads");
        Assert.That(loaded!.PremiumRewardsClaimed, Is.Empty);
        Assert.That(loaded.MachineId, Is.EqualTo(Guid.Empty));
    }
}
