using System;
using System.IO;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>Tests for buff level fallback functionality.</summary>
[NonParallelizable]
public class EffectLevelFallbackTest {
    private SqliteConnection? dataConnection;
    private MetadataContext? dataContext;
    private SkillMetadataStorage storage = null!;

    private static string? FindDataDb() {
        foreach (string? dir in new[] { Environment.GetEnvironmentVariable("SQLITE_DATA_DIR"), Environment.GetEnvironmentVariable("SQLITE_DIR") }) {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "maple-data.db"))) {
                return Path.Combine(dir, "maple-data.db");
            }
        }
        for (string? dir = AppContext.BaseDirectory; dir != null; dir = Path.GetDirectoryName(dir)) {
            string candidate = Path.Combine(dir, "SaveData", "maple-data.db");
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        return null;
    }

    [OneTimeSetUp]
    public void LoadGameData() {
        string? path = FindDataDb();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set SQLITE_DIR)");
        }

        dataConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        dataConnection.Open();
        dataContext = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(dataConnection).Options);
        dataContext.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        storage = new SkillMetadataStorage(dataContext);
    }

    [OneTimeTearDown]
    public void Unload() {
        dataContext?.Dispose();
        dataConnection?.Dispose();
    }

    [Test]
    public void TryGetEffectOrLevel1_ReturnsCorrectEffect() {
        // Smiting Aura 10400123 at level 3 should return effect level 1
        Assert.That(storage.TryGetEffectOrLevel1(10400123, 3, out AdditionalEffectMetadata? effect), Is.True);
        Assert.That(effect!.Level, Is.EqualTo(1));

        // Colosseum boss 69000501 at level 0 should return effect level 1
        Assert.That(storage.TryGetEffectOrLevel1(69000501, 0, out effect), Is.True);
        Assert.That(effect!.Level, Is.EqualTo(1));

        // Skill 10400122 at level 3 should return effect level 3
        Assert.That(storage.TryGetEffectOrLevel1(10400122, 3, out effect), Is.True);
        Assert.That(effect!.Level, Is.EqualTo(3));

        // Skill 10400123 at level 1 should return effect level 1
        Assert.That(storage.TryGetEffectOrLevel1(10400123, 1, out effect), Is.True);
        Assert.That(effect!.Level, Is.EqualTo(1));

        // Non-existent ID should return false
        Assert.That(storage.TryGetEffectOrLevel1(99999999, 1, out effect), Is.False);
    }
}
