using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>VR-04 (BT-238): how long the regions of NPC skills live, and how hard the traps of the map scripts hit. A region lasts its Delay plus
/// its RemoveDelay plus its fire count of intervals (FieldSkill's constructor; an ImmediateActive one fires at its add and lasts one interval
/// less). Nothing removes a region at its caster's death (RN-02), and RN-01 only caps their number, so the long ones are the ones to know. A
/// script trap is a map-placed skill (set_skill): it repeats at its Interval (at least 100 ms) when its count is 0, and a hit may take a share
/// of the target's maximum health (DamageByTargetMaxHp). Both listings go to the test output; the thresholds hold today's worst values, so a
/// longer region or a harder trap fails the test and gets looked at.</summary>
[Category("Data")]
[NonParallelizable]
public class LongRegionTest {
    private const long Minute = 60_000;

    // Today's worst: 2,147,506,647 ms (24.9 days: Landevian 23000117's skill 60300104 level 2, region 99999763, a delay of int.MaxValue); 90
    // regions of 60 s or more of the 1,041 the NPC skills lay, held by 80 NPCs (BT-238's 81 rows counted the same kind another way).
    private const long LongestRegionMs = 2_147_506_647;
    private const int MostLongRegions = 90;

    // Today's worst trap: 70000065 level 6 and 70002001 hit for the target's whole health every 100 ms (10 times a second). The first is
    // MonsterOnly (FieldSkill); the second is the wipe of 02020111 and 02020145 (a range of 14,500, one cast each, started by their scripts):
    // a finding, listed and not fixed.
    private const double WorstHealthPerSecond = 10.0;
    private static readonly (int Id, short Level)[] KnownWipes = [(70002001, 1)];
    private static readonly (int Id, short Level)[] KnownOverAHealthBarPerSecond = [
        (70000065, 6), (70002001, 1), (70000085, 1), (70000015, 2), (70000015, 3), (70000569, 1), (70000567, 3),
    ];

    private sealed record Region(int NpcId, string? NpcName, int SkillId, short SkillLevel, int RegionId, short RegionLevel, long LifeMs);

    private static IEnumerable<SkillEffectMetadata> Effects(SkillMetadataLevel level) =>
        level.Skills.Concat(level.Motions.SelectMany(motion => motion.Attacks).SelectMany(attack => attack.Skills.Concat(attack.SkillsOnDamage)));

    private static long Life(SkillEffectMetadata effect) {
        SkillEffectMetadataSplash splash = effect.Splash!;
        int fireCount = effect.FireCount > 0 ? effect.FireCount : -1; // FieldManager.AddSkill
        return splash.ImmediateActive
            ? splash.RemoveDelay + (long) (fireCount - 1) * splash.Interval
            : (long) splash.Delay + splash.RemoveDelay + (long) fireCount * splash.Interval;
    }

    private static void WithData(Action<MetadataContext> test) {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        test(context);
    }

    [Test]
    public void NpcRegionsThatLiveOverAMinute() {
        WithData(context => {
            Dictionary<int, IReadOnlyDictionary<short, SkillMetadataLevel>> skills = context.SkillMetadata.AsEnumerable().ToDictionary(skill => skill.Id, skill => skill.Levels);
            var regions = new List<Region>();
            foreach (NpcMetadata npc in context.NpcMetadata.AsEnumerable()) {
                foreach (NpcMetadataSkill.Entry entry in npc.Skill.Entries) {
                    if (!skills.TryGetValue(entry.Id, out IReadOnlyDictionary<short, SkillMetadataLevel>? levels) || !levels.TryGetValue(entry.Level, out SkillMetadataLevel? level)) {
                        continue;
                    }
                    foreach (SkillEffectMetadata effect in Effects(level).Where(effect => effect.Splash != null)) {
                        regions.AddRange(effect.Skills.Select(skill => new Region(npc.Id, npc.Name, entry.Id, entry.Level, skill.Id, skill.Level, Life(effect))));
                    }
                }
            }

            List<Region> longLived = regions.Where(region => region.LifeMs >= Minute)
                .DistinctBy(region => (region.SkillId, region.SkillLevel, region.RegionId, region.RegionLevel, region.LifeMs)).OrderByDescending(region => region.LifeMs).ToList();
            foreach (Region region in longLived) {
                string who = string.Join(", ", regions.Where(other => other.SkillId == region.SkillId && other.SkillLevel == region.SkillLevel && other.RegionId == region.RegionId
                                                                   && other.RegionLevel == region.RegionLevel && other.LifeMs == region.LifeMs)
                    .Select(other => $"{other.NpcName} {other.NpcId}").Distinct().Take(4));
                TestContext.Out.WriteLine($"region {region.RegionId} level {region.RegionLevel} of skill {region.SkillId} level {region.SkillLevel}: {region.LifeMs / 1000.0:0.#} s ({who})");
            }
            TestContext.Out.WriteLine($"{longLived.Count} regions of 60 s or more of {regions.Select(region => (region.SkillId, region.SkillLevel, region.RegionId, region.RegionLevel, region.LifeMs)).Distinct().Count()}; "
                                      + $"{longLived.SelectMany(region => regions.Where(other => other.SkillId == region.SkillId && other.RegionId == region.RegionId).Select(other => other.NpcId)).Distinct().Count()} NPCs hold one");

            foreach (string name in new[] { "Captain Moc", "Tristan", "Nyxie", "Nyxie the Runaway" }) {
                Assert.That(regions.Any(region => region.NpcName == name && region.LifeMs >= Minute), Is.True, $"{name} lays a region of a minute or more (BT-238)");
            }
            Assert.That(regions.Any(region => region.LifeMs == 99_999_000), Is.True, "the 99,999 s mine (BT-238)");
            Assert.That(longLived.Count, Is.LessThanOrEqualTo(MostLongRegions), "no new region of a minute or more");
            Assert.That(longLived.Max(region => region.LifeMs), Is.LessThanOrEqualTo(LongestRegionMs), "no region lives longer than today's longest");
        });
    }

