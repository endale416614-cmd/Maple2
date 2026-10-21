using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Server.Game.Manager;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// W-04: event progress is deleted only when it expires. A value of an event that is not active at login stays in the
/// save because GameEventManager.Save passes only the values of active events and the store updates only what it is given.
/// </summary>
[NonParallelizable]
public class EventProgressTest {
    [Test]
    public void Expired_OnlyAfterTheExpirationTime() {
        var value = new GameEventUserValue(GameEventUserValueType.AttendanceAccumulatedTime, 1_000, 1);
        Assert.Multiple(() => {
            Assert.That(GameEventManager.Expired(value, 999), Is.False);
            Assert.That(GameEventManager.Expired(value, 1_000), Is.False);
            Assert.That(GameEventManager.Expired(value, 1_001), Is.True);
        });
    }

    [Test]
    public void SavingTheActiveEventsValues_LeavesAnInactiveEventsValue() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        DbContextOptions options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
            context.Database.ExecuteSqlRaw("INSERT INTO sqlite_sequence (name, seq) VALUES ('account', 9999999999), ('character', 19999999999)");
        }
        GameStorage.Request Request() {
            var context = new Ms2Context(options);
            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            return new GameStorage.Request(null!, context, NullLogger.Instance);
        }

        long characterId;
        using (GameStorage.Request db = Request()) {
            long accountId = db.CreateAccount(new Account { Username = "events", MachineId = Guid.NewGuid() }, "password").Id;
            characterId = db.CreateCharacter(new Character {
                AccountId = accountId, Name = "Events", Mastery = new Mastery(), ReturnMaps = new LimitedStack<int>(3, 2000062),
            })!.Id;
            var active = new GameEventUserValue(GameEventUserValueType.AttendanceRewardsClaimed, 2_000_000_000, 1);
            var inactive = new GameEventUserValue(GameEventUserValueType.AttendanceRewardsClaimed, 2_000_000_000, 2);
            active.SetValue("1");
            inactive.SetValue("5");
            Assert.That(db.SaveGameEventUserValues(characterId, [active, inactive]), Is.True);
        }

        using (GameStorage.Request db = Request()) {
            // A later session where event 2 is not active saves event 1's value only.
            var active = new GameEventUserValue(GameEventUserValueType.AttendanceRewardsClaimed, 2_000_000_000, 1);
            active.SetValue("2");
            Assert.That(db.SaveGameEventUserValues(characterId, [active]), Is.True);
        }

        using (GameStorage.Request db = Request()) {
            var stored = db.GetEventUserValues(characterId).ToDictionary(value => value.EventId, value => value.Value);
            Assert.That(stored, Is.EqualTo(new Dictionary<int, string> { [1] = "2", [2] = "5" }));
        }
    }
}
