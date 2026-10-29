using System.IO;
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
        Assert.That(config.Patches.Length, Is.EqualTo(2));
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
}
