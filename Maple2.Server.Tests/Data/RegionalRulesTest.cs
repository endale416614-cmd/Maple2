using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>RG-01: the pack tests of docs/REGIONAL_RULES.md that no other test holds, one per rule, on the pack DB (PACK_DATA_DB),
/// with the base DB (Global's, MAPLE_DATA_DB) attached as "b" for the rules that compare the two. The rules that already had a test
/// are named in the document (PackContentTest.KmsBoxes_GiveTheirKmsContents, KmsConsumables_GoToTheConsumableTab; MeretNewTabTest).</summary>
[Category("Data")]
[NonParallelizable]
public class RegionalRulesTest {
    // Tables whose rows are keyed by Id and that the pack only adds to (the shared tables, such as gacha_info, are extended on purpose).
    private static readonly string[] RecordTables = ["item", "npc", "map", "quest", "skill", "achievement", "pet", "ride", "script", "function-cube"];

    // Global's own maps without a name (80000000) or with a string key (51000006): not shipped by the pack, so outside D-16.
    private static readonly int[] GlobalUnnamedMaps = [80000000, 51000006];

    private static readonly Regex StringKey = new(@"^[A-Z]+_\d+_NAME", RegexOptions.Compiled);
    private static readonly Regex ForeignScript = new("[ᄀ-ᇿ㄰-㆏가-힯぀-ヿ一-鿿]", RegexOptions.Compiled);

    private SqliteConnection connection = null!;

    [SetUp]
    public void SetUp() {
        string? pack = Environment.GetEnvironmentVariable("PACK_DATA_DB");
        if (string.IsNullOrEmpty(pack) || !File.Exists(pack)) {
            Assert.Ignore("set PACK_DATA_DB to the pack's GameData/maple-data.db");
        }
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = pack, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
    }

    [TearDown]
    public void TearDown() => connection?.Dispose();

    private void AttachBase() {
        string? path = DataDb.Find();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        using SqliteCommand attach = connection.CreateCommand();
        attach.CommandText = "ATTACH DATABASE $path AS b";
        attach.Parameters.AddWithValue("$path", path);
        attach.ExecuteNonQuery();
    }

