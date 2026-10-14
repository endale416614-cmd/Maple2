using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>Mail attachments go with their mail: a reused mail id must never bring back items of a deleted mail.</summary>
[NonParallelizable]
public class MailAttachmentTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;
    private long accountId;

    [SetUp]
    public void SetUp() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }

        using GameStorage.Request db = NewRequest();
        accountId = db.CreateAccount(new Account { Username = "mailer", MachineId = Guid.NewGuid() }, "password").Id;
    }

    [TearDown]
    public void TearDown() {
        connection?.Dispose();
    }

    private GameStorage.Request NewRequest() {
        var context = new Ms2Context(options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return new GameStorage.Request(null!, context, NullLogger.Instance);
    }

    // Raw rows: the storage item methods need the game's item metadata.
    private long InsertItem(long ownerId) {
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw(
            "INSERT INTO item (OwnerId, ItemId, Rarity, Slot, \"Group\", Amount, ExpiryTime, TimeChangedOption, RemainUses, IsLocked, " +
            "UnlockTime, GlamorForges, GachaDismantleId, Appearance, CreationTime, LastModified) " +
            "VALUES ({0}, 13100051, 1, -1, 0, 1, '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, '{{}}', '2026-01-01 00:00:00', '2026-01-01 00:00:00')",
            ownerId);
        return context.Database.SqlQueryRaw<long>("SELECT MAX(Id) AS Value FROM item").AsEnumerable().First();
    }

    private long? OwnerOf(long itemUid) {
        using var context = new Ms2Context(options);
        return context.Database.SqlQueryRaw<long>("SELECT OwnerId AS Value FROM item WHERE Id = {0}", itemUid).AsEnumerable()
            .Select(owner => (long?) owner).FirstOrDefault();
    }

    [Test]
    public void DeleteMail_DeletesItsAttachments_ButNotCollectedItems() {
        long mailId;
        using (GameStorage.Request db = NewRequest()) {
            mailId = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id;
        }
        long attached = InsertItem(mailId);
        long collected = InsertItem(mailId); // taken into the inventory, stored under the mail id until the next save
        long inventory = InsertItem(accountId);

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.DeleteMail(mailId, accountId, [attached]), Is.True);
        }

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetMail(mailId, accountId), Is.Null, "mail deleted");
        }
        Assert.That(OwnerOf(attached), Is.Null, "attachment deleted with the mail");
        Assert.That(OwnerOf(collected), Is.EqualTo(mailId), "collected item kept for the inventory save");
        Assert.That(OwnerOf(inventory), Is.EqualTo(accountId), "other items untouched");
    }

    [Test]
    public void DeleteMail_UnknownMail_DeletesNothing() {
        long item = InsertItem(GameStorage.FirstMailId);
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.DeleteMail(GameStorage.FirstMailId, accountId, [item]), Is.False);
        }
        Assert.That(OwnerOf(item), Is.EqualTo(GameStorage.FirstMailId));
    }

    [Test]
    public void CreateMail_DoesNotReuseAnIdThatStillOwnsItems() {
        // The reported case: every mail deleted, yet items still stored under mail 100000.
        InsertItem(GameStorage.FirstMailId);
        InsertItem(GameStorage.FirstMailId + 87);

        long id;
        using (GameStorage.Request db = NewRequest()) {
            id = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id;
        }
        Assert.That(id, Is.EqualTo(GameStorage.FirstMailId + 88));
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetMail(id, accountId)!.Items, Is.Empty, "no ghost attachments");
        }
    }

    [Test]
    public void DeleteOrphanedMailItems_RemovesOnlyItemsOfMissingMail() {
        long liveMail;
        using (GameStorage.Request db = NewRequest()) {
            liveMail = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id;
        }
        long live = InsertItem(liveMail);
        long orphanFirst = InsertItem(GameStorage.FirstMailId + 50);
        long orphanLast = InsertItem(GameStorage.MailIdLimit - 1);
        long account = InsertItem(accountId);
        long pet = InsertItem(2000000000001); // items inside a pet are owned by the pet's item uid
        long listing = InsertItem(110000000000); // black market listing

        using (GameStorage.Request db = NewRequest()) {
            db.DeleteOrphanedMailItems();
        }

        Assert.That(OwnerOf(orphanFirst), Is.Null);
        Assert.That(OwnerOf(orphanLast), Is.Null);
        Assert.That(OwnerOf(live), Is.EqualTo(liveMail), "attachment of existing mail kept");
        Assert.That(OwnerOf(account), Is.EqualTo(accountId));
        Assert.That(OwnerOf(pet), Is.EqualTo(2000000000001));
        Assert.That(OwnerOf(listing), Is.EqualTo(110000000000));

        using (GameStorage.Request db = NewRequest()) {
            db.DeleteOrphanedMailItems(); // nothing left to do
        }
        Assert.That(OwnerOf(live), Is.EqualTo(liveMail));
    }
}
