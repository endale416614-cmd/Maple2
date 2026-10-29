using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Maple2.Tools;
using Microsoft.Data.Sqlite;

namespace Maple2.Server.Tests.Offline;

/// <summary>HS-02 and SV-01: what function cubes do that the ingest drops (client object XML, in summon-npcs.json): an
/// assistant's cube spawns her, and a souvenir gives its buff on every use.</summary>
public class AssistantSouvenirTest {
    private const int MomoCube = 80000000; // Momo 50800001
    private const int MedalCube = 50710002; // Imperial Medal 50710002

    private static FunctionCubeMetadata Cube(int id, InteractCubeControlType control, InteractCubeState defaultState, int[] autoStateChange, int time) =>
        new(id, 0, ConfigurableCubeType.None, defaultState, control, autoStateChange, time, null);

    // Rows of the shipped function-cube table
    private static readonly FunctionCubeMetadata Medal = Cube(MedalCube, InteractCubeControlType.Switch, InteractCubeState.None, [1, 0], 100);
    private static readonly FunctionCubeMetadata Momo = Cube(MomoCube, InteractCubeControlType.SpawnNPC, InteractCubeState.InUse, [], 0);
    private static readonly FunctionCubeMetadata StrawDoll = Cube(50900001, InteractCubeControlType.SpawnNPC, InteractCubeState.InUse, [], 0);
    private static readonly FunctionCubeMetadata RicePaddy = Cube(50400191, InteractCubeControlType.Farming, InteractCubeState.InUse, [1, 0], 1000);

    private static ItemMetadata Furnishing(int id, HousingCategory category) {
        var property = new ItemMetadataProperty(false, 0, 1, 6, 5, "Maid", string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit,
            null, null, [], null, null, new ItemMetadataHousing(0, 1, 1, category, true), new ItemMetadataInstall(false, 0, 0, false, MapAttribute.none));
    }

