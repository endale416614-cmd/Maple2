extern alias ingest;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ingest::M2dXmlGenerator;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>BX-02 (BT-117): the ingest reads every row of an item in a drop group. The parser's group.v keeps one row per item id.</summary>
[NonParallelizable] // FeatureLocaleFilter.Locale is process-wide.
public class IndividualDropTableMapperTest {
    // Group 5 has 20001002's rows of Global's individualItemDrop_Final.xml: a row of 1 at weight 99 and a row of 10 for each item. Group 6 is made up.
    private const string Rows = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
          <dropBox dropBoxID="20001002" comment="sample">
            <group dropGroupID="5">
              <v itemID="33000279" weight="99" />
              <v itemID="33000279" minCount="10" />
              <v itemID="33000280" weight="99" />
              <v itemID="33000280" minCount="10" />
            </group>
            <group dropGroupID="6">
              <v itemID="20300652" />
              <v itemID="30001524" weight="7" locale="KR" />
              <v itemID="30001524" weight="8" locale="KR" />
              <v itemID="30001525" weight="1" />
              <v itemID="30001525" weight="2" locale="NA" />
              <v itemID="30001525" weight="3" locale="NA" />
            </group>
          </dropBox>
        </ms2>
        """;

    [Test]
    public void ParseIndividualItemDropTable_ReadsEveryRowOfAnItem() {
        IDictionary<int, IndividualDropItemTable.Entry> box = Parse()[20001002];

        Assert.That(box[5].Items.Select(item => (item.Ids[0], item.Weight, item.DropCount.Min)),
            Is.EqualTo(new[] {(33000279, 99, 1), (33000279, 0, 10), (33000280, 99, 1), (33000280, 0, 10)}));
        Assert.That(box[6].Items.Select(item => (item.Ids[0], item.Weight)), Is.EqualTo(new[] {(20300652, 0), (30001525, 2), (30001525, 3)}),
            "the NA rows of an item replace its rows without a locale, as the parser picks, and the KR rows stay out");
    }

    // The ingest's locale (Program.cs), on a one-entry Server.m2d.
    private static IReadOnlyDictionary<int, IDictionary<int, IndividualDropItemTable.Entry>> Parse() {
        string locale = FeatureLocaleFilter.Locale;
        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            FeatureLocaleFilter.Locale = "NA";
            string path = Path.Combine(scratch.FullName, "Server.m2d");
            MonsterExpTableMapperTest.WriteArchive(path, "table/Server/individualItemDrop_Final.xml", Encoding.UTF8.GetBytes(Rows));
            using var reader = new M2dReader(path);
            return new ServerTableMapper(reader).ParseIndividualItemDropTable().Entries;
        } finally {
            FeatureLocaleFilter.Locale = locale;
            scratch.Delete(true);
        }
    }
}
