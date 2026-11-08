extern alias ingest;

using System.Collections.Generic;
using System.IO;
using System.Text;
using ingest::M2dXmlGenerator;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>FT-13c: the ingest keeps a dungeon room's isExpireTimeOut. Golden: rows of GMS table/na/dungeonroom.xml (Xml.m2d), shortened.</summary>
[NonParallelizable] // FeatureLocaleFilter.Locale is process-wide.
public class DungeonRoomMapperTest {
    // 23006003 (02000326) fails at 0 and its scripts read no dungeon_timeout; 24090005's script fails the run itself; 1001 has no timer.
    private const string Rows = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <dungeonRoom dungeonRoomID="1001" cooldownType="nextDay" rewardCount="6" />
            <dungeonRoom dungeonRoomID="23006003" dungeonLevel="50" playType="limitReward" groupType="chaosRaid" lobbyFieldID="50000003" fieldIDs="02000326" durationTick="900000" isExpireTimeOut="1" timerType="clock" maxUserCount="10" />
            <dungeonRoom dungeonRoomID="24090005" dungeonLevel="50" playType="limitReward" groupType="chaosRaid" lobbyFieldID="50000007" fieldIDs="02000428" durationTick="300000" isExpireTimeOut="0" timerType="clock" maxUserCount="10" />
        </ms2>
        """;

    [Test]
    public void ParseDungeonRoom_KeepsIsExpireTimeOut() {
        string locale = FeatureLocaleFilter.Locale;
        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            // The ingest's locale and LANGUAGE=EN (Program.cs, .env.example): the parser reads the na table, as on the GMS Xml.m2d
            FeatureLocaleFilter.Locale = "NA";
            string path = Path.Combine(scratch.FullName, "Xml.m2d");
            MonsterExpTableMapperTest.WriteArchive(path, "table/na/dungeonroom.xml", Encoding.UTF8.GetBytes(Rows));
            using var reader = new M2dReader(path);
            IReadOnlyDictionary<int, DungeonRoomMetadata> rooms = new TableMapper(reader, "en").ParseDungeonRoom().Entries;

            Assert.That(rooms.Keys, Is.EquivalentTo(new[] {1001, 23006003, 24090005}));
            Assert.That((rooms[23006003].ExpireTimeOut, rooms[23006003].DurationTick), Is.EqualTo((true, 900000)));
            Assert.That((rooms[24090005].ExpireTimeOut, rooms[24090005].DurationTick), Is.EqualTo((false, 300000)));
            Assert.That(rooms[1001].ExpireTimeOut, Is.False);
        } finally {
            FeatureLocaleFilter.Locale = locale;
            scratch.Delete(true);
        }
    }
}
