using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-02: a hit ledger. Ten common monsters at about Lv 20, 50 and 60 (the Lv 60 end is the rig's player, a bare Lv 60 Knight with
/// its job's level stats and no gear) cast each of their skills on the player a number of times, and the damage of every hit of the type
/// Normal is read from the player's damage packets. A monster's hit has no weapon roll (GetBonusAttack returns 1, 1 for an NPC), so what a
/// skill does to this player is a few fixed amounts, one per rate of its attack points; a critical hit (1.5 times) or a miss is a random roll
/// and is not counted. The amounts below are today's values (the monster factor 2.8, BL-01 moves it into a file); a balance change moves them
/// and this ledger is then written again from the test output. The test allows 2 % either way for the truncation of the factors.</summary>
[Category("Data")]
[NonParallelizable]
public class HitLedgerTest {
    private const double Tolerance = 0.02;
    private const int Casts = 10;

    // Monster id, skill id: the Normal hit amounts on the rig's player, sorted, distinct. Empty: the skill hits nothing at 100 units
    // (a buff, a summon or a shot that needs a longer line).
    private static readonly (int Npc, int Skill, long[] Amounts)[] Ledger = [
        (21000026, 40026001, [47]), // Brown Birk
        (21000191, 40302001, [67]), // Evil Stirge
        (21090104, 40262001, [80]), // Skelic Swordman
        (21090104, 40262011, [40, 80]),
        (24000902, 40013011, [20]), // Hungry Piggy
        (21000087, 40079001, [2133]), // Lava Golem
        (21000087, 40079011, [1066]),
        (21000143, 40350001, [440]), // Leafy Turto
        (21000143, 40350011, [1333]),
        (21000250, 40186001, [2666]), // Frillina
        (21000250, 40186021, [1333]),
        (21091037, 50000615, [2247]), // Frostguard Knight
        (21091045, 40367001, [2247]), // Cursed Drake
        (21091045, 40367011, [2247]),
        (21099208, 40037001, [2174]), // Axe Stump
    ];

    private static readonly int[] Monsters = [
        21000026, 21000191, 21090104, 24000902, // Lv 20
        21000087, 21000143, 21000250, // Lv 50
        21091037, 21091045, 21099208, // Lv 60
    ];

    private static long playerHealth;

    private static FieldRig Open() {
        FieldRig? rig = FieldRig.TryOpen();
        if (rig == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        rig!.ScheduledEventsFollowRigTime = true;
        Stat health = rig.Session.Stats.Values[BasicAttribute.Health];
        playerHealth = health.Total;
        health.Total = health.Base = health.Current = 1_000_000_000_000;
        return rig;
    }

    /// <summary>The distinct Normal amounts of the skill's hits over <see cref="Casts"/> casts, sorted.</summary>
    private static long[] Measure(FieldRig rig, NpcMetadata metadata, int skillId, short level) {
        rig.Clean();
        FieldNpc npc = rig.SpawnNpc(metadata.Id, new Vector3(100, 0, 0), ai: false);
        rig.AdvanceUntil(() => npc.MovementState.State is not (ActorState.Spawn or ActorState.Regen), 10_000);
        rig.Player.Position = new Vector3(rig.Player.Position.X, rig.Player.Position.Y, npc.Position.Z);
        rig.ClearSent();
        for (int cast = 0; cast < Casts; cast++) {
            MovementState.NpcSkillCastTask task = rig.Cast(npc, skillId, level);
            rig.AdvanceUntil(() => task.IsDone, 15_000);
            rig.AdvanceUntil(() => rig.Regions == 0, 20_000);
            rig.Advance(4000); // a missile's flight
        }
        long[] amounts = rig.Damages.Where(hit => hit.Type == DamageType.Normal).Select(hit => hit.Amount).Distinct().OrderBy(amount => amount).ToArray();
        rig.Despawn(npc);
        return amounts;
    }

    [Test]
    public void TodaysMonsterHitsOnTheRigsPlayer() {
        using FieldRig rig = Open();
        var report = new StringBuilder();
        report.AppendLine($"the rig's player: Lv {FieldRig.PlayerLevel} Knight, {playerHealth} health, hits as a share of it in brackets");
        var failures = new List<string>();
        foreach (int id in Monsters) {
            Assert.That(rig.Npcs.TryGet(id, out NpcMetadata? metadata), Is.True, $"npc {id}");
            foreach (NpcMetadataSkill.Entry entry in metadata!.Skill.Entries) {
                long[] amounts = Measure(rig, metadata, entry.Id, entry.Level);
                report.AppendLine($"LEDGER Lv {metadata.Basic.Level} {id} {metadata.Name}: skill {entry.Id} level {entry.Level}: {string.Join(", ", amounts.Select(amount => $"{amount} [{amount * 100.0 / playerHealth:0.00} %]"))}");
                (int Npc, int Skill, long[] Amounts) golden = Ledger.FirstOrDefault(row => row.Npc == id && row.Skill == entry.Id);
                if (golden.Amounts == null) {
                    failures.Add($"{id} skill {entry.Id}: not in the ledger");
                } else if (golden.Amounts.Length != amounts.Length || golden.Amounts.Zip(amounts).Any(pair => Math.Abs(pair.First - pair.Second) > pair.First * Tolerance + 1)) {
                    failures.Add($"{id} skill {entry.Id}: held {string.Join(", ", golden.Amounts)}, now {string.Join(", ", amounts)}");
                }
            }
        }
        TestContext.Out.Write(report.ToString());
        Assert.That(rig.Errors, Is.Empty);
        Assert.That(failures, Is.Empty, "hits that moved");
    }
}