    private sealed record Trap(double PerCast, double PerSecond, ApplyTargetType Applies, int Placements, int Repeating, HashSet<string> Maps);

    [Test]
    public void ScriptTrapsAndTheirHealthPerSecond() {
        WithData(context => {
            Dictionary<int, IReadOnlyDictionary<short, SkillMetadataLevel>> skills = context.SkillMetadata.AsEnumerable().ToDictionary(skill => skill.Id, skill => skill.Levels);
            var traps = new Dictionary<(int Id, short Level), Trap>();
            foreach (MapEntity entity in context.MapEntity.Where(entity => entity.Name.StartsWith("MS2TriggerSkill")).AsEnumerable()) {
                if (entity.Block is not Ms2TriggerSkill placed || !skills.TryGetValue(placed.SkillId, out IReadOnlyDictionary<short, SkillMetadataLevel>? levels)
                                                              || !levels.TryGetValue(placed.Level, out SkillMetadataLevel? level)) {
                    continue;
                }
                // A cast hits for the share of the target's health of each of its attacks, Count times (the data's DamageByTargetMaxHp).
                SkillMetadataAttack[] attacks = level.Motions.SelectMany(motion => motion.Attacks).ToArray();
                double perCast = attacks.Sum(attack => attack.Damage.DamageByTargetMaxHp * Math.Max(attack.Damage.Count, 1));
                double perSecond = perCast * 1000 / FieldManager.TriggerInterval(placed.Interval ?? 0);
                Trap trap = traps.GetValueOrDefault((placed.SkillId, placed.Level))
                            ?? new Trap(perCast, 0, attacks.FirstOrDefault(attack => attack.Damage.DamageByTargetMaxHp > 0)?.Range.ApplyTarget ?? ApplyTargetType.None, 0, 0, []);
                trap.Maps.Add(entity.XBlock);
                traps[(placed.SkillId, placed.Level)] = trap with {
                    PerSecond = Math.Max(trap.PerSecond, perSecond), Placements = trap.Placements + 1, Repeating = trap.Repeating + (placed.Count == 0 ? 1 : 0),
                };
            }

            List<KeyValuePair<(int Id, short Level), Trap>> hurting = traps.Where(trap => trap.Value.PerCast > 0).OrderByDescending(trap => trap.Value.PerSecond).ToList();
            foreach (((int id, short level), Trap trap) in hurting) {
                TestContext.Out.WriteLine($"trap {id} level {level}: {trap.PerCast * 100:0.#} % of the target's health per cast, up to {trap.PerSecond * 100:0.#} % per second, applies to {trap.Applies}, "
                                          + $"{trap.Placements} placements ({trap.Repeating} repeating) on {trap.Maps.Count} maps");
            }
            double worst = hurting.Max(trap => trap.Value.PerSecond);
            TestContext.Out.WriteLine($"{hurting.Count} of {traps.Count} trap skill levels take a share of health; the worst is {worst * 100:0.#} % per second");

            // A cast that takes a player's whole health: not a monster-only region, not one that names friendly or every actor (the score dummies).
            var wipes = hurting.Where(trap => trap.Value.PerCast >= 1 && !FieldSkill.MonsterOnly(trap.Key.Id, trap.Key.Level)
                                              && trap.Value.Applies is ApplyTargetType.Hostile or ApplyTargetType.RegionBuff or ApplyTargetType.RegionBuff2)
                .Select(trap => trap.Key).ToList();
            Assert.That(worst, Is.LessThanOrEqualTo(WorstHealthPerSecond), "no trap hits harder than today's worst");
            Assert.That(hurting.Where(trap => trap.Value.PerSecond > 1.0001).Select(trap => trap.Key), Is.EquivalentTo(KnownOverAHealthBarPerSecond), "a new trap over 100 % of health per second");
            Assert.That(wipes, Is.EquivalentTo(KnownWipes), "a trap that takes the whole health of a player in a cast is a finding: it is listed here, not fixed");
        });
    }
}
