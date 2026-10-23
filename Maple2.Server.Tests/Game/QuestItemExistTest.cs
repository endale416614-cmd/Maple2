using NUnit.Framework;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class QuestItemExistTest {
    [Test]
    public void HeldCounter() {
        // Test cases from the task description
        Assert.That(QuestManager.HeldCounter(0, 3, 3), Is.EqualTo(3)); // Kono case at accept
        Assert.That(QuestManager.HeldCounter(0, 3, 5), Is.EqualTo(3));
        Assert.That(QuestManager.HeldCounter(0, 10, 4), Is.EqualTo(4));
        Assert.That(QuestManager.HeldCounter(4, 10, 5), Is.EqualTo(5)); // one more picked up
        Assert.That(QuestManager.HeldCounter(5, 10, 2), Is.EqualTo(5)); // never down
    }
}