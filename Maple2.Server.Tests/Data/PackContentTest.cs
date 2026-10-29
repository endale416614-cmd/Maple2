using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Maple2.Database.Context;
using Maple2.Database.Storage;
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

            // Pack 1.2.2-dev.b3 (BA-07): batch 3 (furniture, pets, mounts, consumables) and the 2 pet NPCs; the 1.2.1 minimums above stay.
            Assert.That(context.NpcMetadata.Count(), Is.GreaterThanOrEqualTo(7521));
            Assert.That(context.ItemMetadata.Count(), Is.GreaterThanOrEqualTo(39191));
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

            // Batch 1 (cosmetics)
            var item2 = context.ItemMetadata.FirstOrDefault(i => i.Id == 10300186);
            Assert.IsNotNull(item2, "Item 10300186 (Tough Face (M)) not found");

            // batch 2 (gear)
            var item3 = context.ItemMetadata.FirstOrDefault(i => i.Id == 11200106);
            Assert.IsNotNull(item3, "Item 11200106 (Old Arena Earrings) not found");
            Assert.That(item3.Name, Is.EqualTo("Old Arena Earrings"));

            // batch 3 (other items)
            var item4 = context.ItemMetadata.FirstOrDefault(i => i.Id == 20001058);
            Assert.IsNotNull(item4, "Item 20001058 (Auto-Fishing Voucher (30 Min)) not found");
            Assert.That(item4.Name, Is.EqualTo("Auto-Fishing Voucher (30 Min)"));
        }

        // A pack NPC file ships with the batch of the pet item that has its id (BA-03s); batch 3 has two.
        [Test]
        public void PetNpcs_ShipWithTheirPets()
        {
            Assert.IsTrue(context.NpcMetadata.Any(n => n.Id == 60000062), "NPC 60000062 (Nao Mabinogi Stuffed Toy) not found");
            Assert.IsTrue(context.NpcMetadata.Any(n => n.Id == 61100150), "NPC 61100150 (Black Bean) not found");
        }

        // Pack 1.2.2-dev.b3b (BA-09): each KMS mount names its own skill copy (IM-03), a ground mount with subtype 12.
        [Test]
        public void KmsMounts_NameTheirSkillCopies()
        {
            var ground = context.ItemMetadata.FirstOrDefault(i => i.Id == 50600281);
            Assert.IsNotNull(ground, "Item 50600281 (Blue Tyrannosaurus) not found");
            Assert.That(ground.Skill?.Id, Is.EqualTo(80001281));
            Assert.That(ground.Property.SubType, Is.EqualTo(12));
            Assert.IsTrue(context.SkillMetadata.Any(s => s.Id == 80001281), "Skill 80001281 (the summon copy of 50600281) not found");

            var air = context.ItemMetadata.FirstOrDefault(i => i.Id == 40400055);
            Assert.IsNotNull(air, "Item 40400055 not found");
            Assert.That(air.Skill?.Id, Is.EqualTo(80011055));
            Assert.IsTrue(context.SkillMetadata.Any(s => s.Id == 80011055), "Skill 80011055 (the glide copy of 40400055) not found");
        }

        // The KMS pet model has its animation keys (IM-04), so the server pet walks.
        [Test]
        public void KmsPetModel_HasAnimationKeys()
        {
            Assert.IsTrue(context.AnimationMetadata.Any(a => a.Model == "60000062_nao"), "Animation 60000062_nao (Nao Mabinogi Stuffed Toy) not found");
        }

        // A KMS copy whose KMS name equals its twin's carries the twin's English name (NM-03, BT-97).
        [Test]
        public void KmsCopies_CarryTheirTwinsName()
        {
            var cello = context.ItemMetadata.FirstOrDefault(i => i.Id == 34000072);
            Assert.IsNotNull(cello, "Item 34000072 not found");
            Assert.That(cello.Name, Is.EqualTo("Deep Dark Cello"));
        }

        // Pack 1.2.2-dev.b3c (BA-10): the pet NPC 61100150 carries its pet's name (NM-06, BT-106).
        [Test]
        public void KmsPetNpc_CarriesThePetsName()
        {
            var bean = context.NpcMetadata.FirstOrDefault(n => n.Id == 61100150);
            Assert.IsNotNull(bean, "NPC 61100150 (Black Bean) not found");
            Assert.That(bean.Name, Is.EqualTo("Black Bean"));
        }

        // Pack 1.2.2-dev.b3d (BA-11): KMS boxes and capsules give their KMS contents under home ids (IB-02, BT-114); Global's boxes stay.
        [Test]
        public void KmsBoxes_GiveTheirKmsContents()
        {
            var boxes = new ServerTableMetadataStorage(context).IndividualDropItemTable.Entries;
            var gacha = new TableMetadataStorage(context).GachaInfoTable.Entries;
            int[] Prizes(int box) => boxes[box].Values.SelectMany(group => group.Items).SelectMany(entry => entry.Ids).Where(id => id > 0).Distinct().ToArray();
            ItemMetadata Item(int id) => context.ItemMetadata.Single(item => item.Id == id);

            // a Super Chat Theme Bundle opens KMS's box: 10 Super Chat Themes; Global's box of that id keeps its beauty vouchers
            Assert.That(Item(20802020).Function?.Parameters, Is.EqualTo("0,1,0,320090392"));
            var theme = boxes[320090392].Values.Single().Items.Single();
            Assert.That(theme.Ids.Where(id => id > 0), Is.EqualTo(new[] { 20800019 }));
            Assert.That(theme.DropCount.Min, Is.EqualTo(10));
            Assert.That(Prizes(320000392), Does.Contain(20500005));

            // capsule 22004504: its row names KMS's box, whose one prize is the KMS voucher box copy; Global's 20303049 stays
            Assert.That(gacha[1004504].DropBoxId, Is.EqualTo(545090004));
            Assert.That(Prizes(545090004), Is.EqualTo(new[] { 20393049 }));
            Assert.That(Item(20393049).Name, Is.EqualTo("Instant Gathering Voucher (50)"));
            Assert.That(Item(20303049).Name, Is.EqualTo("Wayward Soul Accessory Box"));

            // a KMS weapon box takes the copy of its KMS key; 22004502 gives its 41 KMS prizes (the server skipped the KR rows)
            Assert.That(Item(20301047).Function?.Parameters, Does.StartWith("20090985,"));
            Assert.That(Prizes(gacha[1004502].DropBoxId), Has.Length.EqualTo(41));

            // a twin: where KMS's box names its re-issue 11320293, the home box names Global's 11300308
            Assert.That(Prizes(320090526), Does.Contain(11300308).And.Not.Contain(11320293));

            // a KMS weapon box gives each class its row: a KMS duplicate the selection dropped names the item shipped in its place
            var weapons = boxes[320090181].Values.Single().Items;
            Assert.That(weapons, Has.Count.EqualTo(11));
            Assert.That(weapons.SelectMany(entry => entry.Ids), Does.Contain(13100329).And.Not.Contain(13100338));

            // a twin is for the KMS item's gender: the female Calico Cat stays Global's female one
            Assert.That(Prizes(510090044), Does.Contain(11320347).And.Contain(11320348));
        }

        // Pack 1.2.2-dev.b3d (BA-11): KMS potions, lures, vouchers and snares that came without a subtype go to the Consumable tab (IT-06, BT-112).
        [Test]
        public void KmsConsumables_GoToTheConsumableTab()
        {
            foreach (int id in new[] { 20001056, 20001880, 63100502 })
                Assert.That(context.ItemMetadata.Single(item => item.Id == id).Property.SubType, Is.EqualTo(2), $"item {id}");
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