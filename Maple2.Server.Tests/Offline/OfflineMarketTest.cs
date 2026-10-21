using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// R12-22: the offline Black Market's synthetic listings replace the previous set and touch no other listing or item;
/// the config loads only in Adapted mode.
/// </summary>
[NonParallelizable]
public class OfflineMarketTest {
    private const int FENNEL = 50100286; // Fennel Leaves Block
    private const int WILDFLOWER = 50100289; // Yellow Wildflower Block

    private SqliteConnection? dataConnection;
    private MetadataContext? dataContext;
    private ItemMetadataStorage items = null!;
    private SqliteConnection save = null!;
    private GameStorage storage = null!;

    // Same lookup as GearRepairTest.
    private static string? FindDataDb() {
        foreach (string? dir in new[] { Environment.GetEnvironmentVariable("SQLITE_DATA_DIR"), Environment.GetEnvironmentVariable("SQLITE_DIR") }) {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "maple-data.db"))) {
                return Path.Combine(dir, "maple-data.db");
            }
        }
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "SaveData", "maple-data.db");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    [OneTimeSetUp]
    public void LoadGameData() {
        string? path = FindDataDb();
        if (path == null) {
            return;
        }

        dataConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        dataConnection.Open();
        dataContext = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(dataConnection).Options);
        dataContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        items = new ItemMetadataStorage(dataContext);
    }

    [OneTimeTearDown]
    public void Unload() {
        dataContext?.Dispose();
        dataConnection?.Dispose();
    }

    [SetUp]
    public void NewSave() {
        Assume.That(dataContext, Is.Not.Null, "maple-data.db not found (set SQLITE_DATA_DIR)");
        save = new SqliteConnection("Data Source=:memory:");
        save.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(save).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        storage = new GameStorage(options, items, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    [TearDown]
    public void CloseSave() => save?.Dispose();

    private List<long> Longs(string sql) {
        using SqliteCommand command = save.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<long>();
        while (reader.Read()) {
            values.Add(reader.GetInt64(0));
        }
        return values;
    }

    private long Count(string sql) => Longs(sql).Single();

    private Item NewItem(int itemId) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? metadata), Is.True);
        return new Item(metadata!);
    }

    [Test]
    public void ReplacingTwice_KeepsOnlyTheNewSet_AndNothingElse() {
        long expiry = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
        long playerListing, playerItem;
        Item stored;
        using (GameStorage.Request db = storage.Context()) {
            stored = db.CreateItem(7, NewItem(FENNEL))!; // a player's item, listed below (as the game does: another request)
        }
        using (GameStorage.Request db = storage.Context()) {
            BlackMarketListing? listing = db.CreateBlackMarketingListing(new BlackMarketListing(stored) {
                CharacterId = 5, AccountId = 4, Price = 500, Quantity = 1, ExpiryTime = expiry,
            });
            Assert.That(listing, Is.Not.Null);
            playerListing = listing!.Id;
            playerItem = stored.Uid;
            Assert.That(db.ReplaceSyntheticListings([(NewItem(FENNEL), 1000L), (NewItem(WILDFLOWER), 2000L)], expiry), Is.EqualTo(2));
        }

        List<long> first = Longs($"SELECT Id FROM \"black-market-listing\" WHERE CharacterId = {OfflineMarket.SellerId} ORDER BY Id");
        Assert.That(first, Has.Count.EqualTo(2));
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId IN ({string.Join(",", first)})"), Is.EqualTo(2), "each listing owns its item");
        long bought = Longs($"SELECT Id FROM item WHERE OwnerId = {first[0]}").Single();
        using (SqliteCommand command = save.CreateCommand()) { // a purchase moved this one into a mail
            command.CommandText = $"UPDATE item SET OwnerId = 123456 WHERE Id = {bought}";
            command.ExecuteNonQuery();
        }

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.ReplaceSyntheticListings([(NewItem(WILDFLOWER), 3000L)], expiry), Is.EqualTo(1));
        }

        List<long> second = Longs($"SELECT Id FROM \"black-market-listing\" WHERE CharacterId = {OfflineMarket.SellerId}");
        Assert.That(second, Has.Count.EqualTo(1));
        Assert.That(second.Intersect(first), Is.Empty, "the first set is gone");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId IN ({string.Join(",", first)})"), Is.EqualTo(0), "with the items it still owned");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE Id = {bought} AND OwnerId = 123456"), Is.EqualTo(1), "a bought item stays");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {second[0]}"), Is.EqualTo(1));
        Assert.That(Count($"SELECT COUNT(*) FROM \"black-market-listing\" WHERE Id = {playerListing}"), Is.EqualTo(1), "a player's listing stays");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE Id = {playerItem} AND OwnerId = {playerListing}"), Is.EqualTo(1), "with its item");
    }

    [Test]
    public void ReplacingWithNothing_RemovesTheSet() {
        long expiry = DateTimeOffset.UtcNow.AddDays(2).ToUnixTimeSeconds();
        using (GameStorage.Request db = storage.Context()) {
            db.ReplaceSyntheticListings([(NewItem(FENNEL), 1000L)], expiry);
            Assert.That(db.ReplaceSyntheticListings([], expiry), Is.EqualTo(0)); // Legacy, or no offline-market.json
        }
        Assert.That(Count("SELECT COUNT(*) FROM \"black-market-listing\""), Is.EqualTo(0));
        Assert.That(Count("SELECT COUNT(*) FROM item"), Is.EqualTo(0));
    }

    [Test]
    public void Load_IsNullUnderLegacy_AndReadsTheFileUnderAdapted() {
        string path = Path.Combine(Path.GetTempPath(), $"offline-market-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"count\": 2, \"priceMultiplier\": 1.5, // comment\n \"items\": [ { \"id\": 50100286 }, { \"id\": 50100289, \"rarity\": 2, \"price\": 900 }, ] }");
        string?[] saved = new[] { "OFFLINE_PROFILE", "OFFLINE_MARKET", "OFFLINE_MARKET_FILE" }.Select(Environment.GetEnvironmentVariable).ToArray();
        try {
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", null);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", path);
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", "Legacy");
            Assert.That(OfflineMarket.Load(), Is.Null, "never under Legacy");

            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", "Adapted");
            OfflineMarket.Config? config = OfflineMarket.Load();
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.Count, Is.EqualTo(2));
            Assert.That(config.PriceMultiplier, Is.EqualTo(1.5));
            Assert.That(config.Items.Select(offer => (offer.Id, offer.Rarity, offer.Amount, offer.Price)),
                Is.EqualTo(new[] { (50100286, 0, 1, 0L), (50100289, 2, 1, 900L) }));
        } finally {
            Environment.SetEnvironmentVariable("OFFLINE_PROFILE", saved[0]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET", saved[1]);
            Environment.SetEnvironmentVariable("OFFLINE_MARKET_FILE", saved[2]);
            File.Delete(path);
        }
    }
}
