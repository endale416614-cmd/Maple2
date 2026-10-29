using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>Offline pet taming option (PetTaming=true): spawn rate, candy capture, taming items used up.</summary>
public class PetTamingTest {
    private const int MAX = 10000; // constants.xml TamingPetMaxPoint

    [Test]
    public void Capture_OptionOff_KeepsOriginalRule() {
        // Lapis Master Snare: TrapLevel 3, ForcedTaming
        Assert.That(OfflinePets.Captures(3, true, 0, MAX, enabled: false), Is.True);
        Assert.That(OfflinePets.Captures(1, false, MAX, MAX, enabled: false), Is.True, "non-forced trap on a full gauge");
        Assert.That(OfflinePets.Captures(1, false, MAX - 1, MAX, enabled: false), Is.False);
        // Dryad Pet Candy: no trap
        Assert.That(OfflinePets.Captures(0, false, MAX, MAX, enabled: false), Is.False, "candy never captured before");
    }

    [Test]
    public void Capture_OptionOn_FullGaugeCaptures() {
        Assert.That(OfflinePets.Captures(0, false, MAX, MAX, enabled: true), Is.True, "10 candies fill the gauge");
        Assert.That(OfflinePets.Captures(0, false, MAX - 100, MAX, enabled: true), Is.False);
        Assert.That(OfflinePets.Captures(3, true, 0, MAX, enabled: true), Is.True, "snare still instant");
        Assert.That(OfflinePets.Captures(0, false, 0, 0, enabled: true), Is.False, "missing constant never captures");
    }

    [Test]
    public void SpawnRate_TripledAndCapped() {
        Assert.That(OfflinePets.ScaledSpawnRate(1000), Is.EqualTo(3000), "10% -> 30%");
        Assert.That(OfflinePets.ScaledSpawnRate(5000), Is.EqualTo(OfflinePets.SPAWN_RATE_TOTAL), "50% caps at 100%");
        Assert.That(OfflinePets.ScaledSpawnRate(0), Is.EqualTo(0), "regions without pets stay without");
        Assert.That(OfflinePets.ScaledSpawnRate(int.MaxValue), Is.EqualTo(OfflinePets.SPAWN_RATE_TOTAL), "no overflow");
    }

    [Test]
    public void CaptureRarity_OptionOff_KeepsPlaceholder() {
        int[] counts = Distribution(enabled: false);
        Assert.That(counts[1..], Is.EqualTo(new[] { 24, 25, 25, 25, 1 }));
    }

    [Test]
    public void CaptureRarity_OptionOn_DoublesEpicAndLegendary() {
        int[] counts = Distribution(enabled: true);
        Assert.That(counts[1..], Is.EqualTo(new[] { 16, 16, 16, 50, 2 }), "Normal, Rare, Exceptional, Epic, Legendary out of 100 ranks");
    }

    // FieldPet rolls TamingRank 1-100 evenly.
    private static int[] Distribution(bool enabled) {
        var counts = new int[6];
        for (int rank = 1; rank <= 100; rank++) {
            counts[OfflinePets.CaptureRarity(rank, enabled)]++;
        }
        return counts;
    }

    [Test]
    public void OnlyTamingItemsAreConsumedOnCast() {
        Assert.That(OfflinePets.IsTamingItem("PetTaming", 94000001, 94000001), Is.True, "Dryad Pet Candy throw");
        Assert.That(OfflinePets.IsTamingItem("PetTaming", 94000001, 94000002), Is.False, "the splash is not the item's own skill");
        Assert.That(OfflinePets.IsTamingItem("PetTrap", 93000001, 93000001), Is.True, "Dryad G1 Snare throw");
        // IT-04: potions are no taming item; FieldPlayer.UsesUpOnCast uses them up in both modes, with or without the useItem flag.
        Assert.That(OfflinePets.IsTamingItem("PS", 90000015, 90000015), Is.False, "potions use the data's useItem flag");
    }
}
