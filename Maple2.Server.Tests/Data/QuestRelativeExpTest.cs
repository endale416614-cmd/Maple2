using System.Text.Json;
using Maple2.Model;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-17 (BT-81): a reward with no flat Exp pays its relative exp. Golden: Port 57940.msb, ExpUp gained 738490, message 1053, rest 0.</summary>
public class QuestRelativeExpTest {
    [TestCase(ExpType.quest)]
    [TestCase(ExpType.mission)]
    [TestCase(ExpType.dailymission)]
    [TestCase(ExpType.dailymissionLevelUp)]
    [TestCase(ExpType.questSkyFortress)]
    public void Paid(ExpType type) {
        Assert.That(ExperienceManager.PaysRelativeExp(type), Is.True);
    }

    [TestCase(ExpType.none)]
    [TestCase(ExpType.unknown)]
    [TestCase(ExpType.epicQuest)]
    [TestCase(ExpType.dailyGuildQuest)]
    [TestCase(ExpType.weeklyGuildQuest)]
    public void NotPaid(ExpType type) {
        Assert.That(ExperienceManager.PaysRelativeExp(type), Is.False);
    }

    [Test]
    public void Messages_AsCaptured_AndNoPrestigeYet() {
        Assert.That((int) ExpType.quest.Message(), Is.EqualTo(1010));
        Assert.That((int) ExpType.mission.Message(), Is.EqualTo(1053));
        Assert.That(ExpType.dailymission.Message(), Is.EqualTo(ExpMessageCode.s_msg_take_exp));
        Assert.That(ExpMessageCode.s_msg_take_quest_exp.Type(), Is.EqualTo(ExpType.none));
        Assert.That(ExpMessageCode.s_msg_take_mission_exp.Type(), Is.EqualTo(ExpType.none));
    }

    [Test]
    [Category("Data")]
    public void AMission_AtLevel65_PaysTheCapturedAmount() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        JsonElement Read(string sql) {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return JsonDocument.Parse((string) command.ExecuteScalar()!).RootElement.Clone();
        }

        Assert.That(Read("select CompleteReward from quest where Id = 72000147").GetProperty("RelativeExp").GetInt32(), Is.EqualTo((int) ExpType.mission));
        JsonElement entry = Read("select [Table] from [table] where Name = 'commonexp.xml'").GetProperty("Entries").GetProperty("mission");
        Assert.That(entry.GetProperty("ExpTableId").GetInt32(), Is.EqualTo(3));
        long level65 = Read("select [Table] from [table] where Name = 'exp*.xml'").GetProperty("ExpBase").GetProperty("3").GetProperty("65").GetInt64();
        // The same sum as ExperienceManager.AddExp.
        Assert.That((long) ((level65 * 1f) * entry.GetProperty("Factor").GetSingle()), Is.EqualTo(738490));
    }
}