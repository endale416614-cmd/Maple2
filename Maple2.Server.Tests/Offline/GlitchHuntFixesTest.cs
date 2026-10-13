using System.Linq;
using System.Xml;
using Maple2.Model.Game.Dungeon;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Util;
using Maple2.Server.Game.Trigger.Helpers;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>Defects found by the test bot sweeps (docs\audit\GLITCHES.md).</summary>
public class GlitchHuntFixesTest {
    private static HTrigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test_map", "test_trigger", document);
    }

    // Real trigger 02000348_bf/main, state "클리어_04" (Splint Coal Mine round end; the Muros Studio hub waits for var 1).
    private const string RoundEndXml = """
        <ms2><state name="클리어_04"><onEnter><action name="dungeon_variable" value="1" var_id="1" /></onEnter></state></ms2>
        """;

    // Real trigger 02000380_bf/main, state "Start_09".
    private const string HubXml = """
        <ms2><state name="Start_09"><onEnter></onEnter><condition name="dungeon_variable" value="1" var_id="1"><transition state="Round_2" /></condition></state><state name="Round_2"><onEnter></onEnter></state></ms2>
        """;

    [Test]
    public void DungeonVariableAction_IsParsedNotDropped() {
        HTrigger trigger = Parse(RoundEndXml);
        IAction action = trigger.States[0].Enter!.Actions.Single();
        Assert.That(action, Is.TypeOf<HTrigger.SetDungeonVariable>());
    }

    [Test]
    public void DungeonVariableCondition_IsParsed() {
        HTrigger trigger = Parse(HubXml);
        ICondition condition = trigger.States[0].Conditions.Single();
        Assert.That(condition, Is.TypeOf<HTrigger.DungeonVariable>());
        Assert.That(condition.NextState, Is.EqualTo("Round_2"));
    }

    [Test]
    public void DungeonRoomRecord_StartsWithoutVariablesOrDarkStream() {
        var record = new DungeonRoomRecord(null!);
        Assert.That(record.Variables, Is.Empty);
        Assert.That(record.DarkStreamRounds, Is.EqualTo(0));
    }
}
