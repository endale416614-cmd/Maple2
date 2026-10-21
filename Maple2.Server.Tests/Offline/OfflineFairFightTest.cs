using Maple2.Model.Enum;
using Maple2.Server.Game.Util;
using static Maple2.Server.Game.Util.OfflineFairFight;

namespace Maple2.Server.Tests.Offline;

/// <summary>RFF_DESIGN.md section 5 scenarios 3-6 and the worked table of section 3.2, on the plain-value rule.</summary>
public class OfflineFairFightTest {
    private const double BalrogHp = 42_196_479;   // Balrog 23090005, Temple of Immortals 20004002 (raid, 4-man)
    private const double DevorakHp = 646_587_248; // Devorak 23200016, Shadow Altar 23007003 (chaosRaid, timer 900 s)
    private static readonly Encounter Balrog = new(true, "raid", 20004002, 0, true, 3);
    private static readonly Encounter Devorak = new(true, "chaosRaid", 23007003, 900_000, true, 3);
    private static readonly Encounter LavaGolem = new(false, "", 0, 0, false, 1); // Class 1, open field
    private static readonly Encounter RaidMinion = new(true, "raid", 20004002, 0, false, 1);

    private static long Solo(Encounter encounter, double maxHp, long calculated, double rate = 1.0, int players = 1,
        bool invincible = false, bool shielded = false, bool enabled = true, DamageType type = DamageType.Normal) =>
        OfflineFairFight.Floor(enabled, players, invincible, shielded, encounter, maxHp, rate, type, calculated, Defaults);

    [Test]
    public void WorkedTable_Balrog_RaidSixHundredSeconds() {
        Assert.That(ClearSeconds(Balrog, Defaults), Is.EqualTo(600));
        Assert.That(FloorHit(BalrogHp, 600, 1.0, Defaults), Is.EqualTo(28_131).Within(1));
    }

    [Test]
    public void WorkedTable_Devorak_TimerCapsTheClearTime() {
        Assert.That(ClearSeconds(Devorak, Defaults), Is.EqualTo(720).Within(1e-9)); // min(900, 900 s * 0.8)
        Assert.That(FloorHit(DevorakHp, 720, 1.0, Defaults), Is.EqualTo(359_215).Within(1));
    }

    [Test]
    public void Scenario3_NormalMonstersInTheOpenField_NoFloor() {
        Assert.That(ClearSeconds(LavaGolem, Defaults), Is.Null);
        Assert.That(Solo(LavaGolem, 109_567, 100), Is.EqualTo(100));
    }

    [Test]
    public void OpenFieldBosses_OnlyGroupClassesAreFloored() {
        Assert.That(ClearSeconds(new Encounter(false, "", 0, 0, true, 3), Defaults), Is.Null, "class 3: one player");
        Assert.That(ClearSeconds(new Encounter(false, "", 0, 0, true, 7), Defaults), Is.Null, "class 7: one player");
        Assert.That(ClearSeconds(new Encounter(false, "", 0, 0, true, 4), Defaults), Is.EqualTo(300));
        Assert.That(ClearSeconds(new Encounter(false, "", 0, 0, true, 6), Defaults), Is.EqualTo(600));
        Assert.That(ClearSeconds(new Encounter(false, "", 0, 0, false, 4), Defaults), Is.Null, "not a boss");
    }

    [Test]
    public void GroupDungeonMonsters_AreFloored_NotOnlyTheBoss() {
        Assert.That(ClearSeconds(RaidMinion, Defaults), Is.EqualTo(600));
        Assert.That(Solo(RaidMinion, 1_000_000, 1), Is.GreaterThan(1));
    }

    [Test]
    public void NormalDungeons_AreOffUnlessTheFileListsThem() {
        var vayar = new Encounter(true, "normal", 20001001, 0, true, 3);
        Assert.That(ClearSeconds(vayar, Defaults), Is.Null);
        Config withNormal = Parse("{\"groupTypes\": {\"normal\": {\"clearSeconds\": 300}}}");
        Assert.That(ClearSeconds(vayar, withNormal), Is.EqualTo(300));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Scenario4_InvincibleOrShieldedTarget_Unchanged(bool invincible, bool shielded) {
        Assert.That(Solo(Balrog, BalrogHp, 1_000, invincible: invincible, shielded: shielded), Is.EqualTo(1_000));
    }

    [Test]
    public void Scenario4_SameHitWithoutImmunity_IsFloored() {
        Assert.That(Solo(Balrog, BalrogHp, 1_000), Is.EqualTo(28_131).Within(1));
    }

    [TestCase(2)]
    [TestCase(4)]
    public void Scenario5_MoreThanOnePlayer_Unchanged(int players) {
        Assert.That(Solo(Balrog, BalrogHp, 1_000, players: players), Is.EqualTo(1_000));
    }

    [Test]
    public void Scenario6_MultihitPointsSumToOneHitOfTheirTotalRate() {
        double three = 3 * FloorHit(BalrogHp, 600, 0.81, Defaults);
        Assert.That(three, Is.EqualTo(FloorHit(BalrogHp, 600, 2.43, Defaults)).Within(1e-6));
    }

    [Test]
    public void OneHitNeverRemovesMoreThanTwoPercent() {
        Assert.That(FloorHit(1_000_000, 1, 50, Defaults), Is.EqualTo(20_000).Within(1e-9));
    }

    [TestCase(DamageType.Miss, 0)]
    [TestCase(DamageType.Block, 0)]
    [TestCase(DamageType.Normal, 0)]
    public void MissBlockAndZero_AreNeverFloored(DamageType type, long calculated) {
        Assert.That(Apply(type, calculated, 10_000), Is.EqualTo(calculated));
    }

    [Test]
    public void HitsAboveTheFloor_KeepTheirValue_CritsIncluded() {
        Assert.That(Apply(DamageType.Critical, 50_000, 22_505.2), Is.EqualTo(50_000));
        Assert.That(Apply(DamageType.Critical, 100, 22_505.2), Is.EqualTo(22_506));
    }

    [Test]
    public void Legacy_DisabledRule_Unchanged() {
        Assert.That(Solo(Balrog, BalrogHp, 1_000, enabled: false), Is.EqualTo(1_000));
    }

    [Test]
    public void Parse_MergesOverTheDefaults() {
        Config config = Parse("{\"floorShare\": 0.5, \"groupTypes\": {\"normal\": {\"clearSeconds\": 300}}}");
        Assert.That(config.FloorShare, Is.EqualTo(0.5));
        Assert.That(config.ReferenceRatePerSecond, Is.EqualTo(2.5));
        Assert.That(config.GroupTypes!["raid"].ClearSeconds, Is.EqualTo(600));
        Assert.That(config.GroupTypes["normal"].ClearSeconds, Is.EqualTo(300));
        Assert.That(config.Classes!["4"].PartySize, Is.EqualTo(2));
    }
}
