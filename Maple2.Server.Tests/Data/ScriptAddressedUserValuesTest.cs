using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Trigger;
using Maple2.Tools;
using Maple2.Tools.Scheduler;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>Keeps TriggerContext.ScriptAddressedUserValueMaps honest: on a listed map every write reaches a script that reads the key,
/// and no AI node or NPC script function writes those keys (they stay field-wide, where a script's own value would hide them).</summary>
[Category("Data")]
[NonParallelizable]
public class ScriptAddressedUserValuesTest {
    [Test]
    public void ListedMapsDeliverEveryWrite() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        string aiNodes = AllText(connection, "SELECT Reserved || Battle || BattleEnd || AiPresets FROM ai");
        string scriptFunctions = AllText(connection, "SELECT \"Table\" FROM \"server-table\" WHERE Name = '*scriptFunction.xml'");

        foreach (int mapId in TriggerContext.ScriptAddressedUserValueMaps) {
            string xBlock = context.MapMetadata.Single(map => map.Id == mapId).XBlock;
            Dictionary<int, string> models = context.MapEntity.Where(entity => entity.XBlock == xBlock).AsEnumerable()
                .Select(entity => entity.Block).OfType<TriggerModel>()
                .ToDictionary(model => model.Id, model => model.Name.ToLowerInvariant());
            Dictionary<string, XElement> scripts = context.TriggerMetadata.Where(trigger => trigger.MapXBlock == xBlock).AsEnumerable()
                .Where(trigger => models.ContainsValue(trigger.Name.ToLowerInvariant()))
                .ToDictionary(trigger => trigger.Name.ToLowerInvariant(), trigger => XDocument.Parse(trigger.Xml).Root!);
            Assert.That(scripts, Is.Not.Empty, xBlock);

            var keys = new HashSet<string>();
            foreach ((string name, XElement script) in scripts) {
                foreach (XElement write in script.Descendants("action").Where(action => (string?) action.Attribute("name") == "set_user_value")) {
                    string key = (string) write.Attribute("key")!;
                    int.TryParse((string?) write.Attribute("trigger_id"), out int target);
                    Assert.That(target == 0 || models.ContainsKey(target), Is.True, $"{xBlock} {name}: {key} goes to trigger {target}, which no model has");
                    string reader = target == 0 ? name : models[target];
                    Assert.That(Reads(scripts[reader], key), Is.True, $"{xBlock} {name}: {key} goes to {reader}, which never reads it");
                    keys.Add(key);
                }
            }
            foreach (string key in keys) {
                Assert.That(aiNodes.Contains($"\"Key\":\"{key}\""), Is.False, $"{xBlock}: an AI node writes {key} field-wide");
                Assert.That(scriptFunctions.Contains($"\"SetTriggerValueKey\":\"{key}\""), Is.False, $"{xBlock}: an NPC script function writes {key} field-wide");
            }
        }
    }

    private static bool Reads(XElement script, string key) => script.Descendants("condition")
        .Any(condition => (string?) condition.Attribute("name") == "user_value" && (string?) condition.Attribute("key") == key);

    private static string AllText(SqliteConnection connection, string sql) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var text = new StringBuilder();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) {
            text.Append(reader.GetString(0));
        }
        return text.ToString();
    }
}

/// <summary>TriggerContext.SetUserValue's routing and the own-value read in TriggerContext.UserValue, on bare objects (no database, no running field),
/// and the values a Kritias puzzle's time event writes and wait_seconds_user_value reads (FT-10b).
/// A fixture of its own: the Data category above would skip it where maple-data.db is missing.</summary>
public class RoutingScriptAddressedUserValuesTest {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ListedMap = 2000378; // Dais Maze
    private const int UnlistedMap = 2000001;

    [Test]
    public void SetUserValue_RoutesToTheAddressedScript_OnListedMapsOnly() {
        Assert.That(TriggerContext.ScriptAddressedUserValueMaps, Does.Contain(ListedMap));
        Assert.That(TriggerContext.ScriptAddressedUserValueMaps, Does.Not.Contain(UnlistedMap));

        (FieldManager field, TriggerContext a, TriggerContext b) = Build(ListedMap);

        // trigger_id names the reading script: b sees it, the writer does not.
        a.SetUserValue(2, "FindWay", 1);
        Assert.That(b.UserValue("FindWay", 1, false), Is.True);
        Assert.That(a.UserValue("FindWay", 1, false), Is.False);
        Assert.That(field.UserValues, Is.Empty);

        // No trigger_id (0) names the writer itself.
        a.SetUserValue(0, "WaveTime", 3);
        Assert.That(a.UserValue("WaveTime", 3, false), Is.True);
        Assert.That(b.UserValue("WaveTime", 3, false), Is.False);
        Assert.That(field.UserValues, Is.Empty);

        // A trigger_id no script has falls through to the field-wide value, which every script reads.
        a.SetUserValue(99, "Stray", 5);
        Assert.That(a.UserValue("Stray", 5, false), Is.True);
        Assert.That(b.UserValue("Stray", 5, false), Is.True);
        Assert.That(field.UserValues["Stray"], Is.EqualTo(5));

        // A map that is not listed keeps one field-wide value per key, whatever trigger_id says.
        (FieldManager plainField, TriggerContext plainA, TriggerContext plainB) = Build(UnlistedMap);
        plainA.SetUserValue(2, "K", 1);
        Assert.That(plainA.UserValue("K", 1, false), Is.True);
        Assert.That(plainB.UserValue("K", 1, false), Is.True);
        Assert.That(plainField.UserValues["K"], Is.EqualTo(1));
        Assert.That(Own(plainA), Is.Empty);
        Assert.That(Own(plainB), Is.Empty);
    }

