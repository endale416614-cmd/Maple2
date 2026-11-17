using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Server.Game.Model;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-01a: the field rig runs real NPCs of maple-data.db on a real map for game time with no thread and no screen.</summary>
[Category("Data")]
[NonParallelizable]
public class FieldRigTest {
    private const int Mushming = 21000012;
    private const int BasicAttack = 40011002; // Mushming's melee skill
    private const int RingSkill = 40011011; // lays a ring of tiles around the player

    private static FieldRig Open() {
        FieldRig? rig = FieldRig.TryOpen();
        if (rig == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        return rig!;
    }

    [Test]
    public void AnNpcUpdatesForAMinuteOfGameTime() {
        using FieldRig rig = Open();
        FieldNpc npc = rig.SpawnNpc(Mushming, new Vector3(300, 0, 0));
        long start = rig.Field.FieldTick;
        rig.Advance(60_000);

        Assert.That(rig.Elapsed, Is.EqualTo(60_000));
        Assert.That(rig.Field.FieldTick - start, Is.EqualTo(60_000), "the field's clock is the rig's");
        Assert.That(rig.Errors, Is.Empty);
        Assert.That(npc.Position, Is.Not.EqualTo(rig.Player.Position + new Vector3(300, 0, 0)), "it moved");
    }

    [Test]
    public void AMonsterWithASkillHitsThePlayerAndTheHitCanBeRead() {
        using FieldRig rig = Open();
        // VR-05: no AI and a forced cast on the player's level, so no AI pick and no walk decides whether the cast reaches the player; the hit rate
        // of a monster against the rig's player is above 1 (it never misses), a critical hit only raises the amount.
        FieldNpc npc = rig.SpawnNpc(Mushming, new Vector3(100, 0, 0), ai: false);
        Assert.That(rig.AdvanceUntil(() => npc.MovementState.State is not (ActorState.Spawn or ActorState.Regen), 10_000), Is.True, "it finished spawning");
        rig.Player.Position = new Vector3(rig.Player.Position.X, rig.Player.Position.Y, npc.Position.Z);
        rig.ClearSent();
        rig.Cast(npc, BasicAttack);
        bool hit = rig.AdvanceUntil(() => rig.HealthLost > 0, 10_000);

        TestContext.Out.WriteLine($"hit={hit} after {rig.Elapsed} ms, lost {rig.HealthLost}, damage packets {rig.Hits}, regions {rig.Regions}");
        Assert.That(hit, Is.True, "the player lost health");
        Assert.That(rig.Hits, Is.GreaterThan(0), "and the hit is a packet in its send queue");
        Assert.That(rig.Damages.Select(entry => entry.Amount).Sum(), Is.EqualTo(rig.HealthLost), "and the packets carry what it lost");
        Assert.That(rig.Errors, Is.Empty);
    }

    [Test]
    public void ARegionPlacedByACastIsCountedAndEnds() {
        using FieldRig rig = Open();
        FieldNpc npc = rig.SpawnNpc(Mushming, new Vector3(150, 0, 0), ai: false); // VR-05: a forced cast, not the AI's pick of one
        Assert.That(rig.AdvanceUntil(() => npc.MovementState.State is not (ActorState.Spawn or ActorState.Regen), 10_000), Is.True, "it finished spawning");
        Assert.That(rig.Regions, Is.Zero, "the map's own regions are not loaded");
        rig.Cast(npc, RingSkill);
        bool placed = rig.AdvanceUntil(() => rig.Regions > 0, 10_000);

        Assert.That(placed, Is.True, "a cast placed a region");
        Assert.That(rig.MostRegionsOfOneCaster, Is.EqualTo(rig.Regions));
        Assert.That(rig.Field.DebugFieldSkills.Select(region => region.Caster.ObjectId), Is.All.EqualTo(npc.ObjectId));

        rig.Despawn(npc);
        rig.Advance(60_000);
        TestContext.Out.WriteLine($"regions after its despawn and a minute: {rig.Regions}");
        Assert.That(rig.Regions, Is.Zero, "it ended");
        Assert.That(rig.Errors, Is.Empty);
    }

    // VR-05: an NPC that dies makes its drops on the field's item calculator; the rig's field had none, so the drop threw in the Update that ran the death.
    [Test]
    public void TheRigsFieldMakesAnItemWithItsStats() {
        using FieldRig rig = Open();
        Assert.That(rig.Field.ItemDrop.CreateItem(11200074), Is.Not.Null, "a Navigator accessory");
        Assert.That(rig.Errors, Is.Empty);
    }
}
