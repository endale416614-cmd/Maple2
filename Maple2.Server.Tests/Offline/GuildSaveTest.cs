using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maple2.Server.Tests.Offline;

/// <summary>GD-01: a guild save keeps the guild's progress and the rows of members that are not in memory.</summary>
[NonParallelizable]
public class GuildSaveTest {
    private SqliteConnection connection = null!;
    private DbContextOptions options = null!;
    private readonly Dictionary<long, PlayerInfo> infos = new();
    private long leaderId;
    private long memberId;

    private sealed class Provider(Dictionary<long, PlayerInfo> infos) : IPlayerInfoProvider {
        public PlayerInfo? GetPlayerInfo(long id) => infos.GetValueOrDefault(id);
    }

    [SetUp]
    public void SetUp() {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        options = new DbContextOptionsBuilder().UseSqlite(connection).Options;
        using (var context = new Ms2Context(options)) {
            context.Database.EnsureCreated();
            // The id ranges of a real save (DbProvider.EnsureSqliteGameSchema); otherwise account and character both get id 1.
            context.Database.ExecuteSqlRaw("INSERT INTO sqlite_sequence (name, seq) VALUES ('account', 9999999999), ('character', 19999999999)");
        }

        using GameStorage.Request db = NewRequest();
        long accountId = db.CreateAccount(new Account { Username = "guildsave", MachineId = Guid.NewGuid() }, "password").Id;
        leaderId = NewCharacter(db, accountId, "GuildLeader");
        memberId = NewCharacter(db, accountId, "GuildMember");
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

    private long NewCharacter(GameStorage.Request db, long accountId, string name) {
        // A return map as character creation gives one (CharacterManagementHandler); the save conversion reads it.
        long id = db.CreateCharacter(new Character {
            AccountId = accountId, Name = name, Mastery = new Mastery(), ReturnMaps = new LimitedStack<int>(3, 2000062),
        })!.Id;
        var character = new CharacterInfo(accountId, id, name, string.Empty, string.Empty, Gender.Male, Job.Newbie, 1);
        infos[id] = new PlayerInfo(character, "Home", default, new List<long>());
        return id;
    }

    [Test]
    public void SaveGuild_KeepsTheProgressAndTheMemberRowsMissingFromMemory() {
        const long checkinTime = 1_800_000_000;
        Guild guild;
        using (GameStorage.Request db = NewRequest()) {
            guild = db.CreateGuild("GuildSave", leaderId)!;
            // Only the leader is in memory; the other member's row is in the database.
            Assert.That(db.CreateGuildMember(guild.Id, infos[memberId]), Is.Not.Null);
        }

        guild.Experience = 1200;
        guild.Funds = 340;
        guild.Members.TryAdd(leaderId, new GuildMember {
            GuildId = guild.Id, Info = infos[leaderId], Rank = 0, CheckinTime = checkinTime, WeeklyContribution = 10,
        });
        using (GameStorage.Request db = NewRequest()) {
            Assert.That(db.SaveGuild(guild), Is.True);
        }

        using (GameStorage.Request db = NewRequest()) {
            Guild? saved = db.GetGuild(guild.Id);
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.Experience, Is.EqualTo(1200));
            Assert.That(saved.Funds, Is.EqualTo(340));

            IList<GuildMember> members = db.GetGuildMembers(new Provider(infos), guild.Id);
            Assert.That(members.Select(member => member.CharacterId), Is.EquivalentTo(new[] { leaderId, memberId }));
            GuildMember leader = members.Single(member => member.CharacterId == leaderId);
            Assert.That(leader.CheckinTime, Is.EqualTo(checkinTime));
            Assert.That(leader.WeeklyContribution, Is.EqualTo(10));
        }
    }
}
