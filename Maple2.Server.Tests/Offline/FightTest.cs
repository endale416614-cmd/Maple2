using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-02: fights on the field rig (VR-01a). The play checks of the wave 3 monster specs (NB-19 to NB-29) as assertions, the heal at a
/// battle's end, a stun, an invincible window. The NPC has no AI and casts what the test queues, so a run does not depend on which skill an AI
/// picks; the rig's clock is the only clock (its one-shot delayed events follow it). Limits of the rig, stated per check below: one bare Lv 60
/// Knight whose stat rebuild is off, one NPC at a time, nothing that needs a second player or a portal.</summary>
[Category("Data")]
[NonParallelizable]
public class FightTest {
    private static FieldRig Open() {
        FieldRig? rig = FieldRig.TryOpen();
        if (rig == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        rig!.ScheduledEventsFollowRigTime = true;
        Stat health = rig.Session.Stats.Values[BasicAttribute.Health];
        health.Total = health.Base = health.Current = 1_000_000_000_000; // no hit kills it; HealthLost is not read, the damage packets are
        return rig;
    }

    /// <summary>An NPC with no AI at <paramref name="offset"/> from the player, past its spawn, with the player on its level and the send queue empty.</summary>
    private static FieldNpc Spawn(FieldRig rig, int npcId, int offset = 100) {
        FieldNpc npc = rig.SpawnNpc(npcId, new Vector3(offset, 0, 0), ai: false);
        Assert.That(rig.AdvanceUntil(() => npc.MovementState.State is not (ActorState.Spawn or ActorState.Regen), 10_000), Is.True, "it finished spawning");
        rig.Player.Position = new Vector3(rig.Player.Position.X, rig.Player.Position.Y, npc.Position.Z); // the narrow boxes of some attacks are 50 high
        rig.ClearSent();
        return npc;
    }

    private static IEnumerable<FieldSkill> Regions(FieldRig rig) => rig.Field.DebugFieldSkills.Concat(rig.Field.DebugCubeSkills).OrderBy(region => region.ObjectId);

    /// <summary>The rig time of the first step on which <paramref name="count"/> regions of the skill have been placed, or -1.</summary>
    private static long WaitForRegions(FieldRig rig, int skillId, int count, int maxMs) =>
        rig.AdvanceUntil(() => Regions(rig).Count(region => region.Value.Id == skillId) >= count, maxMs) ? rig.Elapsed : -1;

    // NB-23, NB-22c: Mushming 21000012's 40011011 lays a ring of eight tiles around the player it hits (p0), then one under him (p1).
    [Test]
    public void MushmingLaysARingOfEightThenOneUnderThePlayer() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 21000012);
        long start = rig.Elapsed;
        rig.Cast(npc, 40011011);
        Assert.That(rig.AdvanceUntil(() => Regions(rig).Count() >= 2, 5000), Is.True, "two regions");

        List<FieldSkill> placed = Regions(rig).ToList();
        Assert.That(placed.Select(region => region.Value.Id), Is.All.EqualTo(40011012));
        Assert.That(placed.Select(region => region.Points.Length), Is.EqualTo(new[] { 8, 1 }), "a ring of eight, then one");
        TestContext.Out.WriteLine($"both regions placed {rig.Elapsed - start} ms after the cast");

