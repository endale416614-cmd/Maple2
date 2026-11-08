extern alias ingest;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using ingest::M2dXmlGenerator;
using ingest::Maple2.File.Ingest.Mapper;
using ingest::Maple2.File.IO;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Ingest;

/// <summary>FT-10b: the ingest keeps the Kritias field puzzles, time-event type 6, of the features the data build enables. Golden: rows
/// 100201 and 100205 of table/Server/timeEventData.xml of Server.m2d. Global's feature table has Kritias_2018_12 at NA 46, on for NA Live
/// (47), and Kritias02 at NA 99, off.</summary>
[NonParallelizable] // FeatureLocaleFilter is process-wide.
public class TimeEventTableMapperTest {
    private const string Rows = """
        <?xml version="1.0" encoding="utf-8"?>
        <ms2>
            <event id="100201" type="6" feature="Kritias_2018_12" prob="100" startTime="2014-1-1-05-00-00" endTime="2099-12-31-12-00-00" cycleTime="0-0-0-0-15-0" randomTime="0-0-0-0-0-0" lifeTime="0-0-0-0-10-00" individualChannelSpawn="0" variableCountByChannelCount="0.5" targetFields="02020001" triggerID="100002" key="EventStart" value="1" />
            <event id="100205" type="6" feature="Kritias02" prob="100" startTime="2014-1-1-05-00-00" endTime="2099-12-31-12-00-00" cycleTime="0-0-0-0-15-0" randomTime="0-0-0-0-0-0" lifeTime="0-0-0-0-10-00" individualChannelSpawn="0" variableCountByChannelCount="0.5" targetFields="02020068" triggerID="100002" key="EventStart" value="1" />
        </ms2>
        """;

    [Test]
    public void ParseTimeEventTable_KeepsThePuzzlesOfEnabledFeatures() {
        TimeEventTable table = Parse();

        Assert.That(table.TriggerValue?.Keys, Is.EqualTo(new[] {100201}), "Kritias02 is off for NA, as in every table the feature filter reads");
        TriggerValueEventMetadata row = table.TriggerValue![100201];
        Assert.That(row.Probability, Is.EqualTo(100));
        Assert.That(row.StartTime, Is.EqualTo(new DateTime(2014, 1, 1, 5, 0, 0)));
        Assert.That(row.EndTime, Is.EqualTo(new DateTime(2099, 12, 31, 12, 0, 0)));
        Assert.That(row.CycleTime, Is.EqualTo(TimeSpan.FromMinutes(15)));
        Assert.That(row.RandomTime, Is.EqualTo(TimeSpan.Zero));
        Assert.That(row.LifeTime, Is.EqualTo(TimeSpan.FromMinutes(10)));
        Assert.That(row.TargetMapIds, Is.EqualTo(new[] {2020001}));
        Assert.That((row.TriggerId, row.Key, row.Value), Is.EqualTo((100002, "EventStart", 1)));
    }

    [Test]
    public void TriggerValue_SurvivesTheJsonRoundTrip_AndOldDataHasNone() {
        ServerTable table = Parse();

        var read = (TimeEventTable) JsonSerializer.Deserialize<ServerTable>(JsonSerializer.Serialize(table))!;
        Assert.That(read.TriggerValue?[100201].Key, Is.EqualTo("EventStart"));
        Assert.That(read.TriggerValue![100201].TargetMapIds, Is.EqualTo(new[] {2020001}));

        // Data ingested before FT-10b (the DBs until RB-02) has no TriggerValue: the field then schedules nothing.
        var old = (TimeEventTable) JsonSerializer.Deserialize<ServerTable>("""{"!":"timeEvent","GlobalPortal":{},"WorldBoss":{}}""")!;
        Assert.That(old.TriggerValue, Is.Null);
    }

    // The ingest's locale and the feature as the data build enables it (Program.cs, Filter.Load), on a one-entry Server.m2d.
    private static TimeEventTable Parse() {
        string locale = FeatureLocaleFilter.Locale;
        bool added = FeatureLocaleFilter.Features.TryAdd("Kritias_2018_12", 46);
        DirectoryInfo scratch = Directory.CreateTempSubdirectory();
        try {
            FeatureLocaleFilter.Locale = "NA";
            string path = Path.Combine(scratch.FullName, "Server.m2d");
            MonsterExpTableMapperTest.WriteArchive(path, "table/Server/timeEventData.xml", Encoding.UTF8.GetBytes(Rows));
            using var reader = new M2dReader(path);
            return new ServerTableMapper(reader).ParseTimeEventTable();
        } finally {
            FeatureLocaleFilter.Locale = locale;
            if (added) {
                FeatureLocaleFilter.Features.Remove("Kritias_2018_12");
            }
            scratch.Delete(true);
        }
    }
}
