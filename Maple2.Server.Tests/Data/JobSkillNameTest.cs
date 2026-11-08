using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// NM-07 (BT-167): every job skill the skill tree shows has a name. The tree shows a job.xml skill only when it has a uiPosition, under
/// its main skill, whose name the ingest reads from the client's skill name strings (string/en). The blank names in the data are sub skills
/// and the four main skills below, which have no uiPosition in Global's job.xml. Skipped when no data DB is found.
/// </summary>
[Category("Data")]
public class JobSkillNameTest {
    private static readonly int[] HiddenWithoutName = [1000001, 1000011, 1200001, 1200011];

    [Test]
    public void ShownJobSkillsHaveNames_BaseData() => Check(DataDb.Find(), "MAPLE_DATA_DB");

    [Test]
    public void ShownJobSkillsHaveNames_PackData() => Check(Environment.GetEnvironmentVariable("PACK_DATA_DB"), "PACK_DATA_DB to the pack's GameData/maple-data.db");

    private static void Check(string? path, string setting) {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Assert.Ignore("maple-data.db not found (set " + setting + ")");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select [Table] from [table] where Name = 'job.xml'";
        using JsonDocument job = JsonDocument.Parse((string) command.ExecuteScalar()!);
        int[] mains = job.RootElement.GetProperty("Entries").EnumerateObject()
            .SelectMany(entry => entry.Value.GetProperty("Skills").EnumerateObject())
            .SelectMany(rank => rank.Value.EnumerateArray())
            .Select(skill => skill.GetProperty("Main").GetInt32())
            .Distinct()
            .ToArray();

        var named = new HashSet<int>();
        command.CommandText = "select Id from skill where trim(Name) <> ''";
        using (SqliteDataReader reader = command.ExecuteReader()) {
            while (reader.Read()) {
                named.Add(reader.GetInt32(0));
            }
        }

        // A main skill without skill data counts as unnamed too.
        int[] unnamed = mains.Where(id => !named.Contains(id)).Order().ToArray();
        Assert.That(unnamed, Is.EqualTo(HiddenWithoutName), $"main skills without a name, of the {mains.Length} in job.xml");
    }
}
