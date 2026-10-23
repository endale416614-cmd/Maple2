using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Maple2.Database.Context;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data
{
    [Category("Data")]
    [NonParallelizable]
    public class PackContentTest
    {
        private string path;
        private MetadataContext context;
        private SqliteConnection connection;

        [SetUp]
        public void SetUp()
        {
            path = Environment.GetEnvironmentVariable("PACK_DATA_DB");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Assert.Ignore("set PACK_DATA_DB to the pack's GameData/maple-data.db");
            }

            connection = new SqliteConnection($"Data Source=\"{path}\";Mode=ReadOnly;");
            connection.Open();
            context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options)
            {
                ChangeTracker =
                {
                    QueryTrackingBehavior = QueryTrackingBehavior.NoTracking
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            context?.Dispose();
            connection?.Dispose();
        }

        // CONTENT_REGISTRY.md: the 16 maps come with every pack build (nightly maps the extra features enable; art in the resource tails)
        private static readonly (int Id, string XBlock)[] AddedMaps = new[]
        {
            (2000554, "02000554_bf"),
            (50000016, "50000016_dl"),
            (52000189, "52000189_qd"),
            (52000190, "52000190_qd"),
            (52000191, "52000191_qd"),
            (52000192, "52000192_qd"),
            (52000193, "52000193_qd"),
            (52000194, "52000194_qd"),
            (52000195, "52000195_qd"),
            (52000196, "52000196_qd"),
            (52000197, "52000197_qd"),
            (52000198, "52000198_qd"),
            (52000199, "52000199_qd"),
            (52000200, "52000200_qd"),
            (52000201, "52000201_qd"),
            (52000202, "52000202_qd")
        };

        [Test]
        public void DataBuild_NamesThePack()
        {
            var buildJsonPath = Path.Combine(Path.GetDirectoryName(path), "data-build.json");
            Assert.IsTrue(File.Exists(buildJsonPath), $"data-build.json not found at {buildJsonPath}");

            var json = File.ReadAllText(buildJsonPath);
            var doc = JsonDocument.Parse(json);
            var contentPack = doc.RootElement.GetProperty("contentPack").GetString();

            Assert.IsFalse(string.IsNullOrEmpty(contentPack), "contentPack is empty");
        }

        // Pack 1.2.1-dev.b2 (BA-03): batches 1-2 of the unified content (cosmetics; weapons, armour, accessories); each batch raises these.
        [Test]
        public void Totals_AtLeastTheRegistry()
        {
            Assert.That(context.NpcMetadata.Count(), Is.GreaterThanOrEqualTo(7519));
            Assert.That(context.QuestMetadata.Count(), Is.GreaterThanOrEqualTo(4946));
            Assert.That(context.MapMetadata.Count(), Is.GreaterThanOrEqualTo(1200));
            Assert.That(context.ItemMetadata.Count(), Is.GreaterThanOrEqualTo(38206));
        }

        [Test]
        public void AddedMaps_HaveRowsAndEntities()
        {
            var missingMaps = new System.Text.StringBuilder();
            foreach (var (id, xblock) in AddedMaps)
            {
                var mapMetadata = context.MapMetadata.FirstOrDefault(m => m.XBlock == xblock);
                if (mapMetadata == null)
                {
                    missingMaps.AppendLine($"Missing MapMetadata for XBlock: {xblock}");
                    continue;
                }

                var mapEntity = context.MapEntity.FirstOrDefault(e => e.XBlock == xblock);
                if (mapEntity == null)
                {
                    missingMaps.AppendLine($"Missing MapEntity for XBlock: {xblock}");
                }
            }

            Assert.IsEmpty(missingMaps.ToString(), $"Missing maps or entities: {missingMaps}");
        }

        [Test]
        public void Lanemone_SpawnsInThePrismaticLab()
        {
            var npc = context.NpcMetadata.FirstOrDefault(n => n.Id == 11004802);
            Assert.IsNotNull(npc, "NPC 11004802 (Lanemone) not found");

            Assert.That(npc.Name, Is.EqualTo("Lanemone"));

            var entities = context.MapEntity.Where(e => e.XBlock == "52000196_qd").ToList();
            var spawnPoint = entities.Select(e => e.Block).OfType<SpawnPointNPC>().FirstOrDefault(spawn =>
                spawn.NpcList.Any(n => n.NpcId == 11004802));

            Assert.IsNotNull(spawnPoint, "Lanemone (11004802) not found in SpawnPointNPC for 52000196_qd");
        }

        [Test]
        public void AddedItems_HaveData()
        {
            var item1 = context.ItemMetadata.FirstOrDefault(i => i.Id == 11020411);
            Assert.IsNotNull(item1, "Item 11020411 (Exotic Red Opera Mask) not found");
            Assert.That(item1.Name, Is.EqualTo("Exotic Red Opera Mask"));
            Assert.That(item1.SlotNames, Is.Not.Empty, "SlotNames is empty for Exotic Red Opera Mask");

            // Batch 1 (cosmetics); Earthworm Bait 20000284 comes with batch 3 (BA-03).
            var item2 = context.ItemMetadata.FirstOrDefault(i => i.Id == 10300186);
            Assert.IsNotNull(item2, "Item 10300186 (Tough Face (M)) not found");

            // batch 2 (gear)
            var item3 = context.ItemMetadata.FirstOrDefault(i => i.Id == 11200106);
            Assert.IsNotNull(item3, "Item 11200106 (Old Arena Earrings) not found");
            Assert.That(item3.Name, Is.EqualTo("Old Arena Earrings"));
        }

        [Test]
        public void AddedQuests_Exist()
        {
            // Nightly quests the extra features enable, in every pack build; the pack's own quests (10002003, 10003448)
            // come with batch 4 (BA-03).
            Assert.IsTrue(context.QuestMetadata.Any(q => q.Id == 10003399), "Quest 10003399 not found");
            Assert.IsTrue(context.QuestMetadata.Any(q => q.Id == 10003403), "Quest 10003403 not found");
        }
    }
}