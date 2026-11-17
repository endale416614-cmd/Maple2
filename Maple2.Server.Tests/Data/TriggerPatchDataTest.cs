using System.IO;
using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>PI-11 (BT-259) and PI-14 (BT-160): the two map script patches of offline-triggers.json apply to the real scripts exactly once.</summary>
[Category("Data")]
public class TriggerPatchDataTest {
    private SqliteConnection connection = null!;

    [SetUp]
    public void SetUp() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
    }

    [TearDown]
    public void TearDown() {
        connection?.Dispose();
    }

    private string Script(string xBlock, string name) {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "select Xml from trigger where MapXBlock = $x and Name = $n";
        command.Parameters.AddWithValue("$x", xBlock);
        command.Parameters.AddWithValue("$n", name);
        return (string) command.ExecuteScalar()!;
    }

    private static OfflineTriggers.Config Shipped() => OfflineTriggers.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "offline-triggers.json")))!;

    private static Trigger Parse(string xBlock, string name, string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger(xBlock, name, document);
    }

    private static int Occurrences(string text, string find) => text.Split(find).Length - 1;

    // Moonlight Fortress: the ride's last wait goes back to ResetShip00, so the ship is disabled, hidden and shown at the dock again.
    [Test]
    public void MoonlightFortressShipComesBackForEveryRide() {
        string xml = Script("02000318_bf", "shipmove_02");
        OfflineTriggers.Patch patch = Shipped().Patches.Single(patch => patch.XBlock == "02000318_bf" && patch.Trigger == "shipmove_02");
        Assert.That(Occurrences(xml, patch.Find), Is.EqualTo(1));

        string Wait24s(Trigger trigger) => trigger.States.Single(s => s.Name == "ResetShip02").Conditions.Single(c => c.Name == "wait_tick").NextState!;

        Assert.That(Wait24s(Parse("02000318_bf", "shipmove_02", xml)), Is.EqualTo("ResetShip01"));
        string patched = OfflineTriggers.Apply("02000318_bf", "shipmove_02", xml, Shipped());
        Trigger trigger = Parse("02000318_bf", "shipmove_02", patched);
        Assert.That(Wait24s(trigger), Is.EqualTo("ResetShip00"));
        Assert.That(trigger.States.Single(s => s.Name == "ResetShip00").Conditions.Select(c => c.NextState), Is.EqualTo(new[] { "ResetShip01" }));
        Assert.That(trigger.States.Single(s => s.Name == "ResetShip01").Conditions.Count, Is.GreaterThan(0));
    }

    // The Assassin's Past: spawn 114 (Fallen Man) is placed without the Dead_B loop; the Child (115) is still placed after him.
    [Test]
    public void FallenManIsPlacedWithoutTheDeathPoseLoop() {
        string xml = Script("52000159_qd", "52000159");
        OfflineTriggers.Patch patch = Shipped().Patches.Single(patch => patch.XBlock == "52000159_qd" && patch.Trigger == "52000159");
        Assert.That(Occurrences(xml, patch.Find), Is.EqualTo(1));
        Assert.That(Occurrences(xml, "spawn_id=\"114\""), Is.EqualTo(1));

        string patched = OfflineTriggers.Apply("52000159_qd", "52000159", xml, Shipped());
        Assert.That(patched.Contains("spawn_id=\"114\""), Is.False);
        Assert.That(patched.Length, Is.EqualTo(xml.Length - patch.Find.Length));

        Trigger.State Death04(string text) => Parse("52000159_qd", "52000159", text).States.Single(s => s.Name == "남자의죽음_04");
        Assert.That(Death04(patched).Enter!.Actions.Count, Is.EqualTo(Death04(xml).Enter!.Actions.Count - 1));
        Assert.That(patched.Contains("<action name=\"spawn_monster\" spawn_ids=\"114\""), Is.True);
        Assert.That(patched.Contains("<action name=\"spawn_monster\" spawn_ids=\"115\""), Is.True);
    }
}
