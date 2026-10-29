using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>IT-03 (BT-95): offline, a data end date that has passed gives no expiry, when an item is made and when it is loaded.</summary>
[TestFixture]
public class ItemExpiryTest {
    private static ItemMetadata Metadata(ItemMetadataLife life) {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var customize = new ItemMetadataCustomize(0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(1000, "1000", [], "", [], life, property, customize, limit, null, null, [], null, null, null, null);
    }

    [Test]
    public void PastDataEndDate_GivesNoExpiry() {
        // 1584594000 is Meta Badge: Bronze Attack Mark (70150014)'s own date.
        var item = new Item(Metadata(new ItemMetadataLife(0, 1584594000)));
        Assert.That(item.ExpiryTime, Is.EqualTo(0));
        Assert.That(item.IsExpired(), Is.False);
    }

    [Test]
    public void FutureDataEndDate_IsKept() {
        long end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86400;
        var item = new Item(Metadata(new ItemMetadataLife(0, end)));
        Assert.That(item.ExpiryTime, Is.EqualTo(end));
    }

    [Test]
    public void Duration_CountsFromNow() {
        long before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var item = new Item(Metadata(new ItemMetadataLife(3600, 0)));
        long after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.That(item.ExpiryTime, Is.InRange(before + 3600, after + 3600));
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void SavedItems_DropOnlyAPastDataEndDate() {
        string? path = DataDb.Find();
        Assume.That(path, Is.Not.Null, "maple-data.db not found (set MAPLE_DATA_DB)");

        using var dataConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        dataConnection.Open();
        using var dataContext = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(dataConnection).Options);
        dataContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var items = new ItemMetadataStorage(dataContext);
        Assert.That(items.TryGet(20000665, out ItemMetadata? elixir), Is.True); // life@expirationPeriod, past
        Assert.That(items.TryGet(30000436, out ItemMetadata? key), Is.True); // Lennon's Key, 60 s duration

        using var save = new SqliteConnection("Data Source=:memory:");
        save.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(save).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        // A fresh request per step, as a restarted server sees the save.
        var storage = new GameStorage(options, items, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        // Step 1: fresh inserts. The forced elixir value is what a Release build gave the item before this fix.
        long owner, elixirUid, keyUid;
        using (GameStorage.Request db = storage.Context()) {
            owner = db.CreateAccount(new Account { Username = "itemexpiry", MachineId = Guid.NewGuid() }, "password").Id;
            Item createdElixir = db.CreateItem(owner, new Item(elixir!) { ExpiryTime = 1514757599 })!;
            Assert.That(createdElixir.ExpiryTime, Is.EqualTo(0));
            elixirUid = createdElixir.Uid;
            Item createdKey = db.CreateItem(owner, new Item(key!) { ExpiryTime = 1577836800 })!;
            Assert.That(createdKey.ExpiryTime, Is.EqualTo(1577836800L));
            Assert.That(createdKey.IsExpired(), Is.True);
            keyUid = createdKey.Uid;
        }

        // Step 2: the elixir row as the rc-1.2.2 save holds it. The key expired before it was inserted.
        using (SqliteCommand update = save.CreateCommand()) {
            update.CommandText = $"UPDATE item SET ExpiryTime = '2017-12-31 23:59:59' WHERE Id = {elixirUid}";
            Assert.That(update.ExecuteNonQuery(), Is.EqualTo(1));
        }
        using (SqliteCommand count = save.CreateCommand()) {
            count.CommandText = $"SELECT COUNT(*) FROM item WHERE Id = {keyUid} AND CreationTime > ExpiryTime";
            Assert.That(count.ExecuteScalar(), Is.EqualTo(1L));
        }

        // Step 3: reload.
        using (GameStorage.Request db = storage.Context()) {
            Item[] loaded = db.GetInventory(owner).Values.SelectMany(tab => tab).ToArray();
            Item loadedElixir = loaded.Single(item => item.Uid == elixirUid);
            Assert.That(loadedElixir.ExpiryTime, Is.EqualTo(0));
            Assert.That(loadedElixir.IsExpired(), Is.False);
            // A duration item that ran out stays expired.
            Item loadedKey = loaded.Single(item => item.Uid == keyUid);
            Assert.That(loadedKey.ExpiryTime, Is.EqualTo(1577836800L));
            Assert.That(loadedKey.IsExpired(), Is.True);
        }
    }
}
