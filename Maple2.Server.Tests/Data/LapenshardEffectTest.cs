using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>FT-15b: what a lapenshard applies while equipped. Golden: Port 57963.msb, the Buff packets after Equip of 41000041.</summary>
[Category("Data")]
public class LapenshardEffectTest {
    private SqliteConnection connection = null!;

    [SetUp]
    public void SetUp() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
    }

    [TearDown]
    public void TearDown() {
        connection?.Dispose();
    }

    [Test]
    public void TheCapturedEffects_OfSwordOfTime() {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select AdditionalEffects from item where Id = 41000041";
        using JsonDocument effects = JsonDocument.Parse((string) command.ExecuteScalar()!);

        var ids = new List<int>();
        foreach (JsonElement effect in effects.RootElement.EnumerateArray()) {
            ids.Add(effect.GetProperty("Id").GetInt32());
        }

        Assert.That(ids, Is.EqualTo(new[] { 120010005, 90051326, 120010007, 120010008, 120010009 }));
    }

    [Test]
    public void EveryEffect_NeverExpires() {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select count(*) from item i, json_each(i.AdditionalEffects) e " +
                              "where i.Id between 41000000 and 43999999 and not exists (select 1 from [additional-effect] a " +
                              "where a.Id = json_extract(e.value, '$.Id') and json_extract(a.Property, '$.KeepCondition') = 99)";

        Assert.That((long) command.ExecuteScalar()!, Is.EqualTo(0));
    }
}