    // A field without a server: FieldTick only (Broadcast does nothing before Init).
    private static FieldManager Field(long tick) {
        var field = (FieldManager) RuntimeHelpers.GetUninitializedObject(typeof(FieldManager));
        GC.SuppressFinalize(field);
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldTick))!.SetValue(field, tick);
        return field;
    }

    private static NpcSummons.Config Shipped() => NpcSummons.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "summon-npcs.json")))!;

    [Test]
    public void Parse_WithCubeSpawnsAndBuffs() {
        var config = NpcSummons.Parse("""{"Summons": {}, "CubeSpawns": {"80000000": 11000512}, "CubeBuffs": {"50710002": [90000143, 1]}}""");
        Assert.That(config!.CubeSpawns![MomoCube], Is.EqualTo(11000512));
        Assert.That(config.CubeBuffs![MedalCube], Is.EqualTo(new[] { 90000143, 1 }));
        Assert.That(NpcSummons.Parse("""{"Summons": {}}""")!.CubeSpawns, Is.Null, "a file without them still loads");
    }

    [Test]
    public void ShippedFile_SpawnsTheAssistantsAndGivesTheSouvenirBuffs() {
        NpcSummons.Config config = Shipped();
        Assert.That(config.CubeSpawns, Has.Count.EqualTo(47), "every SpawnNPC cube of the client object XML");
        Assert.That(Enumerable.Range(MomoCube, 36).Where(config.CubeSpawns!.ContainsKey).Count(), Is.EqualTo(36), "the 36 assistants");
        Assert.That(config.CubeSpawns[MomoCube], Is.EqualTo(11000512), "Momo");
        Assert.That(config.CubeBuffs, Has.Count.EqualTo(29), "every cube with a firstAdditionalEffect");
        Assert.That(config.CubeBuffs![MedalCube], Is.EqualTo(new[] { 90000143, 1 }), "the Imperial Medal: +2 to every primary stat for 1 h");
    }

    [Test]
    public void OnlyAnAssistantsCubeSpawnsItsNpc() {
        PlotCube Placed(HousingCategory category, FunctionCubeMetadata? cube) =>
            new(Furnishing(50800001, category)) { Type = PlotCube.CubeType.Construction, Interact = cube is null ? null : new InteractCube(new Vector3B(1, 2, 3), cube) };
        Assert.That(NpcSummons.AssistantNpcId(Placed(HousingCategory.Maid, Momo)), Is.EqualTo(11000512));
        Assert.That(NpcSummons.AssistantNpcId(Placed(HousingCategory.Misc, StrawDoll)), Is.Null, "the Straw Doll's cube names a dummy that fights");
        Assert.That(NpcSummons.AssistantNpcId(Placed(HousingCategory.Maid, null)), Is.Null, "a placed item without a function cube");
    }

    [Test]
    public void UsedSouvenir_GoesBackToItsAutoStateChangeTarget() {
        var medal = new FieldFunctionInteract(Field(1000), 1, new InteractCube(new Vector3B(0, 0, 0), Medal), 0);
        medal.InteractCube.State = InteractCubeState.InUse;
        medal.Update(1100);
        Assert.That(medal.InteractCube.State, Is.EqualTo(InteractCubeState.None), "1-0: back to 0, off, so the next use switches it on");
    }

    [Test]
    public void UsedFarm_StillGoesBackToAvailable() {
        var paddy = new FieldFunctionInteract(Field(1000), 1, new InteractCube(new Vector3B(0, 0, 0), RicePaddy), 0);
        Assert.That(paddy.Use(), Is.False, "a farm starts growing (its default state 1)");
        paddy.Update(2000);
        Assert.That(paddy.InteractCube.State, Is.EqualTo(InteractCubeState.Available), "1-C, which the ingest reads as [1, 0]: ready to gather");
        Assert.That(paddy.Use(), Is.True);
    }

    [Test]
    public void EveryUseOfTheSouvenirSwitchesItOn() {
        FieldManager field = Field(1000);
        var medal = new FieldFunctionInteract(field, 1, new InteractCube(new Vector3B(0, 0, 0), Medal), 0);
        Assert.That(medal.Switch(7), Is.True, "on: the handler gives the buff");
        Assert.That(medal.InteractCube.InteractingCharacterId, Is.EqualTo(7));
        medal.Update(1099);
        Assert.That(medal.InteractCube.State, Is.EqualTo(InteractCubeState.InUse), "the timer runs 100 ms from the use");
        medal.Update(1100);
        typeof(FieldManager).GetProperty(nameof(FieldManager.FieldTick))!.SetValue(field, 5000L);
        Assert.That(medal.Switch(8), Is.True, "on again, so the buff again");
        medal.Update(5099);
        Assert.That(medal.InteractCube.State, Is.EqualTo(InteractCubeState.InUse), "the second use restarts the timer");
        Assert.That(NpcSummons.CubeBuff(MedalCube), Is.EqualTo(new[] { 90000143, 1 }));
    }

    [Test]
    [Category("Data")]
    [NonParallelizable]
    public void ShippedCatalog_SellsOnlyAssistantsTheDataHas() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        string catalogPath = Environment.GetEnvironmentVariable("OFFLINE_MERET_CATALOG_FILE") ?? Path.Combine(Paths.SOLUTION_DIR, "offline-meret-catalog.json");
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        HashSet<int> sold = catalog.RootElement.GetProperty("Entries").EnumerateArray().Select(entry => entry.GetProperty("ItemId").GetInt32()).ToHashSet();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        var npcs = new HashSet<int>();
        var assistantCubes = new Dictionary<int, int>(); // assistant item -> its function cube
        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "select Id from npc";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                npcs.Add(reader.GetInt32(0));
            }
        }
        using (SqliteCommand command = connection.CreateCommand()) {
            command.CommandText = "select Id, Housing, Install from item where Housing like '%\"HousingCategory\":91%'";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read()) {
                using JsonDocument housing = JsonDocument.Parse(reader.GetString(1));
                using JsonDocument install = JsonDocument.Parse(reader.GetString(2));
                if (housing.RootElement.GetProperty("HousingCategory").GetInt32() == (int) HousingCategory.Maid) {
                    assistantCubes[reader.GetInt32(0)] = install.RootElement.GetProperty("ObjectCubeId").GetInt32();
                }
            }
        }

        Dictionary<int, int> spawns = Shipped().CubeSpawns!;
        int[] withoutNpc = assistantCubes.Where(pair => !npcs.Contains(spawns.GetValueOrDefault(pair.Value))).Select(pair => pair.Key).Order().ToArray();
        Assert.That(assistantCubes, Has.Count.EqualTo(36));
        Assert.That(withoutNpc, Is.EqualTo(new[] { 50800002, 50800011, 50800012, 50800031, 50800032, 50800033, 50800034, 50800035, 50800036 }),
            "the data has no NPC for 9 assistants");
        Assert.That(assistantCubes.Keys.Where(sold.Contains).Intersect(withoutNpc), Is.Empty, "the Meret Market sells none of them");
        Assert.That(assistantCubes.Keys.Count(sold.Contains), Is.EqualTo(27), "and every other one");
    }
}
