using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>BM-04: the offline Black Market's player level is the level of the character saved last.</summary>
[NonParallelizable]
public class OfflineMarketLevelTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using var context = new Ms2Context(options);
        context.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown() => connection?.Dispose();

    private GameStorage.Request NewRequest() {
        var context = new Ms2Context(options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return new GameStorage.Request(null!, context, NullLogger.Instance);
    }

    private static Character NewCharacter(long accountId, string name, short level) => new() {
        AccountId = accountId, Name = name, Level = level, Mastery = new Mastery(), ReturnMaps = new LimitedStack<int>(3, 2000062),
    };

    [Test]
    public void LastPlayedLevel_IsTheLevelOfTheCharacterSavedLast() {
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.LastPlayedLevel(), Is.EqualTo(1), "no character");
            long accountId = db.CreateAccount(new Account { Username = "market", MachineId = Guid.NewGuid() }, "password")!.Id;
            Assert.That(db.CreateCharacter(NewCharacter(accountId, "Old", 30)), Is.Not.Null);
            Assert.That(db.CreateCharacter(NewCharacter(accountId, "New", 55)), Is.Not.Null);
        }
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.LastPlayedLevel(), Is.EqualTo(55), "saved last (a tie goes to the newer character)");
        }

        using (var context = new Ms2Context(options)) {
            context.Database.ExecuteSqlRaw("UPDATE \"character\" SET LastModified = '2000-01-01 00:00:00' WHERE Name = 'New'");
        }
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.LastPlayedLevel(), Is.EqualTo(30));
        }
    }
}
