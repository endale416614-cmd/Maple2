using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// PE-03 (BT-224): pet fusion. Golden: pet_fusion.msb (GMS build 12) fuses Balrogs 61000079 (rarity 3, level 1, exp 0, level limit 50)
/// into Guard MK II 61000160 (rarity 3, level 12, exp 20416, level limit 70): +15000 exp each, FusionCount 1, 3, 5, and 95416 exp at
/// level 12 with no LevelUp.
/// </summary>
public class PetFusionTest {
    private const int DailyMax = 24; // DailyPetEnchantMaxCount
    private const long GuardUid = 0x266d9840b354340c;

    [Test]
    public void AFodderPet_GivesTheCapturedExp() {
        Item guard = Pet(61000160, rarity: 3, limit: 70, level: 12, exp: 20416);

        Assert.That(PetFusion.Fodder(guard, Pet(61000079, rarity: 3, limit: 50), 1, 0, DailyMax), Is.EqualTo(new PetFusion.Gain(15000, 10000, 1)),
            "12000 and the 3000 bonus; the capture paid 5000 under a 50% fusion cost sale");
        Assert.That(PetFusion.Fodder(guard, Pet(61000079, rarity: 3, limit: 50), 1, 24, DailyMax), Is.EqualTo(new PetFusion.Gain(11733, 10000, 1)),
            "the 25th fusion of the day: no bonus, and the base falls toward 20% at 60");
        Assert.That(PetFusion.Fodder(guard, Pet(61000079, rarity: 3, limit: 50), 1, 59, DailyMax)!.Value.Exp, Is.EqualTo(2400));
        Assert.That(PetFusion.Fodder(guard, Pet(61000079, rarity: 3, limit: 50, level: 3, exp: 1000), 1, 0, DailyMax), Is.EqualTo(new PetFusion.Gain(159800, 110000, 1)),
            "80% of a levelled fodder's exp: 12000 + 0.8 x (2 x 90000 + 1000) + 3000, for 10000 + 2 x 50000 mesos");
    }

    [Test]
    public void AFusionStone_GivesItsExpPerStone() {
        Item guard = Pet(61000160, rarity: 3, limit: 70, level: 12, exp: 20416);
        var polished = new Item(Meta(30000917, 15, 1, new ItemMetadataFunction(ItemFunction.AddPetEXP, "AddPetEXP", "15000", false)), 3, 5);

        Assert.That(PetFusion.Fodder(guard, polished, 3, 0, DailyMax), Is.EqualTo(new PetFusion.Gain(45000, 30000, 0)), "3 Polished Fusion Stones");
        Assert.That(PetFusion.Fodder(guard, new Item(Meta(30000001, 15, 1, null)), 1, 0, DailyMax), Is.Null, "neither a pet nor a Fusion Stone");
    }

    [Test]
    public void APetLevelsAtPetExpThresholds() {
        Item guard = Pet(61000160, rarity: 3, limit: 70, level: 12, exp: 20416);

        Assert.That(PetFusion.AddExp(guard, 75000, Table(), 50), Is.EqualTo(0), "202500 (90000 x 2.25) finishes level 12 at level limit 70");
        Assert.That((guard.Pet!.Level, guard.Pet.Exp), Is.EqualTo(((short) 12, 95416L)));
        Assert.That(PetFusion.AddExp(guard, 107084 + 202500 + 5, Table(), 50), Is.EqualTo(2));
        Assert.That((guard.Pet.Level, guard.Pet.Exp), Is.EqualTo(((short) 14, 5L)), "the rest carries over");

        Item balrog = Pet(61000079, rarity: 3, limit: 50, level: 49);
        Assert.That(PetFusion.AddExp(balrog, 3 * 90000, Table(), 50), Is.EqualTo(1));
        Assert.That((balrog.Pet!.Level, balrog.Pet.Exp), Is.EqualTo(((short) 50, 180000L)), "PetMaxLevel 50 is the last");
    }

    [Test]
    public void TheRepliesAreTheCaptured() {
        Item guard = Pet(61000160, rarity: 3, limit: 70, level: 12, exp: 35416, uid: GuardUid);

        Assert.That(Hex(PetPacket.Fusion(0x004dd005, guard)), Is.EqualTo("0A" + "05D04D00" + "588A000000000000" + "0C3454B340986D26"));
        Assert.That(Hex(PetPacket.FusionCount(1)), Is.EqualTo("0C" + "01000000"));
        guard.Pet!.Level = 13;
        Assert.That(Hex(PetPacket.LevelUp(0x004dd005, guard)), Is.EqualTo("0B" + "05D04D00" + "0D000000" + "0C3454B340986D26"));
    }

    private static string Hex(ByteWriter packet) => System.Convert.ToHexString(packet.Buffer[2..packet.Length]);

    private static Item Pet(int id, int rarity, int limit, short level = 1, long exp = 0, long uid = 0) {
        var pet = new Item(Meta(id, 11, limit, null), rarity) { Uid = uid };
        pet.Pet!.Level = level;
        pet.Pet.Exp = exp;
        return pet;
    }

    private static ItemMetadata Meta(int id, int type, int limit, ItemMetadataFunction? function) {
        var property = new ItemMetadataProperty(false, 0, type == 11 ? 1 : 9999, type, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limits = new ItemMetadataLimit(Gender.All, limit, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        return new ItemMetadata(id, $"{id}", [], string.Empty, [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limits, null, function, [], null, null, null, null);
    }

    // petexp.xml: levels 1 to 49 at normal 22000, rare 55000, elite 90000, excellent 90000, legendary 90000, artifact 0; level 50 all 0.
    private static PetExpTable Table() => new(Enumerable.Range(1, 50).ToDictionary(level => level, level => level < 50 ? new long[] {22000, 55000, 90000, 90000, 90000, 0} : new long[6]));
}
