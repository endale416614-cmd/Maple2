extern alias ingest;

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using Maple2.Model.Common;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>FT-10a: the ingest keeps the Hidden Realm portals (timeEventData.xml type 2) and room_random.xml. Golden: their rows in Server.m2d.</summary>
public class HiddenRealmPortalMapperTest {
    // Rows of the real file: a portal of room group 1 on Ludibrium Crater, one of group 2 on Moonlight Desert's ant hell spawns, and the
    // test map's, which opens every minute.
    private const string TimeEvents = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <event id="10000" type="2" prob="80" startTime="2014-1-1-12-00-00" endTime="2099-12-31-12-00-00" cycleTime="0-0-0-0-7-0" randomTime="0-0-0-0-6-30" lifeTime="0-0-0-0-0-29" screenNotice="0" chatNotice="0" targetFields="02000014" noticeMap="0" targetSpawnTags="보너스맵" roomID="1" roomCount="1" />
            <event id="11001" type="2" prob="80" startTime="2014-1-1-12-00-00" endTime="2099-12-31-12-00-00" cycleTime="0-0-0-0-7-0" randomTime="0-0-0-0-6-30" lifeTime="0-0-0-0-0-29" screenNotice="0" chatNotice="0" targetFields="02010033" noticeMap="0" targetSpawnTags="개미지옥" roomID="2" roomCount="1" />
            <event id="12000" type="2" prob="100" startTime="2014-1-1-12-00-00" endTime="2099-12-31-12-00-00" cycleTime="0-0-0-0-1-0" randomTime="0-0-0-0-0-0" lifeTime="0-0-0-0-0-29" screenNotice="0" chatNotice="0" targetFields="99999970" noticeMap="0" targetSpawnTags="보너스맵" roomID="1" roomCount="1" />
        </ms2>
        """;

    // Two rows of each of the groups 1 and 2, as the real file has them.
    private const string Rooms = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <randomroom id="1" prob="10000">
                <ref roomid="15" weight="100" />
                <ref roomid="18" weight="50" />
            </randomroom>
            <randomroom id="2" prob="10000">
                <ref roomid="201" weight="100" />
                <ref roomid="202" weight="100" />
            </randomroom>
            <room id="15" fieldIDs="80000006" roomdurationTick="35000" nifAssetName="Eff_event_portal_A01" maxUserCount="1" autoClosing="1" />
            <room id="18" fieldIDs="80000008" roomdurationTick="30000" nifAssetName="Eff_event_portal_A01" maxUserCount="1" autoClosing="1" />
            <room id="201" fieldIDs="80000012" roomdurationTick="60000" nifAssetName="Eff_kr_sandswirl_01" maxUserCount="1" autoClosing="1" />
            <room id="202" fieldIDs="80000013" roomdurationTick="30000" nifAssetName="Eff_kr_sandswirl_01" maxUserCount="4" autoClosing="1" />
        </ms2>
        """;

    private static readonly RandomRoomTable.Room Room15 = new(15, 100, 80000006, 35000, "Eff_event_portal_A01");

    [Test]
    public void ParseTimeEventTable_KeepsTheHiddenRealmPortals() {
        TimeEventTable table = Read(ServerTableNames.TIME_EVENT, TimeEvents, mapper => mapper.ParseTimeEventTable());

        Assert.That(table.HiddenRealmPortal!.Keys, Is.EqualTo(new[] {10000, 11001, 12000}));
        HiddenRealmPortalMetadata portal = table.HiddenRealmPortal[10000];
        Assert.That((portal.Id, portal.Probability, portal.StartTime, portal.EndTime, portal.RoomId, portal.RoomCount),
            Is.EqualTo((10000, 80, new DateTime(2014, 1, 1, 12, 0, 0), new DateTime(2099, 12, 31, 12, 0, 0), 1, 1)));
        Assert.That((portal.CycleTime, portal.RandomTime, portal.LifeTime), Is.EqualTo((TimeSpan.FromMinutes(7), new TimeSpan(0, 6, 30), TimeSpan.FromSeconds(29))));
        Assert.That(portal.TargetMapIds, Is.EqualTo(new[] {2000014}));
        Assert.That(portal.SpawnTags, Is.EqualTo(new[] {"보너스맵"}));
        Assert.That((table.HiddenRealmPortal[11001].RoomId, table.HiddenRealmPortal[11001].SpawnTags.Single()), Is.EqualTo((2, "개미지옥")));
        Assert.That((table.HiddenRealmPortal[12000].Probability, table.HiddenRealmPortal[12000].CycleTime, table.HiddenRealmPortal[12000].RandomTime),
            Is.EqualTo((100, TimeSpan.FromMinutes(1), TimeSpan.Zero)));
        Assert.That(table.GlobalPortal.Count + table.WorldBoss.Count + table.FieldInteract!.Count, Is.Zero, "the other types stay apart");
    }

    [Test]
    public void ParseRandomRoomTable_KeepsEachGroupsRooms() {
        RandomRoomTable table = Read(ServerTableNames.ROOM_RANDOM, Rooms, mapper => mapper.ParseRandomRoomTable());

        Assert.That(table.Entries.Keys, Is.EqualTo(new[] {1, 2}));
        Assert.That(table.Entries[1].Probability, Is.EqualTo(10000));
        Assert.That(table.Entries[1].Rooms, Is.EqualTo(new[] {Room15, new RandomRoomTable.Room(18, 50, 80000008, 30000, "Eff_event_portal_A01")}));
        Assert.That(table.Entries[2].Rooms.Select(room => (room.Id, room.MapId, room.DurationTick, room.Model)),
            Is.EqualTo(new[] {(201, 80000012, 60000, "Eff_kr_sandswirl_01"), (202, 80000013, 30000, "Eff_kr_sandswirl_01")}));
    }

    // With the data's JSON options (PropertyBuilderExtensions): a zero RandomTime is not written and reads back as zero.
    [Test]
    public void BothTables_SurviveTheJsonRoundTrip() {
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };
        ServerTable portals = Read(ServerTableNames.TIME_EVENT, TimeEvents, mapper => mapper.ParseTimeEventTable());
        ServerTable rooms = Read(ServerTableNames.ROOM_RANDOM, Rooms, mapper => mapper.ParseRandomRoomTable());

        var readPortals = (TimeEventTable) JsonSerializer.Deserialize<ServerTable>(JsonSerializer.Serialize(portals, options), options)!;
        var readRooms = (RandomRoomTable) JsonSerializer.Deserialize<ServerTable>(JsonSerializer.Serialize(rooms, options), options)!;

        Assert.That(readPortals.HiddenRealmPortal![11001].SpawnTags, Is.EqualTo(new[] {"개미지옥"}));
        Assert.That((readPortals.HiddenRealmPortal[10000].LifeTime, readPortals.HiddenRealmPortal[12000].RandomTime), Is.EqualTo((TimeSpan.FromSeconds(29), TimeSpan.Zero)));
        Assert.That(readRooms.Entries[1].Rooms[0], Is.EqualTo(Room15));
    }

    // The ingest's reader on a one-entry Server.m2d.
    private static T Read<T>(string name, string rows, Func<ServerTableMapper, T> parse) {
        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            string path = Path.Combine(scratch.FullName, "Server.m2d");
            MonsterExpTableMapperTest.WriteArchive(path, $"table/Server/{name}", Encoding.UTF8.GetBytes(rows));
            using var reader = new M2dReader(path);
            return parse(new ServerTableMapper(reader));
        } finally {
            scratch.Delete(true);
        }
    }
}
