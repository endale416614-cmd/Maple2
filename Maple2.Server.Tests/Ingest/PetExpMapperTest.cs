extern alias ingest;

using System.IO;
using System.Text;
using System.Text.Json;
using ingest::M2dXmlGenerator;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>PE-03: the ingest reads petexp.xml. Golden: rows of GMS table/petexp.xml (Xml.m2d), shortened.</summary>
[NonParallelizable] // FeatureLocaleFilter.Features is process-wide.
public class PetExpMapperTest {
    private const string Rows = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <exp level="1" normal="22000" rare="55000" elite="90000" excellent="90000" legendary="90000" artifact="0" feature="PetBase" />
            <exp level="49" normal="22000" rare="55000" elite="90000" excellent="90000" legendary="90000" artifact="0" feature="PetBase" />
            <exp level="50" normal="0" rare="0" elite="0" excellent="0" legendary="0" artifact="0" feature="PetBase" />
        </ms2>
        """;

    [Test]
    public void ParsePetExpTable_ReadsEachRarityOfEachLevel() {
        bool enabled = FeatureLocaleFilter.Features.ContainsKey("PetBase");
        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            // The ingest enables PetBase from the client's feature settings (the pets themselves need it).
            FeatureLocaleFilter.Features.TryAdd("PetBase", 1);
            string path = Path.Combine(scratch.FullName, "Xml.m2d");
            MonsterExpTableMapperTest.WriteArchive(path, "table/petexp.xml", Encoding.UTF8.GetBytes(Rows));
            using var reader = new M2dReader(path);
            PetExpTable table = new TableMapper(reader, "en").ParsePetExpTable();

            Assert.That(table.Entries.Keys, Is.EquivalentTo(new[] {1, 49, 50}));
            Assert.That(table.Entries[1], Is.EqualTo(new long[] {22000, 55000, 90000, 90000, 90000, 0}));
            Assert.That(table.Entries[49], Is.EqualTo(new long[] {22000, 55000, 90000, 90000, 90000, 0}));
            Assert.That(table.Entries[50], Is.EqualTo(new long[6]));

            Table? read = JsonSerializer.Deserialize<Table>(JsonSerializer.Serialize<Table>(table));
            Assert.That(read, Is.TypeOf<PetExpTable>());
            Assert.That(((PetExpTable) read!).Entries[49], Is.EqualTo(table.Entries[49]), "the row survives the data's JSON");
        } finally {
            if (!enabled) {
                FeatureLocaleFilter.Features.Remove("PetBase");
            }
            scratch.Delete(true);
        }
    }
}
