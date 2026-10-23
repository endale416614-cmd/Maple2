using System;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>Tests for the world clock functionality (T-02).</summary>
[NonParallelizable]
public class WorldTicksTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;

    [SetUp]
    public void SetUp() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
        }
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

    [Test]
    public void SaveAndRead_RoundTrip() {
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetWorldTicks(), Is.EqualTo((0, 0)));
        }

        using (GameStorage.Request db = NewRequest()) {
            db.SaveWorldTicks(123456, 789);
        }

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetWorldTicks(), Is.EqualTo((123456, 789)));
        }

        using (GameStorage.Request db = NewRequest()) {
            db.SaveWorldTicks(200000, 900);
        }

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.GetWorldTicks(), Is.EqualTo((200000, 900)));
        }
    }
}