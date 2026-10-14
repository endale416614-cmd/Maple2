using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Items;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>Rarity of items created without one, and the EpicGear option.</summary>
public class ItemRarityTest {
    private static readonly ItemOptionConstantTable Constants = new(new Dictionary<int, IReadOnlyDictionary<int, ItemOptionConstant>>());
    private static readonly ItemOptionStaticTable Statics = new(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>>());

    private static ItemOptionRandomTable Randoms(int id, params int[] rarities) {
        var option = new ItemOption(1f, new ItemOption.Range<int>(1, 1), []);
        return new ItemOptionRandomTable(new Dictionary<int, IReadOnlyDictionary<int, ItemOption>> {
            [id] = rarities.ToDictionary(rarity => rarity, _ => option),
        });
    }

    private static ItemMetadataOption Option(int id, int constantId) {
        return new ItemMetadataOption(id, ItemOptionMakeType.Base, id, ItemOptionMakeType.Base, constantId, ItemOptionMakeType.Base, 50, 0);
    }

    [Test]
    public void DefaultRarity_GearTakesLowestOptionRarity() {
        // Murpagoth Warrior Dagger: option rows only at Epic, ConstantId is its own item id
        Assert.That(ItemDropManager.DefaultRarity(Option(13150288, 13150288), Constants, Statics, Randoms(13150288, 4)), Is.EqualTo(4));
        // field gear with rows Rare-Legendary
        Assert.That(ItemDropManager.DefaultRarity(Option(11200001, 11200001), Constants, Statics, Randoms(11200001, 2, 3, 4, 5)), Is.EqualTo(2));
    }

    [Test]
    public void DefaultRarity_KeepsConstantRarityAndNormalFallback() {
        // boxes and outfits keep their rarity in ConstantId
        Assert.That(ItemDropManager.DefaultRarity(Option(0, 4), Constants, Statics, Randoms(1)), Is.EqualTo(4));
        Assert.That(ItemDropManager.DefaultRarity(Option(11200012, 11200012), Constants, Statics, Randoms(1)), Is.EqualTo(1), "gear without option rows");
        Assert.That(ItemDropManager.DefaultRarity(null, Constants, Statics, Randoms(1)), Is.EqualTo(1));
    }

    [Test]
    public void EpicGear_RaisesGearOnly() {
        Assert.That(OfflineEpicGear.Rarity(13150288, false, 1, enabled: true), Is.EqualTo(4), "dagger");
        Assert.That(OfflineEpicGear.Rarity(11461148, false, 2, enabled: true), Is.EqualTo(4), "armor");
        Assert.That(OfflineEpicGear.Rarity(11960127, false, 1, enabled: true), Is.EqualTo(4), "necklace");
        Assert.That(OfflineEpicGear.Rarity(11300140, false, 6, enabled: true), Is.EqualTo(6), "Ascendant stays");
        Assert.That(OfflineEpicGear.Rarity(11620048, true, 1, enabled: true), Is.EqualTo(1), "outfit");
        Assert.That(OfflineEpicGear.Rarity(20302151, false, 1, enabled: true), Is.EqualTo(1), "box");
        Assert.That(OfflineEpicGear.Rarity(61000079, false, 3, enabled: true), Is.EqualTo(3), "pet");
        Assert.That(OfflineEpicGear.Rarity(13150288, false, 1, enabled: false), Is.EqualTo(1), "option off");
    }
}
