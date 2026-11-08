using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>SE-03: the three map-placed skills with ApplyTarget 7 are the ones the rule was written for, and which of their placements repeat (BT-93).</summary>
[Category("Data")]
[NonParallelizable]
public class ApplyTargetRegionDataTest {
    [Test]
    public void TheThreeSkillsAndTheirPlacements() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        // Skill and level to the first ApplyTarget 7 attack, for every level that holds one.
        var carriers = new Dictionary<(int Id, short Level), SkillMetadataAttack>();
        foreach (StoredSkillMetadata skill in context.SkillMetadata) {
            foreach ((short level, SkillMetadataLevel data) in skill.Levels) {
                IEnumerable<SkillMetadataAttack> attacks = data.Motions.SelectMany(motion => motion.Attacks);
                SkillMetadataAttack? attack = attacks.FirstOrDefault(attack => attack.Range.ApplyTarget == ApplyTargetType.Player4);
                if (attack != null) {
                    carriers[(skill.Id, level)] = attack;
                }
            }
        }
        Assert.That(carriers.Keys.Select(key => key.Id).Distinct().Count(), Is.EqualTo(37));

        SkillMetadataAttack scoreBox = carriers[(70000569, 1)];
        Assert.That((scoreBox.Range.Type, scoreBox.Range.Distance, scoreBox.Range.Width, scoreBox.Range.Height), Is.EqualTo((SkillRegion.Box, 450f, 450f, 450f)));
        Assert.That((scoreBox.TargetCount, scoreBox.Damage.DamageByTargetMaxHp), Is.EqualTo((99, 1f)));
        SkillMetadataAttack flame = carriers[(70000051, 1)];
        Assert.That((flame.TargetCount, flame.Damage.DamageByTargetMaxHp), Is.EqualTo((4, 0.05f)));
        SkillMetadataAttack blast = carriers[(70000065, 7)];
        Assert.That((blast.Range.Distance, blast.Range.Height, blast.Damage.IsConstDamage, blast.Damage.Value), Is.EqualTo((6000f, 1500f, true, 250L)));

        List<(string XBlock, Ms2TriggerSkill Skill)> placed = context.MapEntity.AsEnumerable()
            .Where(entity => entity.Block is Ms2TriggerSkill)
            .Select(entity => (entity.XBlock, (Ms2TriggerSkill) entity.Block))
            .ToList();
        var seven = placed.Where(entry => carriers.ContainsKey((entry.Skill.SkillId, entry.Skill.Level))).ToList();
        Assert.That(seven.Select(entry => entry.Skill.SkillId).Distinct().Order(), Is.EqualTo(new[] { 70000051, 70000065, 70000569 }));
        Assert.That(placed.Count(entry => entry.Skill.SkillId == 70000065), Is.EqualTo(93), "every level");
        Assert.That(seven.Where(entry => entry.Skill.SkillId == 70000065).Select(entry => entry.XBlock).Order(), Is.EqualTo(new[] { "02000389_bf", "02000442_bf", "52100012_qd" }));
        Assert.That(seven.Count(entry => entry.Skill.SkillId == 70000051 && entry.XBlock == "02000252_bf"), Is.EqualTo(457));
        Assert.That(seven.Count(entry => entry.Skill.SkillId == 70000051), Is.EqualTo(457));

        (string XBlock, Ms2TriggerSkill Skill) skill700 = seven.Single(entry => entry.Skill.SkillId == 70000569);
        Assert.That(skill700.XBlock, Is.EqualTo("52010038_qd"));
        Assert.That((skill700.Skill.TriggerId, skill700.Skill.Position), Is.EqualTo((700, new Vector3(-7425, 3300, 5175))));

        // Of these only 70000569 and 70000051's 5 tiles of its 1 s timers have count 0, so they repeat until a script turns them off (SE-05, TriggerRepeatDataTest).
        Assert.That(seven.Where(entry => entry.Skill.Count == 0).Select(entry => entry.Skill.TriggerId).Order(), Is.EqualTo(new[] { 564, 565, 566, 567, 568, 700 }));
    }
}
