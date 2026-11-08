using System.IO;
using System.Linq;
using System.Xml;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class OfflineTriggersTest {
    private const string Condition = "<condition name=\"quest_user_detected\" box_ids=\"9900\" quest_ids=\"10001028\" quest_states=\"2\" />";

    private static OfflineTriggers.Config Shipped() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "offline-triggers.json");
        Assert.That(File.Exists(path), Is.True, path);
        OfflineTriggers.Config? config = OfflineTriggers.Parse(File.ReadAllText(path));
        Assert.That(config, Is.Not.Null);
        return config!;
    }

    [Test]
    public void Parse() {
        OfflineTriggers.Config config = Shipped();
        Assert.That(config.Patches.Length, Is.EqualTo(8));
        Assert.That(config.Patches[0].XBlock, Is.EqualTo("52000066_qd"));
        Assert.That(config.Patches[0].Trigger, Is.EqualTo("chase01"));
        Assert.That(config.Patches[0].Find, Is.EqualTo("quest_ids=\"10001028\""));
        Assert.That(config.Patches[0].Replace, Is.EqualTo("quest_ids=\"10001028,60100070\""));
        Assert.That(config.Patches[1].XBlock, Is.EqualTo("63000024_cs"));
        Assert.That(config.Patches[1].Trigger, Is.EqualTo("wakeup01"));
        Assert.That(config.Patches[1].Find, Is.EqualTo("<transition state=\"MoveToGetWeapon01\" />"));
        Assert.That(config.Patches[1].Replace, Is.EqualTo("<transition state=\"QuestStart01\" />"));
    }

    [Test]
    public void Apply() {
        Assert.That(OfflineTriggers.Apply("52000066_QD", "chase01", Condition, Shipped()), Does.Contain("quest_ids=\"10001028,60100070\""));
    }

    [Test]
    public void ApplyOtherTrigger() {
        Assert.That(OfflineTriggers.Apply("52000066_QD", "other_trigger", Condition, Shipped()), Is.EqualTo(Condition));
    }

    [Test]
    public void ApplyMissingFind() {
        string xml = Condition.Replace("10001028", "99999999"); // the patch's Find is absent
        Assert.That(OfflineTriggers.Apply("52000066_QD", "chase01", xml, Shipped()), Is.EqualTo(xml));
    }

    // Disciple's Room: after a relog with the first quest started, a Soul Binder's script goes to a state GMS's script lacks (BT-139).
    [Test]
    public void ApplyDisciplesRoom() {
        const string xml = "<condition name=\"wait_tick\" wait_tick=\"1000\"><transition state=\"MoveToGetWeapon01\" /></condition>";
        Assert.That(OfflineTriggers.Apply("63000024_cs", "WakeUp01", xml, Shipped()),
            Is.EqualTo("<condition name=\"wait_tick\" wait_tick=\"1000\"><transition state=\"QuestStart01\" /></condition>"));
    }

    // Murpagoth Temple: the healing ritual of 10003090 plays to its end; Global renamed its last two states but not their transitions (TR-11).
    [Test]
    public void ApplyHealingRitual() {
        const string states = "<state name=\"치유의식_05\"><condition name=\"wait_tick\" wait_tick=\"3000\"><transition state=\"의식종료_01\" /></condition></state>"
                              + "<state name=\"의식종료01\"><condition name=\"wait_tick\" wait_tick=\"1000\"><transition state=\"의식종료_02\" /></condition></state>"
                              + "<state name=\"의식종료02\"><condition name=\"wait_tick\" wait_tick=\"1000\"><transition state=\"종료\" /></condition></state><state name=\"종료\" />";
        var document = new XmlDocument();
        document.LoadXml($"<ms2>{OfflineTriggers.Apply("52010032_qd", "main", states, Shipped())}</ms2>");
        string?[] next = TriggerCache.ParseTrigger("52010032_qd", "main", document).States.SelectMany(state => state.Conditions).Select(condition => condition.NextState).ToArray();
        Assert.That(next, Is.EqualTo(new[] { "의식종료01", "의식종료02", "종료" }));
    }

    // Golden Tower 2F: the room's timer script moved the player out of the dungeon 5 seconds after arrival.
    [Test]
    public void ApplyGoldenTower2F() {
        const string state = "<state name=\"유저이동\" posX=\"746\" posY=\"534\"><onEnter><action name=\"move_user\" map_id=\"02000141\" portal_id=\"2\" /></onEnter><condition name=\"always\"><transition state=\"유저이동음성\" /></condition><onExit /></state>";
        Assert.That(OfflineTriggers.Apply("02000246_bf", "timer", state, Shipped()),
            Is.EqualTo("<state name=\"유저이동\" posX=\"746\" posY=\"534\"><onEnter></onEnter><onExit /></state>"));
    }

    // Crisis in Henesys: a wave also ends on a timeout, so a monster nobody can reach does not hold back the next wave.
    [TestCase("901,902,903,904,905,906,907,908,909,910", "Battle01End01", 1)]
    [TestCase("911,912,913,914", "Battle02End01", 1)]
    [TestCase("921,922,923,924,925,926,927,928", "Battle03End01", 0)] // Battle03End01 removes the wave itself
    public void ApplyHenesysWaveTimeout(string spawnIds, string endState, int timeoutActions) {
        string gate = $"<condition name=\"monster_dead\" spawn_ids=\"{spawnIds}\"><transition state=\"{endState}\" /></condition>";
        var document = new XmlDocument();
        document.LoadXml($"<ms2><state name=\"Gate\">{OfflineTriggers.Apply("52000120_qd", "01_henesysdefense", gate, Shipped())}</state></ms2>");
        ICondition[] conditions = TriggerCache.ParseTrigger("52000120_qd", "01_henesysdefense", document).States[0].Conditions.ToArray();
        Assert.That(conditions.Select(condition => condition.Name), Is.EqualTo(new[] { "monster_dead", "wait_tick" }));
        Assert.That(conditions[1].NextState, Is.EqualTo(endState));
        Assert.That(conditions[1].Actions.Count, Is.EqualTo(timeoutActions));
    }
}
