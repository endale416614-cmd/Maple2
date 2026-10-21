using System;
using System.IO;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// M5-02: furniture and mail attachments whose item has no data (a session without the content pack) are kept, and a
/// cube placed on a spot such a hidden cube holds does not break the plot once the data is back.
/// </summary>
[NonParallelizable]
public class PackHousingMailTest {
    private const int MAP = 2000001; // an outdoor housing map of the Global data
    private const int FENNEL = 50100286; // Fennel Leaves Block
    private const int WILDFLOWER = 50100289; // Yellow Wildflower Block
    private const int NO_DATA = 59999999; // an item id the data does not have (a pack item without the pack)

    private SqliteConnection? dataConnection;
    private MetadataContext? dataContext;
    private ItemMetadataStorage items = null!;
    private MapMetadataStorage maps = null!;
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
        maps = new MapMetadataStorage(dataContext);
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
        storage = new GameStorage(options, items, maps, null!, null!, null!, null!, NullLogger<GameStorage>.Instance, null!);
    }

    [TearDown]
    public void CloseSave() => save?.Dispose();

    private int Sql(string sql) {
        using SqliteCommand command = save.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    private long Count(string sql) {
        using SqliteCommand command = save.CreateCommand();
        command.CommandText = sql;
        return (long) command.ExecuteScalar()!;
    }

    // An owned plot of MAP: the account owns plot 1.
    private (long Owner, PlotInfo Plot) OwnedPlot() {
        Assert.That(maps.TryGetUgc(MAP, out UgcMapMetadata? ugc), Is.True);
        using GameStorage.Request db = storage.Context();
        long owner = db.CreateAccount(new Account { Username = "homeowner", MachineId = Guid.NewGuid() }, "password").Id;
        Assert.That(db.InitUgcMap(new[] { ugc! }), Is.True);
        Plot plot = db.LoadPlotsForMap(MAP).First(p => p.Number == 1);
        PlotInfo? bought = db.BuyPlot("homeowner", owner, plot, TimeSpan.FromDays(30));
        Assert.That(bought, Is.Not.Null);
        return (owner, bought!);
    }

    private PlotCube Cube(PlotInfo plot, int itemId, Vector3B position) {
        Assert.That(items.TryGet(itemId, out ItemMetadata? metadata), Is.True);
        using GameStorage.Request db = storage.Context();
        PlotCube? cube = db.CreateCube(plot, new PlotCube(metadata!) { Position = position, Type = PlotCube.CubeType.Default });
        Assert.That(cube, Is.Not.Null);
        return cube!;
    }

    [Test]
    public void SavingAPlot_KeepsCubesWhoseItemHasNoData() {
        (long owner, PlotInfo plot) = OwnedPlot();
        Cube(plot, FENNEL, new Vector3B(1, 1, 1));
        PlotCube hidden = Cube(plot, FENNEL, new Vector3B(2, 2, 2));
        PlotCube removed = Cube(plot, WILDFLOWER, new Vector3B(4, 4, 4));
        Assert.That(Sql($"UPDATE \"ugcmap-cube\" SET ItemId = {NO_DATA} WHERE Id = {hidden.Id}"), Is.EqualTo(1));

        using (GameStorage.Request db = storage.Context()) {
            Plot loaded = db.LoadPlotsForMap(MAP, owner).Single(p => p.Id == plot.Id);
            Assert.That(loaded.Cubes.Count, Is.EqualTo(2), "the cube without data is not loaded");
            // What leaving the home does, after the player picked up the wildflower block.
            Assert.That(db.SaveCubes(loaded, loaded.Cubes.Values.Where(cube => cube.Id != removed.Id).ToList()), Is.Not.Null);
        }

        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {plot.Id}"), Is.EqualTo(2));
        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE Id = {removed.Id}"), Is.EqualTo(0), "a removed cube still goes");
        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE Id = {hidden.Id} AND ItemId = {NO_DATA}"), Is.EqualTo(1),
            "the cube waits for its data");
    }

    [Test]
    public void TwoCubesOnOneSpot_TheNewerGoesToFurnishingStorage() {
        (long owner, PlotInfo plot) = OwnedPlot();
        var spot = new Vector3B(3, 3, 3);
        PlotCube older = Cube(plot, FENNEL, spot);
        PlotCube newer = Cube(plot, WILDFLOWER, spot);

        using (GameStorage.Request db = storage.Context()) {
            Plot loaded = db.LoadPlotsForMap(MAP, owner).Single(p => p.Id == plot.Id); // used to throw: same key added twice
            Assert.That(loaded.Cubes[spot].Id, Is.EqualTo(older.Id), "the older cube keeps the spot");
        }

        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE Id = {newer.Id}"), Is.EqualTo(0));
        using (GameStorage.Request db = storage.Context()) {
            Item[] furnishing = db.GetItemGroups(owner, ItemGroup.Furnishing).TryGetValue(ItemGroup.Furnishing, out var stored) ? stored.ToArray() : [];
            Assert.That(furnishing.Select(item => item.Id), Is.EqualTo(new[] { WILDFLOWER }), "the newer cube is stored, not lost");
        }
        using (GameStorage.Request db = storage.Context()) {
            db.LoadPlotsForMap(MAP, owner).Single(p => p.Id == plot.Id); // a second load stores nothing again
        }
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE ItemId = {WILDFLOWER}"), Is.EqualTo(1));
    }

    [Test]
    public void TwoCubesOnOneSpot_OnAnUnownedPlot_NothingGoesToOwnerZero() {
        (long _, PlotInfo plot) = OwnedPlot();
        var spot = new Vector3B(3, 3, 3);
        Cube(plot, FENNEL, spot);
        PlotCube newer = Cube(plot, WILDFLOWER, spot);
        Assert.That(Sql($"UPDATE ugcmap SET OwnerId = 0 WHERE Id = {plot.Id}"), Is.EqualTo(1));

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.LoadPlotsForMap(MAP).Single(p => p.Id == plot.Id).Cubes.Count, Is.EqualTo(1), "the plot still loads");
        }
        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE Id = {newer.Id}"), Is.EqualTo(1), "left for the next buyer's clean-up");
        Assert.That(Count("SELECT COUNT(*) FROM item"), Is.EqualTo(0), "no item for owner 0 (the World start deletes those)");
    }

    [Test]
    public void ExpiringAPlot_StoresEveryCubeForTheOwner() {
        (long owner, PlotInfo plot) = OwnedPlot();
        Assert.That(items.TryGet(FENNEL, out ItemMetadata? fennel), Is.True);
        Cube(plot, FENNEL, new Vector3B(1, 1, 1));
        Cube(plot, WILDFLOWER, new Vector3B(2, 2, 2));
        Cube(plot, FENNEL, new Vector3B(5, 5, 5));
        Cube(plot, WILDFLOWER, new Vector3B(5, 5, 5)); // on a taken spot: stored while the plot loads
        long kept;
        using (GameStorage.Request db = storage.Context()) {
            kept = db.CreateItem(owner, new Item(fennel!) { Group = ItemGroup.Furnishing })!.Uid; // one fennel block in storage
        }

        using (GameStorage.Request db = storage.Context()) {
            db.ExpireOutdoorPlot(plot);
        }

        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {plot.Id}"), Is.EqualTo(0));
        Assert.That(Count($"SELECT OwnerId FROM ugcmap WHERE Id = {plot.Id}"), Is.EqualTo(0), "the plot is pending");
        Assert.That(Count("SELECT COUNT(*) FROM item WHERE OwnerId = 0"), Is.EqualTo(0), "nothing for owner 0");
        Assert.That(Count($"SELECT Amount FROM item WHERE Id = {kept}"), Is.EqualTo(3), "the two fennel blocks stack onto the stored one");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {owner} AND ItemId = {WILDFLOWER}"), Is.EqualTo(2));
    }

    [Test]
    public void ExpiringAPlot_WithoutThePack_KeepsTheCubesWithoutData() {
        (long owner, PlotInfo plot) = OwnedPlot();
        Cube(plot, FENNEL, new Vector3B(1, 1, 1));
        PlotCube hidden = Cube(plot, FENNEL, new Vector3B(2, 2, 2));
        Assert.That(Sql($"UPDATE \"ugcmap-cube\" SET ItemId = {NO_DATA} WHERE Id = {hidden.Id}"), Is.EqualTo(1));

        using (GameStorage.Request db = storage.Context()) {
            db.ExpireOutdoorPlot(plot);
        }

        Assert.That(Count($"SELECT COUNT(*) FROM \"ugcmap-cube\" WHERE UgcMapId = {plot.Id}"), Is.EqualTo(0), "the next buyer finds no cube");
        Assert.That(Count($"SELECT OwnerId FROM ugcmap WHERE Id = {plot.Id}"), Is.EqualTo(0), "the plot is pending");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {owner} AND ItemId = {NO_DATA} AND \"Group\" = {(int) ItemGroup.Furnishing}"),
            Is.EqualTo(1), "the cube without data waits in the owner's furnishing storage");
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {owner} AND ItemId = {FENNEL}"), Is.EqualTo(1));

        Assert.That(Sql($"UPDATE item SET ItemId = {WILDFLOWER} WHERE ItemId = {NO_DATA}"), Is.EqualTo(1)); // the data is back
        using (GameStorage.Request db = storage.Context()) {
            Item[] furnishing = db.GetItemGroups(owner, ItemGroup.Furnishing).TryGetValue(ItemGroup.Furnishing, out var stored) ? stored.ToArray() : [];
            Assert.That(furnishing.Select(item => item.Id).OrderBy(id => id), Is.EqualTo(new[] { FENNEL, WILDFLOWER }), "the row loads as an item");
        }
    }

    [Test]
    public void DeletingMail_WithAnAttachmentWithoutData_KeepsTheMail() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        Assert.That(items.TryGet(FENNEL, out ItemMetadata? metadata), Is.True);
        long receiver, mailId, loaded, hidden;
        using (GameStorage.Request db = storage.Context()) {
            receiver = db.CreateAccount(new Account { Username = "mailreader", MachineId = Guid.NewGuid() }, "password").Id;
            mailId = db.CreateMail(new Mail { ReceiverId = receiver })!.Id;
            loaded = db.CreateItem(mailId, new Item(metadata!))!.Uid;
            hidden = db.CreateItem(mailId, new Item(metadata!))!.Uid;
        }
        Assert.That(Sql($"UPDATE item SET ItemId = {NO_DATA} WHERE Id = {hidden}"), Is.EqualTo(1));

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteMail(mailId, receiver, new[] { loaded }), Is.False, "an attachment could not be loaded");
        }
        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.GetMail(mailId, receiver), Is.Not.Null, "the mail stays");
        }
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {mailId}"), Is.EqualTo(2), "both attachments stay");

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteMail(mailId, receiver, new[] { loaded, hidden }), Is.True, "with every attachment loaded it goes");
        }
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE OwnerId = {mailId}"), Is.EqualTo(0));
    }

    [Test]
    public void DeletingMail_AfterCollectingAnAttachment_StillWorks() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        Assert.That(items.TryGet(FENNEL, out ItemMetadata? metadata), Is.True);
        long receiver, mailId, attached, collected;
        using (GameStorage.Request db = storage.Context()) {
            receiver = db.CreateAccount(new Account { Username = "collector", MachineId = Guid.NewGuid() }, "password").Id;
            mailId = db.CreateMail(new Mail { ReceiverId = receiver })!.Id;
            attached = db.CreateItem(mailId, new Item(metadata!))!.Uid;
            collected = db.CreateItem(mailId, new Item(metadata!))!.Uid; // in the inventory, saved under the character later
        }

        using (GameStorage.Request db = storage.Context()) {
            Assert.That(db.DeleteMail(mailId, receiver, new[] { attached }), Is.True, "a collected attachment has data: no hold");
        }
        Assert.That(Count($"SELECT COUNT(*) FROM item WHERE Id = {collected} AND OwnerId = {mailId}"), Is.EqualTo(1),
            "the collected item waits for the inventory save");
    }
}
