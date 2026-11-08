using Maple2.Database.Storage;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Offline;

/// <summary>The open slot count is pure; OfflineCharacterSlots.Enabled reads the environment once, so it is not tested.</summary>
public class OfflineCharacterSlotsTest {
    [TestCase(4, 1, true, 15, TestName = "Adapted opens every slot of a new account")]
    [TestCase(8, 3, true, 15, TestName = "Adapted opens every slot after cards")]
    [TestCase(4, 1, false, 4, TestName = "Without the option a new account has its 4 slots")]
    [TestCase(9, 2, false, 9, TestName = "Without the option the cards count")]
    [TestCase(4, 9, false, 9, TestName = "Characters made with every slot open keep their rows")]
    [TestCase(20, 0, false, 15, TestName = "A saved count above the limit is capped")]
    public void Unlocked(int saved, int characters, bool enabled, int expected) {
        Assert.That(OfflineCharacterSlots.Unlocked(saved, characters, enabled), Is.EqualTo(expected));
    }

    [TestCase(4, 1, 5, TestName = "A card opens one more slot")]
    [TestCase(4, 9, 10, TestName = "A card opens one past the characters made with every slot open")]
    [TestCase(14, 3, 15, TestName = "A card never goes past the limit")]
    public void AfterCard(int saved, int characters, int expected) {
        Assert.That(OfflineCharacterSlots.AfterCard(saved, characters), Is.EqualTo(expected));
    }

    [Test]
    public void GameLimit() {
        Assert.That(Constant.ServerMaxCharacters, Is.EqualTo(15));
    }
}
