using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>One bad value in an original map script must not drop the whole script from the trigger cache (BT-88).</summary>
public class TriggerParseTest {
    private static IAction ParseAction(string actionXml) {
        var document = new XmlDocument();
        document.LoadXml($"""<ms2><state name="Start"><onEnter>{actionXml}</onEnter></state></ms2>""");
        HTrigger trigger = TriggerCache.ParseTrigger("test_map", "test_trigger", document);
        return trigger.States[0].Enter!.Actions.Single();
    }

    // The align a parsed action carries: the primary constructor keeps it in a private field.
    private static Align AlignOf(IAction action) =>
        (Align) action.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(Align)).GetValue(action)!;

    [TestCase("All")]
    [TestCase("ALL")]
    [TestCase("all")]
    public void ParseIntArray_ReadsAllInAnyCase(string value) {
        Assert.That(TriggerFunctionMapping.ParseIntArray(value), Is.EqualTo(new[] { -1 }));
    }

    // Real trigger 52010055/main (Sky Fortress in Crisis) writes align="Reft" on its show_caption actions.
    [TestCase("""<action name="show_caption" type="VerticalCaption" title="t" desc="d" align="Reft" offset_rate_x="0.5" offset_rate_y="0.5" duration="3000" scale="1" />""", Align.center)]
    [TestCase("""<action name="show_caption" type="VerticalCaption" title="t" desc="d" align="left" offset_rate_x="0.5" offset_rate_y="0.5" duration="3000" scale="1" />""", Align.left)]
    [TestCase("""<action name="set_dialogue" type="1" spawn_id="101" script="s" time="3" align="Reft" />""", Align.center)]
    public void UnknownAlign_FallsBackToCenterAndKnownAlignParses(string actionXml, Align expected) {
        Assert.That(AlignOf(ParseAction(actionXml)), Is.EqualTo(expected));
    }

    // Real triggers 52100043/ending and 52100060/main write spawn_ids="All".
    [Test]
    public void DestroyMonsterWithAllSpawnIds_Parses() {
        IAction action = ParseAction("""<action name="destroy_monster" spawn_ids="All" />""");
        Assert.That(action, Is.TypeOf<HTrigger.DestroyMonster>());
    }
}
