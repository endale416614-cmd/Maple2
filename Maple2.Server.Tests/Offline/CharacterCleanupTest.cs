using System;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>R12-12: deleting a character removes its quest, achievement and item rows, never the account's.</summary>
[NonParallelizable]
public class CharacterCleanupTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;
    private long accountId;
    private long characterId;

    [SetUp]
    public void SetUp() {
        Assume.That(DbProvider.IsSqlite, "runs with DB_PROVIDER=sqlite");
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
            // The id ranges of a real save (DbProvider.EnsureSqliteGameSchema); otherwise account and character both get id 1.
            context.Database.ExecuteSqlRaw("INSERT INTO sqlite_sequence (name, seq) VALUES ('account', 9999999999), ('character', 19999999999)");
        }

        using GameStorage.Request db = NewRequest();
        accountId = db.CreateAccount(new Account { Username = "cleanup", MachineId = Guid.NewGuid() }, "password").Id;
        // A return map as character creation gives one (CharacterManagementHandler); the save conversion reads it.
        characterId = db.CreateCharacter(new Character {
            AccountId = accountId, Name = "Cleanup", Mastery = new Mastery(), ReturnMaps = new LimitedStack<int>(3, 2000062),
        })!.Id;
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

    private void Insert(string sql, params object[] args) {
        using var context = new Ms2Context(options);
        context.Database.ExecuteSqlRaw(sql, args);
    }

    private void InsertQuest(long ownerId, int questId) => Insert(
        "INSERT INTO quest (OwnerId, Id, State, CompletionCount, StartTime, EndTime, Track, Conditions) " +
        "VALUES ({0}, {1}, 0, 0, 0, 0, 0, '{{}}')", ownerId, questId);

    private void InsertAchievement(long ownerId, int achievementId) => Insert(
        "INSERT INTO achievement (OwnerId, Id, CompletedCount, CurrentGrade, RewardGrade, Favorite, Counter, Category, Grades) " +
        "VALUES ({0}, {1}, 0, 0, 0, 0, 0, 0, '{{}}')", ownerId, achievementId);

    private void InsertItem(long ownerId) => Insert(
        "INSERT INTO item (OwnerId, ItemId, Rarity, Slot, \"Group\", Amount, ExpiryTime, TimeChangedOption, RemainUses, IsLocked, " +
        "UnlockTime, GlamorForges, GachaDismantleId, Appearance, CreationTime, LastModified) " +
        "VALUES ({0}, 13100051, 1, -1, 0, 1, '0001-01-01 00:00:00', 0, 0, 0, 0, 0, 0, '{{}}', '2026-01-01 00:00:00', '2026-01-01 00:00:00')",
        ownerId);

    private long Count(string sql, long id) {
        using var context = new Ms2Context(options);
        return context.Database.SqlQueryRaw<long>(sql, id).AsEnumerable().First();
    }

    private const string Quests = "SELECT COUNT(*) AS Value FROM quest WHERE OwnerId = {0}";
    private const string Achievements = "SELECT COUNT(*) AS Value FROM achievement WHERE OwnerId = {0}";
    private const string Items = "SELECT COUNT(*) AS Value FROM item WHERE OwnerId = {0}";
    private const string Characters = "SELECT COUNT(*) AS Value FROM \"character\" WHERE Id = {0}";

    [Test]
    public void DeleteCharacter_RemovesItsQuestAchievementAndItemRows() {
        InsertQuest(characterId, 100);
        InsertAchievement(characterId, 200);
        InsertItem(characterId);

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.DeleteCharacter(accountId, characterId), Is.True);
        }

        Assert.Multiple(() => {
            Assert.That(Count(Quests, characterId), Is.EqualTo(0));
            Assert.That(Count(Achievements, characterId), Is.EqualTo(0));
            Assert.That(Count(Items, characterId), Is.EqualTo(0));
            Assert.That(Count(Characters, characterId), Is.EqualTo(0));
        });
    }

    [Test]
    public void DeleteCharacter_KeepsTheAccountRows() {
        InsertQuest(accountId, 100); // an account-wide quest
        InsertAchievement(accountId, 200); // an account-wide achievement
        InsertItem(accountId); // account storage
        InsertQuest(characterId, 100);

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.DeleteCharacter(accountId, characterId), Is.True);
        }

        Assert.Multiple(() => {
            Assert.That(Count(Quests, accountId), Is.EqualTo(1));
            Assert.That(Count(Achievements, accountId), Is.EqualTo(1));
            Assert.That(Count(Items, accountId), Is.EqualTo(1));
            Assert.That(Count(Quests, characterId), Is.EqualTo(0));
        });
    }

    [Test]
    public void DeleteCharacter_OfAnotherAccount_RemovesNothing() {
        InsertQuest(characterId, 100);

        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.DeleteCharacter(accountId + 1, characterId), Is.False);
        }

        Assert.Multiple(() => {
            Assert.That(Count(Quests, characterId), Is.EqualTo(1));
            Assert.That(Count(Characters, characterId), Is.EqualTo(1));
        });
    }

    [Test]
    public void IdRanges_KeepAccountAndCharacterApart() {
        Assert.That(characterId, Is.Not.EqualTo(accountId));
        Assert.That(characterId, Is.GreaterThanOrEqualTo(20000000000L));
    }
}