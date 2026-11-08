using System.Linq;
using System.Text.Json;
using Maple2.Server.Game.Manager;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-15a: the prestige level's abilities. Golden: 1-Tria.msb, prestige level 402, its 15 Buff packets in order.</summary>
public class PrestigeAbilityTest {
    [TestCase(4, 5, 50, 20, 0)]
    [TestCase(5, 5, 50, 20, 1)]
    [TestCase(54, 5, 50, 20, 1)]
    [TestCase(55, 5, 50, 20, 2)]
    [TestCase(402, 5, 50, 20, 8)]
    [TestCase(402, 60, 30, 11, 11)]
    [TestCase(402, 80, 30, 20, 11)]
    [TestCase(402, 90, 40, 10, 8)]
    [TestCase(402, 70, 0, 1, 1)]
    [TestCase(69, 70, 0, 1, 0)]
    public void Level(int prestigeLevel, int requiredLevel, int interval, int maxCount, int expected) {
        Assert.That(ExperienceManager.PrestigeAbilityLevel(prestigeLevel, requiredLevel, interval, maxCount), Is.EqualTo(expected));
    }

    [Test]
    [Category("Data")]
    public void TheCapturedEffects_AtPrestige402() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select [Table] from [table] where Name = 'adventurelevelability.xml'";
        using JsonDocument table = JsonDocument.Parse((string) command.ExecuteScalar()!);
        static int Int(JsonElement row, string name) => row.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : 0;

        (int, int)[] effects = table.RootElement.GetProperty("Entries").EnumerateObject()
            .Select(entry => entry.Value)
            .OrderBy(row => Int(row, "RequiredLevel"))
            .Select(row => (Int(row, "BuffId"), (int) ExperienceManager.PrestigeAbilityLevel(402, Int(row, "RequiredLevel"), Int(row, "Interval"), Int(row, "MaxCount"))))
            .ToArray();

        Assert.That(effects, Is.EqualTo(new[] {
            (100000028, 8), (100000029, 8), (100000030, 8), (100000031, 8), (100000032, 8), (100000033, 8), (100000034, 8), (100000035, 8),
            (100000036, 8), (100000037, 8), (100000055, 11), (100000040, 1), (100000042, 11), (100000039, 8), (100000041, 1),
        }));
    }
}