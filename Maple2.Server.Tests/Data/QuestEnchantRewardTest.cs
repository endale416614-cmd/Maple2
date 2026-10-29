using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Manager;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Data;

/// <summary>QE-01 (BT-43): a quest that gives gear for the enchants it asks also gives the Chaos Onyx our Ophelia cost asks for them.</summary>
[Category("Data")]
[NonParallelizable]
public class QuestEnchantRewardTest {
    private const int GirlsInterrupted = 90000610; // one successful enchant; accept reward: Drama Suit (rarity 3), 150 Onyx, 5 Crystal Fragments
    private const int Handicraft101 = 90000590; // its prerequisite, no enchant condition
    private const int DramaSuit = 12200353; // Lv 30 overall
    private const int Onyx = 40100023;
    private const int CrystalFragment = 40100001;
    private const int ChaosOnyx = 40100024; // constants ChaosOnixItemID

    private Dictionary<int, QuestMetadata> quests = [];
    private Dictionary<int, ItemMetadata> items = [];

    [OneTimeSetUp]
    public void Load() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        quests = context.QuestMetadata.AsNoTracking().ToDictionary(quest => quest.Id);
        List<int> rewardIds = quests.Values.SelectMany(quest => quest.AcceptReward.EssentialItem).Select(entry => entry.Id).Distinct().ToList();
        items = context.ItemMetadata.AsNoTracking().Where(item => rewardIds.Contains(item.Id)).ToDictionary(item => item.Id);
    }

    [Test]
    public void GirlsInterrupted_GivesTheChaosOnyxItsEnchantAsks() {
        QuestMetadata quest = quests[GirlsInterrupted];
        var suit = new Item(items[DramaSuit], 3);
        Assert.That(quest.AcceptReward.EssentialItem.Select(entry => entry.Id), Does.Not.Contain(ChaosOnyx)); // the original reward lists none
        Assert.That(Enchant.GetEnchantCost(suit).Where(cost => cost.Tag == ItemTag.ChaosOnix).Sum(cost => cost.Amount), Is.EqualTo(4)); // the cost stays
        Assert.That(QuestManager.EnchantChaosOnyx(quest, suit), Is.EqualTo(4));
    }

    [Test]
    public void OnlyEnchantableGear_ThatCostsChaosOnyx_ForAnEnchantQuest_Adds() {
        QuestMetadata quest = quests[GirlsInterrupted];
        Assert.That(QuestManager.EnchantChaosOnyx(quest, new Item(items[Onyx], 1, 150)), Is.EqualTo(0));
        Assert.That(QuestManager.EnchantChaosOnyx(quest, new Item(items[CrystalFragment], 1, 5)), Is.EqualTo(0));
        Assert.That(QuestManager.EnchantChaosOnyx(quest, new Item(items[DramaSuit], 1)), Is.EqualTo(0)); // a Normal suit's enchant asks none
        Assert.That(QuestManager.EnchantChaosOnyx(quests[Handicraft101], new Item(items[DramaSuit], 3)), Is.EqualTo(0));
    }

    [Test]
    public void NoOtherQuest_Adds() {
        List<int> adding = quests.Values
            .Where(quest => quest.AcceptReward.EssentialItem.Any(entry => items.TryGetValue(entry.Id, out ItemMetadata? item)
                                                                          && QuestManager.EnchantChaosOnyx(quest, new Item(item, entry.Rarity, entry.Amount)) > 0))
            .Select(quest => quest.Id)
            .ToList();
        Assert.That(adding, Is.EqualTo(new[] { GirlsInterrupted }));
    }
}
