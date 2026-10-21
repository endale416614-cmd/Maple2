using System;
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
/// PLAN_1.2.0 Phase E save compatibility (M5-01): a save made with the content pack, opened without it, loads and keeps
/// the pack items (their ids have no metadata in the Global data, so they are skipped, never rewritten or disowned).
/// </summary>
[NonParallelizable]
public class PackSaveCompatibilityTest {
    private const int GLOBAL_ITEM = 13150288;
    private const int PACK_ITEM = 11020411; // Exotic Red Opera Mask: in the content pack only

    private SqliteConnection? dataConnection;
    private MetadataContext? dataContext;
    private ItemMetadataStorage items = null!;

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

    [Test]
    public void SaveWithPackItems_OpenedWithoutThePack_LoadsAndKeepsThem() {
        Assume.That(dataContext, Is.Not.Null, "maple-data.db not found (set SQLITE_DATA_DIR)");
        Assume.That(items.TryGet(PACK_ITEM, out _), Is.False, "this data has the content pack; the test needs the Global data");
        Assert.That(items.TryGet(GLOBAL_ITEM, out ItemMetadata? metadata), Is.True);

        using var save = new SqliteConnection("Data Source=:memory:");
        save.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(save).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
        // A fresh request per step, as a restarted server sees the save.
        var storage = new GameStorage(options, items, null!, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);

        long owner, globalUid, packUid;
        using (GameStorage.Request db = storage.Context()) {
            owner = db.CreateAccount(new Account { Username = "packsave", MachineId = Guid.NewGuid() }, "password").Id;
            globalUid = db.CreateItem(owner, new Item(metadata!, 1))!.Uid;
            packUid = db.CreateItem(owner, new Item(metadata!, 1))!.Uid;
        }
        // The second item becomes what a session with the pack would have saved.
        using (SqliteCommand update = save.CreateCommand()) {
            update.CommandText = $"UPDATE item SET ItemId = {PACK_ITEM} WHERE Id = {packUid}";
            Assert.That(update.ExecuteNonQuery(), Is.EqualTo(1));
        }

        Item[] loaded;
        using (GameStorage.Request db = storage.Context()) {
            Item[] inventory = db.GetInventory(owner).Values.SelectMany(tab => tab).ToArray();
            Assert.That(inventory.Select(item => item.Uid), Is.EqualTo(new[] { globalUid }), "only the Global item loads");
            loaded = db.GetAllItems(owner).ToArray();
            Assert.That(loaded.Select(item => item.Uid), Is.EqualTo(new[] { globalUid }));
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.SaveItems(owner, loaded), Is.True);
        }
        using (GameStorage.Request db = storage.Context()) {
            db.DeleteUnownedItems(); // what the World server does at every start
        }

        using SqliteCommand read = save.CreateCommand();
        read.CommandText = $"SELECT OwnerId, ItemId FROM item WHERE Id = {packUid}";
        using SqliteDataReader row = read.ExecuteReader();
        Assert.That(row.Read(), Is.True, "the pack item is still in the save");
        Assert.That(row.GetInt64(0), Is.EqualTo(owner), "still owned");
        Assert.That(row.GetInt32(1), Is.EqualTo(PACK_ITEM));
    }
}