    // FT-10b: wait_seconds_user_value waits, from the state's start, the seconds its key holds; a script's own value comes first, as in
    // UserValue, and a key nobody set reads 0.
    [Test]
    public void WaitSecondsUserValue_WaitsTheSecondsItsKeyHolds() {
        (FieldManager field, TriggerContext a, TriggerContext b) = Build(ListedMap);
        a.StartTick = b.StartTick = Environment.TickCount64 - 2000; // both states began 2 s ago

        Assert.That(a.WaitSecondsUserValue("TimeEventLifeTime", ""), Is.True, "a key nobody set: 0 s");
        field.UserValues["TimeEventLifeTime"] = 1;
        Assert.That(a.WaitSecondsUserValue("TimeEventLifeTime", ""), Is.True, "1 s, over");
        field.UserValues["TimeEventLifeTime"] = 600;
        Assert.That(a.WaitSecondsUserValue("TimeEventLifeTime", ""), Is.False, "600 s, not over");
        a.SetUserValue(0, "TimeEventLifeTime", 1);
        Assert.That(a.WaitSecondsUserValue("TimeEventLifeTime", ""), Is.True, "the script's own 1 s");
        Assert.That(b.WaitSecondsUserValue("TimeEventLifeTime", ""), Is.False, "the field-wide 600 s");
    }

    // FT-10b: a Kritias puzzle row (time-event type 6) gives the script it names on its map the row's key, and in TimeEventLifeTime the
    // seconds of its life that are left: the field's scheduler pauses while nobody is there, so a run can come late.
    [Test]
    [NonParallelizable] // GameClock is process-wide.
    public void TriggerValueEvent_GivesTheNamedScriptItsKeyAndTheLifeLeft() {
        var row = new TriggerValueEventMetadata(100201, 100, new DateTime(2014, 1, 1, 5, 0, 0), new DateTime(2099, 12, 31, 12, 0, 0),
            TimeSpan.FromMinutes(15), TimeSpan.Zero, TimeSpan.FromMinutes(10), [UnlistedMap], 2, "EventStart", 1);
        TriggerValueEventMetadata[] rows = [row, row with { Id = 100202, TargetMapIds = [ListedMap], Key = "OtherMap" }, row with { Id = 100203, TriggerId = 3, Key = "NoScript" }];
        var now = new DateTime(2030, 1, 1, 5, 30, 0, DateTimeKind.Local); // 05:00 plus whole cycles: a run is due
        using IDisposable clock = GameClock.Override(() => new DateTimeOffset(now));

        Dictionary<string, int> Run(TimeSpan late) {
            (FieldManager field, _, _) = Build(UnlistedMap); // b is trigger 2
            var storage = Bare<ServerTableMetadataStorage>();
            var table = new TimeEventTable(new Dictionary<int, GlobalPortalMetadata>(), new Dictionary<int, WorldBossMetadata>(), TriggerValue: rows.ToDictionary(item => item.Id));
            SetField(storage, "timeEventTable", new Lazy<TimeEventTable>(() => table));
            SetField(field, "<ServerTableMetadata>k__BackingField", storage);
            var scheduler = new EventQueue(Serilog.Log.Logger);
            SetField(field, "Scheduler", scheduler);

            typeof(FieldManager).GetMethod("StartTriggerValueEvents", Any)!.Invoke(field, null);
            now += late;
            scheduler.Start();
            scheduler.InvokeAll();
            now -= late;
            Assert.That(scheduler.Count, Is.EqualTo(1), "the next run is scheduled");
            return field.UserValues;
        }

        Assert.That(Run(TimeSpan.FromMinutes(4)), Is.EquivalentTo(new Dictionary<string, int> { ["TimeEventLifeTime"] = 360, ["EventStart"] = 1 }),
            "4 min late: 6 of the 10 min are left");
        Assert.That(Run(TimeSpan.FromMinutes(11)), Is.Empty, "11 min late: the life is over");
    }

    // Two scripts, a (trigger model id 1) and b (id 2), on one field.
    private static (FieldManager, TriggerContext, TriggerContext) Build(int mapId) {
        var field = Bare<FieldManager>();
        SetField(field, "<MapId>k__BackingField", mapId);
        SetField(field, "UserValues", new Dictionary<string, int>());
        var triggers = new ConcurrentDictionary<string, FieldTrigger>();
        SetField(field, "fieldTriggers", triggers);
        TriggerContext a = Script(field, triggers, 1, "a");
        TriggerContext b = Script(field, triggers, 2, "b");
        return (field, a, b);
    }

    private static TriggerContext Script(FieldManager field, ConcurrentDictionary<string, FieldTrigger> triggers, int id, string name) {
        var trigger = Bare<FieldTrigger>();
        SetField(trigger, "<Field>k__BackingField", field);
        SetField(trigger, "<Value>k__BackingField", new TriggerModel(id, name, Vector3.Zero, Vector3.Zero));
        var context = Bare<TriggerContext>();
        SetField(context, "owner", trigger);
        SetField(context, "scriptUserValues", new Dictionary<string, int>());
        SetField(trigger, "Context", context);
        triggers[name] = trigger;
        return context;
    }

    private static Dictionary<string, int> Own(TriggerContext context) {
        FieldInfo field = typeof(TriggerContext).GetField("scriptUserValues", Any) ?? throw new MissingFieldException(nameof(TriggerContext), "scriptUserValues");
        return (Dictionary<string, int>) field.GetValue(context)!;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }
}