    private long Count(string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long) command.ExecuteScalar()!;
    }

    private List<string> Rows(string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) {
            rows.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.GetValue(i))));
        }
        return rows;
    }

    // A tab-separated table of tools/data, found from the test's folder up (the file is the pack build's input).
    private static List<string[]> Table(string name) {
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "tools", "data", name);
            if (File.Exists(candidate)) {
                return File.ReadAllLines(candidate).Skip(1).Select(line => line.Split('\t')).ToList();
            }
        }
        throw new FileNotFoundException($"tools/data/{name} not found above {AppContext.BaseDirectory}");
    }

    // Rule 1: Global stays. A Global id keeps every column of its record in the pack; only its name may change (rule 4).
    [Test]
    public void GlobalRecords_StayAsTheyAre() {
        AttachBase();
        foreach (string table in RecordTables) {
            var columns = new List<string>();
            foreach (string row in Rows($"SELECT name FROM pragma_table_info('{table}') WHERE name NOT IN ('Id', 'Name')")) {
                columns.Add(row);
            }
            string changed = string.Join(" OR ", columns.Select(column => $"x.\"{column}\" IS NOT y.\"{column}\""));
            if (changed.Length > 0) {
                Assert.That(Rows($"SELECT x.Id FROM \"{table}\" x JOIN b.\"{table}\" y ON x.Id = y.Id WHERE {changed} LIMIT 10"), Is.Empty, $"{table}: Global rows changed");
            }
            Assert.That(Rows($"SELECT Id FROM b.\"{table}\" WHERE Id NOT IN (SELECT Id FROM main.\"{table}\") LIMIT 10"), Is.Empty, $"{table}: Global rows missing");
        }
    }

    // Rule 1 and rule 4: a Global name changes only where Global shows none, a string key, Hangul or a developer label (D-16, NM-04's two reviewed fixes).
    [Test]
    public void GlobalNames_ChangeOnlyWhereGlobalShowsNone() {
        AttachBase();
        foreach (string table in new[] { "item", "npc", "map" }) {
            List<string> changed = Rows($"SELECT x.Id, y.Name FROM \"{table}\" x JOIN b.\"{table}\" y ON x.Id = y.Id WHERE x.Name <> y.Name AND trim(y.Name) <> ''")
                .Where(row => !StringKey.IsMatch(row.Split(" | ", 2)[1]) && !ForeignScript.IsMatch(row)).ToList();
            // 11620059 and 11620060 (gloves named as earrings), 20300845 ("(Test)" label), 20301521 (trailing blanks)
            int[] fixes = [11620059, 11620060, 20300845, 20301521];
            Assert.That(changed.Select(row => int.Parse(row.Split(" | ")[0])).Except(fixes), Is.Empty, $"{table}: Global names changed");
        }
    }

    // Rule 2: a KMS record that duplicates a Global one is found by fingerprint and not added twice. tools/data/unified-selection.tsv
    // (id, name, fingerprint, verdict, decision) is the judgement; the pack holds every kept id and no dropped one.
    [Test]
    public void FingerprintDuplicates_AreNotAddedTwice() {
        List<string[]> selection = Table("unified-selection.tsv").Where(row => row.Length > 4 && int.TryParse(row[0], out _)).ToList();
        HashSet<string> kept = selection.Where(row => row[4] == "keep").Select(row => row[0]).ToHashSet();
        HashSet<string> dropped = selection.Where(row => row[4] == "drop").Select(row => row[0]).ToHashSet();
        Assert.That(kept, Has.Count.GreaterThan(3000));
        Assert.That(dropped, Has.Count.GreaterThan(100));

        HashSet<string> shipped = Rows("SELECT Id FROM item").ToHashSet();
        Assert.That(kept.Except(shipped), Is.Empty, "kept items missing from the pack");
        Assert.That(dropped.Intersect(shipped), Is.Empty, "dropped duplicates shipped");
    }

    // Rule 3 (ids) and rule 5: a KMS record whose id Global uses for another item ships under its home id (tools/data/kms-homes.tsv);
    // the home is an id Global lacks, the Global id keeps Global's record (rule 1), and a twin names an item the pack ships.
    [Test]
    public void KmsHomes_AreFreeIdsAndShipped() {
        AttachBase();
        List<string[]> homes = Table("kms-homes.tsv").Where(row => row.Length > 2).ToList();
        string[] copies = homes.Where(row => row[1] == "copy").Select(row => row[2]).ToArray();
        string[] twins = homes.Where(row => row[1] == "twin").Select(row => row[2]).ToArray();
        Assert.That(copies, Has.Length.GreaterThan(100));
        Assert.That(twins, Has.Length.GreaterThan(100));

        HashSet<string> shipped = Rows("SELECT Id FROM main.item").ToHashSet();
        HashSet<string> global = Rows("SELECT Id FROM b.item").ToHashSet();
        Assert.That(copies.Except(shipped), Is.Empty, "copy homes missing from the pack");
        Assert.That(copies.Intersect(global), Is.Empty, "a copy home is a Global id");
        Assert.That(twins.Except(shipped), Is.Empty, "twins missing from the pack");
        Assert.That(homes.Where(row => row[1] == "copy").Select(row => row[0]).Except(global), Is.Empty, "a copy's own id is no Global id");
    }

    // Rule 4 (D-16): every item, NPC and map the pack ships has an English name: not blank, not a string key, not Hangul or other foreign script.
    [Test]
    public void ShippedRecords_HaveEnglishNames() {
        foreach (string table in new[] { "item", "npc", "map" }) {
            var bad = new List<string>();
            foreach (string row in Rows($"SELECT Id, Name FROM \"{table}\"")) {
                string[] parts = row.Split(" | ", 2);
                if (table == "map" && GlobalUnnamedMaps.Contains(int.Parse(parts[0]))) {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(parts[1]) || StringKey.IsMatch(parts[1]) || ForeignScript.IsMatch(parts[1])) {
                    bad.Add(row);
                }
            }
            Assert.That(bad, Is.Empty, $"{table}: {bad.Count} without an English name, e.g. {string.Join("; ", bad.Take(5))}");
        }
    }
}
