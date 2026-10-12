using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Server.Game.Util;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>Save safety: no implicit commit, all-or-nothing snapshots, atomic mail claim, launcher save status file.</summary>
[NonParallelizable]
public class SaveSafetyTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;
    private long accountId;
    private string? statusFile;

    [SetUp]
    public void SetUp() {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }

        using GameStorage.Request db = NewRequest();
        Account account = db.CreateAccount(new Account { Username = "saver", MachineId = Guid.NewGuid() }, "password");
        accountId = account.Id;
    }

    [TearDown]
    public void TearDown() {
        connection.Dispose();
        Environment.SetEnvironmentVariable("OFFLINE_SAVE_STATUS_FILE", null);
        if (statusFile != null && Directory.Exists(Path.GetDirectoryName(statusFile)!)) {
            Directory.Delete(Path.GetDirectoryName(statusFile)!, recursive: true);
        }
    }

    // A fresh context on the same connection: what a restarted server would see.
    private GameStorage.Request NewRequest() {
        var context = new Ms2Context(options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return new GameStorage.Request(null!, context, NullLogger.Instance);
    }

    private long StorageMesos() {
        using GameStorage.Request db = NewRequest();
        return db.GetStorageInfo(accountId).Mesos;
    }

    [Test]
    public void CreateMail_OnSqlite_AssignsDistinctIds() {
        // SQLite cannot auto-number Id inside the (ReceiverId, Id) key; mail used to fail with "NOT NULL constraint failed: mail.Id".
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        long first, second;
        using (GameStorage.Request db = NewRequest()) {
            first = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })?.Id ?? 0;
            second = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 20 })?.Id ?? 0;
        }

        Assert.That(first, Is.GreaterThanOrEqualTo(100000));
        Assert.That(second, Is.GreaterThan(first));
    }

    [Test]
    public void CreateMail_WithoutExpiry_GetsDefaultExpiry() {
        // Mail with expiry 0 counts as expired in the client: the mail and its items were invisible.
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        long id;
        using (GameStorage.Request db = NewRequest()) {
            id = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id;
        }

        long expected = DateTimeOffset.UtcNow.AddDays(GameStorage.MailExpiryDays).ToUnixTimeSeconds();
        using GameStorage.Request read = NewRequest();
        Assert.That(read.GetMail(id, accountId)!.ExpiryTime, Is.EqualTo(expected).Within(120));
    }

    [Test]
    public void GetAllMail_RepairsStoredMailWithoutExpiry() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        long id;
        using (GameStorage.Request db = NewRequest()) {
            id = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id;
        }
        using (var context = new Ms2Context(options)) {
            // As written before the fix: expiry 0, stored as the epoch in local time.
            context.Database.ExecuteSqlRaw("UPDATE `mail` SET ExpiryTime = {0}", DateTimeOffset.FromUnixTimeSeconds(0).LocalDateTime);
        }

        using GameStorage.Request db2 = NewRequest();
        Mail mail = db2.GetAllMail(accountId).Single(m => m.Id == id);
        Assert.That(mail.ExpiryTime, Is.GreaterThan(DateTimeOffset.UtcNow.AddDays(GameStorage.MailExpiryDays - 1).ToUnixTimeSeconds()));
        Assert.That(db2.GetMail(id, accountId)!.ExpiryTime, Is.EqualTo(mail.ExpiryTime), "the repair is stored");
    }

    [Test]
    public void MailNeverExpires_KeepsMailAndRevivesExpiredMail() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        long id;
        using (GameStorage.Request db = NewRequest()) {
            id = db.CreateMail(new Mail { ReceiverId = accountId, Meso = 10 })!.Id; // normal 30-day expiry
        }
        using (var context = new Ms2Context(options)) {
            // Already expired yesterday.
            context.Database.ExecuteSqlRaw("UPDATE `mail` SET ExpiryTime = {0}", DateTime.Now.AddDays(-1));
        }

        GameStorage.MailNeverExpires = true;
        try {
            long farAhead = DateTimeOffset.UtcNow.AddDays(GameStorage.NeverExpiresDays - 1).ToUnixTimeSeconds();
            using GameStorage.Request db = NewRequest();
            Assert.That(db.GetAllMail(accountId).Single(m => m.Id == id).ExpiryTime, Is.GreaterThan(farAhead), "expired mail comes back");
            long created = db.CreateMail(new Mail(30) { ReceiverId = accountId, Meso = 20 })!.Id;
            Assert.That(db.GetMail(created, accountId)!.ExpiryTime, Is.GreaterThan(farAhead), "a sender's own expiry is extended");
            Assert.That(farAhead + 2L * 86400, Is.LessThan(int.MaxValue), "stays inside 32-bit time");
        } finally {
            GameStorage.MailNeverExpires = false;
        }
    }

    [Test]
    public void Dispose_WithoutCommit_AfterException_RollsBack() {
        try {
            using GameStorage.Request db = NewRequest();
            db.BeginTransaction();
            Assert.That(db.SaveStorageInfo(accountId, 500, 1), Is.True);
            throw new InvalidOperationException("injected failure after the write");
        } catch (InvalidOperationException) {
            // The request was disposed while unwinding; the abandoned transaction must not have been committed.
        }

        Assert.That(StorageMesos(), Is.EqualTo(0), "an uncommitted transaction must not be persisted");
    }

    [Test]
    public void Dispose_WithoutCommit_DoesNotCommitImplicitly() {
        using (GameStorage.Request db = NewRequest()) {
            db.BeginTransaction();
            Assert.That(db.SaveStorageInfo(accountId, 500, 1), Is.True);
        }
        Assert.That(StorageMesos(), Is.EqualTo(0));

        using (GameStorage.Request db = NewRequest()) {
            db.BeginTransaction();
            Assert.That(db.SaveStorageInfo(accountId, 500, 1), Is.True);
            Assert.That(db.Commit(), Is.True);
        }
        Assert.That(StorageMesos(), Is.EqualTo(500), "an explicit Commit persists");
    }

    [Test]
    public void Save_FailureBetweenComponents_PersistsNothing_AndLaterSaveWorks() {
        Mail mail = CreateMail(meso: 700);
        var calls = new List<string>();

        using (GameStorage.Request db = NewRequest()) {
            bool saved = db.SaveAtomic([
                ("storage", request => { calls.Add("storage"); request.Ensure(request.SaveStorageInfo(accountId, 900, 2), "storage"); }),
                ("injected", _ => throw new InvalidOperationException("injected failure between components")),
                ("mail", request => { calls.Add("mail"); request.UpdateMail(mail); }),
            ], out string? failed, out Exception? error);

            Assert.That(saved, Is.False);
            Assert.That(failed, Is.EqualTo("injected"));
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(calls, Is.EqualTo(new[] { "storage" }), "components after the failure never run");
        }

        Assert.That(StorageMesos(), Is.EqualTo(0), "the component that ran before the failure was rolled back");

        using (GameStorage.Request db = NewRequest()) {
            bool saved = db.SaveAtomic([
                ("storage", request => request.Ensure(request.SaveStorageInfo(accountId, 900, 2), "storage")),
            ], out string? failed, out _);
            Assert.That(saved, Is.True, failed);
        }
        Assert.That(StorageMesos(), Is.EqualTo(900), "the next snapshot is saved normally");
    }

    [Test]
    public void Save_ComponentThatIgnoresAFailedWrite_StillFailsTheSnapshot() {
        using (GameStorage.Request db = NewRequest()) {
            bool saved = db.SaveAtomic([
                ("storage", request => request.SaveStorageInfo(accountId, 900, 2)),
                // The result is ignored on purpose: the account does not exist, so the write breaks a foreign key.
                ("orphan", request => request.SaveStorageInfo(accountId + 1000, 5, 1)),
            ], out string? failed, out _);

            Assert.That(saved, Is.False);
            Assert.That(failed, Is.EqualTo("orphan"));
        }

        Assert.That(StorageMesos(), Is.EqualTo(0));
    }

    [Test]
    public void MailClaim_FailureAfterCredit_IsAllOrNothing_AndNeverDoubleCredits() {
        Mail mail = CreateMail(meso: 1000);
        int credits = 0;

        // Mirrors the real claim: the credit and the collect mark are two components of one snapshot.
        bool Claim(bool failAfterCredit) {
            Mail? current;
            using (GameStorage.Request read = NewRequest()) {
                current = read.GetMail(mail.Id, mail.ReceiverId);
            }
            Assert.That(current, Is.Not.Null);
            if (current!.MesoCollected()) {
                return false; // already claimed: nothing to credit again
            }

            long credited = StorageMesos() + current.Meso;
            current.MesoCollectTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using GameStorage.Request db = NewRequest();
            bool saved = db.SaveAtomic([
                ("credit", request => request.Ensure(request.SaveStorageInfo(accountId, credited, 1), "credit")),
                ("inject", _ => { if (failAfterCredit) throw new InvalidOperationException("injected failure after the credit"); }),
                ("mail", request => request.Ensure(request.UpdateMail(current) != null, "mail")),
            ], out _, out _);
            if (saved) {
                credits++;
            }
            return saved;
        }

        // First attempt fails after the credit step; the "restart" (new contexts) must see neither half.
        Assert.That(Claim(failAfterCredit: true), Is.False);
        Assert.That(StorageMesos(), Is.EqualTo(0), "no credit without the collect mark");
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetMail(mail.Id, mail.ReceiverId)!.MesoCollected(), Is.False, "no collect mark without the credit");
        }

        // Retry succeeds: both halves are durable.
        Assert.That(Claim(failAfterCredit: false), Is.True);
        Assert.That(StorageMesos(), Is.EqualTo(1000));
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetMail(mail.Id, mail.ReceiverId)!.MesoCollected(), Is.True);
        }

        // Repeating the claim (or restarting and claiming again) never credits twice.
        Assert.That(Claim(failAfterCredit: false), Is.False);
        Assert.That(Claim(failAfterCredit: false), Is.False);
        Assert.That(StorageMesos(), Is.EqualTo(1000));
        Assert.That(credits, Is.EqualTo(1));
    }

    [Test]
    public void SaveStatusFile_IsWrittenAtomicallyAsJson_AndSkippedWithoutTheVariable() {
        statusFile = Path.Combine(Path.GetTempPath(), $"offline-save-status-{Guid.NewGuid():N}", "save-status.json");

        Assert.That(OfflineSaveStatus.Write(11, 22, OfflineSaveStatus.Committed, null), Is.True);
        Assert.That(File.Exists(statusFile), Is.False, "variable not set: nothing is written");

        Environment.SetEnvironmentVariable("OFFLINE_SAVE_STATUS_FILE", statusFile);
        Assert.That(OfflineSaveStatus.Write(11, 22, OfflineSaveStatus.Active, null), Is.True);
        Assert.That(ReadStatus().GetProperty("result").GetString(), Is.EqualTo("active"));

        Assert.That(OfflineSaveStatus.Write(11, 22, OfflineSaveStatus.Failed, "items: boom"), Is.True);
        JsonElement status = ReadStatus();
        Assert.That(status.GetProperty("characterId").GetInt64(), Is.EqualTo(11));
        Assert.That(status.GetProperty("accountId").GetInt64(), Is.EqualTo(22));
        Assert.That(status.GetProperty("result").GetString(), Is.EqualTo("failed"));
        Assert.That(status.GetProperty("error").GetString(), Is.EqualTo("items: boom"));
        Assert.That(DateTimeOffset.TryParse(status.GetProperty("utc").GetString(), out _), Is.True);
        Assert.That(Directory.GetFiles(Path.GetDirectoryName(statusFile)!), Has.Length.EqualTo(1), "no temp file is left behind");

        Assert.That(OfflineSaveStatus.Write(11, 22, OfflineSaveStatus.Committed, null), Is.True);
        status = ReadStatus();
        Assert.That(status.GetProperty("result").GetString(), Is.EqualTo("committed"));
        Assert.That(status.GetProperty("error").ValueKind, Is.EqualTo(JsonValueKind.Null));

        JsonElement ReadStatus() => JsonDocument.Parse(File.ReadAllText(statusFile!)).RootElement;
    }

    // Inserted with SQL because SQLite cannot auto-generate the Id of the composite (ReceiverId, Id) mail key.
    private Mail CreateMail(long meso) {
        const long mailId = 77;
        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = """
                INSERT INTO mail (ReceiverId, Id, Type, SenderId, SenderName, Title, Content, WeddingInvite, TitleArgs, ContentArgs, Currency, ReadTime, ExpiryTime, SendTime)
                VALUES ($receiver, $id, 0, 0, 'tester', 'reward', '', '', '[]', '[]', $currency, '2026-01-01 00:00:00', '2030-01-01 00:00:00', '2026-01-01 00:00:00')
                """;
            command.Parameters.AddWithValue("$receiver", accountId);
            command.Parameters.AddWithValue("$id", mailId);
            command.Parameters.AddWithValue("$currency", $$"""{"Meso":{{meso}},"MesoCollectTime":0,"Meret":0,"MeretCollectTime":0,"GameMeret":0,"GameMeretCollectTime":0}""");
            command.ExecuteNonQuery();
        }

        using GameStorage.Request db = NewRequest();
        Mail? created = db.GetMail(mailId, accountId);
        Assert.That(created, Is.Not.Null);
        return created!;
    }
}
