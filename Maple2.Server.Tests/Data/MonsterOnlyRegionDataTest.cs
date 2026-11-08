using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>SE-04: the map placements that hit only monsters, and what the other ApplyTarget 5 placements can do to a player in one cast (BT-195).</summary>
[Category("Data")]
[NonParallelizable]
public class MonsterOnlyRegionDataTest {
    [Test]
    public void ThePlacementsAndTheirDamage() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        // The first attack of every skill level.
        var first = new Dictionary<(int Id, short Level), SkillMetadataAttack>();
        foreach (StoredSkillMetadata skill in context.SkillMetadata) {
            foreach ((short level, SkillMetadataLevel data) in skill.Levels) {
                SkillMetadataAttack? attack = data.Motions.SelectMany(motion => motion.Attacks).FirstOrDefault();
                if (attack != null) {
                    first[(skill.Id, level)] = attack;
                }
            }
        }

        SkillMetadataAttack six = first[(70000065, 6)];
        Assert.That((six.Range.Type, six.Range.Distance, six.Range.Height, six.Range.ApplyTarget), Is.EqualTo((SkillRegion.Cylinder, 6000f, 1500f, ApplyTargetType.RegionBuff)));
        Assert.That((six.TargetCount, six.Damage.Count, six.Damage.DamageByTargetMaxHp), Is.EqualTo((50, 1, 1f)));
        Assert.That(first[(70000065, 7)].Range.ApplyTarget, Is.EqualTo(ApplyTargetType.Player4));

        List<(string XBlock, Ms2TriggerSkill Skill)> placed = context.MapEntity.AsEnumerable()
            .Where(entity => entity.Block is Ms2TriggerSkill)
            .Select(entity => (entity.XBlock, (Ms2TriggerSkill) entity.Block))
            .ToList();
        Assert.That(placed, Has.Count.EqualTo(1357));

        // The rule reaches these placements and no other.
        var monsterOnly = placed.Where(entry => FieldSkill.MonsterOnly(entry.Skill.SkillId, entry.Skill.Level)).ToList();
        Assert.That(monsterOnly.Select(entry => (entry.Skill.SkillId, entry.Skill.Level, entry.XBlock)).Order(), Is.EqualTo(new (int, short, string)[] {
            (70000065, 6, "02000389_bf"), (70000065, 6, "02000442_bf"), (70000065, 6, "52100012_qd"), (70000096, 1, "63000028_cs"), (70000096, 1, "63000029_cs"),
        }));
        foreach ((string _, Ms2TriggerSkill turn) in monsterOnly.Where(entry => entry.Skill.SkillId == 70000065)) {
            Assert.That((turn.TriggerId, turn.Count, turn.Position), Is.EqualTo((30010, 1, new Vector3(600, -150, 975))));
        }

        var five = placed.Where(entry => first.TryGetValue((entry.Skill.SkillId, entry.Skill.Level), out SkillMetadataAttack? attack) && attack.Range.ApplyTarget == ApplyTargetType.RegionBuff).ToList();
        SkillMetadataDamage Damage((string XBlock, Ms2TriggerSkill Skill) entry) => first[(entry.Skill.SkillId, entry.Skill.Level)].Damage;

        // The one other ApplyTarget 5 skill that takes all of a player's health in one hit: Blue Lapenta's wipe, unchanged (no capture).
        var whole = five.Where(entry => Damage(entry).Count > 0 && Damage(entry).DamageByTargetMaxHp >= 1 && !FieldSkill.MonsterOnly(entry.Skill.SkillId, entry.Skill.Level)).ToList();
        Assert.That(whole.Select(entry => entry.Skill.SkillId).Distinct(), Is.EqualTo(new[] { 70002001 }));
        Assert.That(whole.Select(entry => entry.XBlock).Order(), Is.EqualTo(new[] { "02020111_bf", "02020111_bf", "02020145_bf", "02020145_bf" }));

        // Count 0 with ApplyTarget 5: 586, none of them lethal by its own damage in the one cast each gets today.
        var countZero = five.Where(entry => entry.Skill.Count == 0).ToList();
        var byHealth = countZero.Where(entry => Damage(entry).Count > 0 && Damage(entry).DamageByTargetMaxHp > 0).ToList();
        var byValue = countZero.Where(entry => Damage(entry).Count > 0 && Damage(entry).DamageByTargetMaxHp == 0).ToList();
        Assert.That((countZero.Count, byHealth.Count, byValue.Count), Is.EqualTo((586, 225, 228)));
        Assert.That(byHealth.Max(entry => Damage(entry).DamageByTargetMaxHp), Is.EqualTo(0.2f));
        Assert.That(byValue.Max(entry => Damage(entry).Value), Is.EqualTo(1L));
    }
}