        rig.Advance(4000);
        Assert.That(rig.Damages.Count, Is.EqualTo(1), // any type: a miss or a critical roll is still the one hit
             "only the tile under the player hurts him, not the ring around him");
        Assert.That(rig.Regions, Is.Zero, "and both end");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-20a, NB-22c: Mushming's Vine Wave 40011001 lays four one-block tiles in front of it, and a tile hits 1.5 s after it is placed.
    [Test]
    public void MushmingsVineWaveLaysFourTilesThatHitOnceAfterAboutOneAndAHalfSeconds() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 21000012);
        rig.Cast(npc, 40011001);
        long first = WaitForRegions(rig, 40011002, 1, 5000);
        Assert.That(first, Is.GreaterThan(0), "a first tile");
        Assert.That(WaitForRegions(rig, 40011002, 4, 5000), Is.GreaterThan(0), "four tiles");
        Assert.That(Regions(rig).Count(), Is.EqualTo(4));

        Assert.That(rig.AdvanceUntil(() => rig.Damages.Count > 0, 5000), Is.True, "a tile hit");
        long hit = rig.Damages[0].AtMs;
        TestContext.Out.WriteLine($"first tile at {first} ms, first hit at {hit} ms: {hit - first} ms (captured median 1,500 ms)");
        Assert.That(hit - first, Is.InRange(1400, 1700));
        rig.Advance(3000);
        Assert.That(rig.Damages.Count, Is.EqualTo(1), "one player on one tile: one hit");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-19b: Lazy Stumpy 24000409's 60036011 (Attack_01_A, LoopCount 2) lands its p0 three times, then plays Attack_02_A, and the monster
    // goes on. NB-22a: its own strikes do not hurt it.
    [Test]
    public void LazyStumpyStrikesThreeTimesThenItsNextMotionAndGoesOn() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 24000409);
        long start = rig.Elapsed;
        rig.Cast(npc, 60036011);
        Assert.That(rig.AdvanceUntil(() => rig.Damages.Count >= 4, 6000), Is.True, "four hits");
        long[] at = rig.Damages.Select(hit => hit.AtMs - start).ToArray();
        TestContext.Out.WriteLine("hits at " + string.Join(", ", at) + " ms (captured medians 510, 970, 1520)");
        Assert.That(at[0], Is.InRange(510 - 150, 510 + 150));
        Assert.That(at[1], Is.InRange(970 - 150, 970 + 150));
        Assert.That(at[2], Is.InRange(1520 - 150, 1520 + 150));
        Assert.That(at[3], Is.GreaterThan(at[2] + 150), "the fourth is the next motion's");

        rig.Advance(4000);
        Assert.That(rig.Damages.Select(hit => hit.SkillId).Distinct(), Is.EqualTo(new[] { 60036011 }));
        Assert.That(npc.MovementState.CastTask, Is.Null, "the cast is over");
        Assert.That(npc.MovementState.State, Is.EqualTo(ActorState.Idle), "and it goes on");
        Stat health = npc.Stats.Values[BasicAttribute.Health];
        Assert.That(health.Current, Is.EqualTo(health.Total), "its own strikes did not hurt it");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-19: Bubble Friller 32001201's 49104011 (Attack_01_B, end 1.73 s, p0 key at 1.07 s): its p0 attack point comes with its key, and its
    // p1 to p3, which have no key, at the end of the motion; before NB-19 only p0 hit.
    [Test]
    public void BubbleFrillersAttackPointsComeAtItsKeyAndAtTheEndOfItsMotion() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 32001201);
        long start = rig.Elapsed;
        rig.Cast(npc, 49104011);
        rig.Advance(6000);

        List<(long AtMs, int SkillId, byte AttackPoint)> points = rig.AttackPoints.Where(point => point.SkillId == 49104011).GroupBy(point => point.AttackPoint).Select(group => group.First()).OrderBy(point => point.AttackPoint).ToList();
        TestContext.Out.WriteLine("attack points at " + string.Join(", ", points.Select(point => $"p{point.AttackPoint} {point.AtMs - start} ms")) + " (captured medians p0 1,126, p1 to p3 1,709 to 1,743)");
        Assert.That(points.Select(point => point.AttackPoint), Is.EqualTo(new byte[] { 0, 1, 2, 3 }), "all four points fire");
        Assert.That(points[0].AtMs - start, Is.InRange(1126 - 200, 1126 + 200));
        Assert.That(points.Skip(1).Select(point => point.AtMs - start), Is.All.InRange(1726 - 200, 1726 + 200));
        Assert.That(points[1].AtMs, Is.EqualTo(points[3].AtMs), "p1 to p3 together");
        Assert.That(rig.Damages.Count, Is.GreaterThanOrEqualTo(4), "four hits on the player (a missile hits when it arrives)");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-19: Chicken Chopper 21000156's 40126011 (its model has no Attack_02_A) fires its three attacks at once, and the monster goes on.
    [Test]
    public void ChickenChoppersAttacksFireAtOnceWhenItsModelHasNoSequence() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 21000156, 80);
        rig.Cast(npc, 40126011);
        Assert.That(rig.AdvanceUntil(() => rig.Damages.Count > 0, 5000), Is.True, "it hit");
        rig.Advance(2000);
        TestContext.Out.WriteLine("chicken chopper hits: " + string.Join(", ", rig.Damages.Select(hit => $"{hit.Type}:{hit.Amount}:{hit.SkillId}@{hit.AtMs}")));
        Assert.That(rig.Damages.Count, Is.EqualTo(3), "three hits");
        Assert.That(rig.Damages.Select(hit => hit.AtMs).Distinct().Count(), Is.EqualTo(1), "in one step");
        Assert.That(npc.MovementState.CastTask, Is.Null, "the cast ended");
        Assert.That(npc.MovementState.State, Is.EqualTo(ActorState.Idle), "and it goes on");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-25: a monster's missile hits when it arrives, its distance over its path's velocity after the attack point. Ninecap Copper
    // 22000042's slow bolt 60028021 (velocity 300): 600 more units of distance are 2,000 ms more flight.
    [Test]
    public void AMissileHitsWhenItArrives() {
        long[] arrival = new long[2];
        long[] attack = new long[2];
        for (int run = 0; run < 2; run++) {
            using FieldRig rig = Open();
            FieldNpc npc = Spawn(rig, 22000042, run == 0 ? 300 : 900);
            rig.Cast(npc, 60028021);
            Assert.That(rig.AdvanceUntil(() => rig.Damages.Count > 0, 12_000), Is.True, "the bolt arrived");
            arrival[run] = rig.Damages[0].AtMs;
            attack[run] = rig.AttackPoints.First(point => point.SkillId == 60028021).AtMs;
            Assert.That(rig.Errors, Is.Empty);
        }
        TestContext.Out.WriteLine($"bolt from 300: attack point {attack[0]} ms, hit {arrival[0]} ms; from 900: {attack[1]} ms, {arrival[1]} ms");
        Assert.That(arrival[0] - attack[0], Is.InRange(1000 - 100, 1000 + 100), "300 units at velocity 300");
        Assert.That(arrival[1] - attack[1], Is.InRange(3000 - 100, 3000 + 100), "900 units at velocity 300: about 3 s, as the play check says");
    }

    // NB-27: Eupheria 23501101's 60300007 lays five mines in an X around her feet; one that is not stepped on ends after 15 s with no blast.
    [Test]
    public void EupheriasMinesLieInAnXAndEndWithNoBlast() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 23501101, 600);
        rig.Cast(npc, 60300007);
        Assert.That(rig.AdvanceUntil(() => Regions(rig).Count(region => region.Value.Id == 60300013) >= 5, 5000), Is.True, "five mines");
        long placed = rig.Elapsed;
        List<FieldSkill> mines = Regions(rig).Where(region => region.Value.Id == 60300013).ToList();
        TestContext.Out.WriteLine("mines at " + string.Join(" ", mines.Select(mine => string.Join("/", mine.Points.Select(point => point - npc.Position)))));
        Assert.That(mines, Has.Count.EqualTo(5));
        Assert.That(mines.SelectMany(mine => mine.Points).Select(point => Vector3.Distance(new Vector3(point.X, point.Y, 0), new Vector3(npc.Position.X, npc.Position.Y, 0))),
            Is.All.LessThan(300), "around her feet");
        Assert.That(mines.SelectMany(mine => mine.Points).Select(point => Vector3.Distance(new Vector3(point.X, point.Y, 0), new Vector3(rig.Player.Position.X, rig.Player.Position.Y, 0))),
            Is.All.GreaterThan(300), "and not under the player");

        int hitsBefore = rig.Damages.Count;
        Assert.That(rig.AdvanceUntil(() => Regions(rig).Count(region => region.Value.Id == 60300013) == 0, 20_000), Is.True, "they end");
        TestContext.Out.WriteLine($"mines placed at {placed} ms, gone at {rig.Elapsed} ms");
        Assert.That(rig.Elapsed - placed, Is.InRange(14_000, 16_500), "after 15 s");
        Assert.That(rig.Damages.Count, Is.EqualTo(hitsBefore), "no blast when they end");
        Assert.That(rig.Errors, Is.Empty);
    }

    /// <summary>Play checks that fail on the rig today, each with why: the test reports the failure here instead of failing. A check that
    /// passes while it is listed is reported too, so the entry can go.</summary>
    private static readonly Dictionary<string, string> Known = new() {
        ["Eupheria's mine blows under the player"] = "stepping on the middle mine ends it (4 of 5 stay) but no hit reaches the player and no blast tile is laid",
    };

    private static void Check(string name, bool holds) {
        if (!holds && !Known.ContainsKey(name)) {
            Assert.Fail(name);
        } else if (!holds) {
            TestContext.Out.WriteLine($"KNOWN {name}: {Known[name]}");
        } else if (Known.ContainsKey(name)) {
            TestContext.Out.WriteLine($"KNOWN entry no longer fails: {name}");
        }
    }

    // NB-27: a mine blows when a player steps on one (2 of 30 captured adds, the middle one, with the player passing over it).
    [Test]
    public void EupheriasMineBlowsWhenThePlayerStepsOnIt() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 23501101, 600);
        rig.Cast(npc, 60300007);
        Assert.That(rig.AdvanceUntil(() => Regions(rig).Count(region => region.Value.Id == 60300013) >= 5, 5000), Is.True);
        rig.Advance(500);
        int before = rig.Damages.Count;
        FieldSkill middle = Regions(rig).First(region => region.Value.Id == 60300013 && (region.Points[0] - npc.Position).Length() < 10);
        rig.Player.Position = middle.Points[0];
        rig.Advance(1500);
        int mines = Regions(rig).Count(region => region.Value.Id == 60300013);
        TestContext.Out.WriteLine($"after stepping on the middle mine: mines {mines}, hits {rig.Damages.Count - before}, regions {rig.Regions}");
        Assert.That(mines, Is.EqualTo(4), "the mine fired and is gone");
        Check("Eupheria's mine blows under the player", rig.Damages.Count - before > 0);
    }

    // PI-02 (BT-267, BT-260): a monster flagged for peace restore is back at full health once its target is gone; its AI starts again only after
    // a real break, a battle that starts 2,000 ms or more after the restore (AiState.RestoreBreakMs), not at a target gap.
    private const string Sentinel = "VR-02 sentinel";

    private static Dictionary<string, long> CoolTimes(FieldNpc npc) =>
        (Dictionary<string, long>) typeof(AiState).GetField("coolTimes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(npc.AiState)!;

    /// <summary>Fights Mushming, halves its health, takes the player out of its sight until it is restored, and brings the player back
    /// <paramref name="awayMs"/> after the restore. Returns the NPC once its next battle has started.</summary>
    private static (FieldNpc Npc, bool Restored, long RestoreAge) FightAndLoseTheTarget(FieldRig rig, int awayMs) {
        FieldNpc npc = rig.SpawnNpc(21000012, new Vector3(150, 0, 0), peaceRestore: true);
        Vector3 home = rig.Player.Position;
        Assert.That(rig.AdvanceUntil(() => npc.BattleState.InBattle, 20_000), Is.True, "the fight starts");
        Stat health = npc.Stats.Values[BasicAttribute.Health];
        health.Current = health.Total / 2;
        CoolTimes(npc)[Sentinel] = 1;

        rig.Player.Position = home + new Vector3(30_000, 0, 0);
        Assert.That(rig.AdvanceUntil(() => !npc.BattleState.InBattle, 10_000), Is.True, "the target is gone");
        bool restored = rig.AdvanceUntil(() => health.Current == health.Total, 10_000);
        long restoredAt = rig.Elapsed;
        rig.Advance(awayMs);
        long age = rig.Elapsed - restoredAt;
        rig.Player.Position = home;
        Assert.That(rig.AdvanceUntil(() => npc.BattleState.InBattle, 20_000), Is.True, "the next battle starts");
        return (npc, restored, age);
    }

    [Test]
    public void AMonsterIsFullAfterItsTargetIsGoneAndItsAiRestartsAfterARealBreak() {
        using FieldRig rig = Open();
        (FieldNpc npc, bool restored, long age) = FightAndLoseTheTarget(rig, 2500);
        TestContext.Out.WriteLine($"restored: {restored}, away {age} ms after the restore, sentinel kept: {CoolTimes(npc).ContainsKey(Sentinel)}");
        Assert.That(restored, Is.True, "back at full health");
        Assert.That(CoolTimes(npc).ContainsKey(Sentinel), Is.False, "its cooltimes were cleared at the next battle");
        Assert.That(rig.Errors, Is.Empty);
    }

    [Test]
    public void ABattleThatResumesWithinTheBreakKeepsItsAiState() {
        using FieldRig rig = Open();
        (FieldNpc npc, bool restored, long age) = FightAndLoseTheTarget(rig, 0);
        TestContext.Out.WriteLine($"restored: {restored}, back {age} ms after the restore, sentinel kept: {CoolTimes(npc).ContainsKey(Sentinel)}");
        Assert.That(restored, Is.True, "still restored at a target gap");
        Assert.That(age, Is.LessThan(AiState.RestoreBreakMs));
        Assert.That(CoolTimes(npc).ContainsKey(Sentinel), Is.True, "its cooltimes stay");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-24: a stun holds an NPC where it stands, and a move queued during it waits until the buff ends.
    [Test]
    public void AStunHoldsTheNpcAndItsQueuedMoveWaitsForTheBuffToEnd() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 21000012, 400);
        Vector3 start = npc.Position;
        npc.MovementState.TryMoveTo(start + new Vector3(0, 300, 0), true);
        rig.Advance(500);
        Assert.That(Vector3.Distance(npc.Position, start), Is.GreaterThan(1), "it walks");

        npc.Buffs.AddBuff(npc, npc, 50000022, 1, rig.Field.FieldTick);
        Assert.That(npc.Buffs.HasBuff(50000022), Is.True, "the stun reached it");
        rig.Advance(100);
        Assert.That(npc.MovementState.State, Is.EqualTo(ActorState.Stun));
        Vector3 held = npc.Position;
        npc.MovementState.TryMoveTo(held + new Vector3(0, -300, 0), true); // queued while it is held
        rig.Advance(3000);
        Assert.That(npc.MovementState.State, Is.EqualTo(ActorState.Stun), "still held");
        Assert.That(Vector3.Distance(npc.Position, held), Is.LessThan(1), "it did not move");

        Assert.That(rig.AdvanceUntil(() => npc.MovementState.State != ActorState.Stun, 15_000), Is.True, "the stun ends");
        TestContext.Out.WriteLine($"stun ended {rig.Elapsed} ms after the start of the rig");
        rig.Advance(1500);
        Assert.That(Vector3.Distance(npc.Position, held), Is.GreaterThan(1), "the queued move ran after it");
        Assert.That(rig.Errors, Is.Empty);
    }

    // NB-21: while an enabled invincible buff holds, a hit is one invincible entry for 0 and takes nothing; after its end the same hit lands.
    [Test]
    public void AnInvincibleWindowTakesNoDamageAndEnds() {
        using FieldRig rig = Open();
        FieldNpc npc = Spawn(rig, 21000012, 600);
        Assert.That(rig.Skills.TryGet(60036011, 1, out SkillMetadata? skill), Is.True);
        SkillMetadataAttack real = skill!.Data.Motions[0].Attacks[0];
        SkillMetadataAttack attack = real with { Damage = real.Damage with { Count = 3, IsConstDamage = true, Value = 100 } };
        Stat health = npc.Stats.Values[BasicAttribute.Health];
        long full = health.Current;

        // 40500022 level 1: invincible for 2 s.
        npc.Buffs.AddBuff(npc, npc, 40500022, 1, rig.Field.FieldTick);
        rig.Advance(500);
        var inside = new DamageRecord(skill, attack);
        npc.ApplyDamage(rig.Player, inside, attack);
        Assert.That(health.Current, Is.EqualTo(full), "no damage inside the window");
        Assert.That(inside.Targets[npc.ObjectId].Damage.Select(entry => (entry.Item1 is DamageType.Invincible or DamageType.Invincible2, entry.Item2)),
            Is.EqualTo(new[] { (true, 0L) }), "one invincible entry for 0");

        rig.Advance(2500);
        var outside = new DamageRecord(skill, attack);
        npc.ApplyDamage(rig.Player, outside, attack);
        Assert.That(health.Current, Is.EqualTo(full - 300), "the same hit after the end takes its three hits");
        Assert.That(rig.Errors, Is.Empty);
    }
}
