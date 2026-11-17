using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using Maple2.Server.Tests.Data;
using Maple2.Tools.Collision;
using Maple2.Tools.VectorMath;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-13 (BT-272): Soul Binder's Fount of Renewal (region 11100172: IncludeCaster 0, friendly, 8 targets) healed no solo player,
/// since a region with IncludeCaster 0 never counted its caster. A player's friendly region now counts him while he stands in it, unless another
/// attack of the same skill takes him by Priority (Eagle Squad 10500291 gives him his own buff 10500293 that way).</summary>
[TestFixture]
public class PlayerRegionCasterTest {
    // 11100172's range: a cylinder of 225.
    private static SkillMetadataRange Range(IncludeCasterType includeCaster, ApplyTargetType applyTarget) =>
        new(SkillRegion.Cylinder, 225, 180, 0, 0, 0, Vector3.Zero, Vector3.Zero, includeCaster, applyTarget, SkillTargetType.Owner);

    [Test]
    public void AFriendlyRegionOfAPlayerCountsHimInIt() {
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Friendly), npcCaster: false, holdsCaster: true, playerCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Last), "Fount of Renewal with him in it");
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Friendly), npcCaster: false, holdsCaster: false, playerCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Exclude), "and out of it");
    }

    [Test]
    public void OtherRegionsKeepTheirRange() {
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Hostile), npcCaster: false, holdsCaster: true, playerCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Exclude), "a hostile attack never hits its caster");
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Friendly), npcCaster: false, holdsCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Exclude), "a map's region");
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Friendly), npcCaster: true, holdsCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Exclude), "an NPC's region");
        Assert.That(FieldSkill.CasterRange(Range(IncludeCasterType.Priority, ApplyTargetType.Friendly), npcCaster: false, holdsCaster: true, playerCaster: true).IncludeCaster,
            Is.EqualTo(IncludeCasterType.Priority), "a region that counts him already");
    }

    private static SkillMetadataLevel Level(params IncludeCasterType[] attacks) => Bare<SkillMetadataLevel>() with {
        Motions = [Bare<SkillMetadataMotion>() with { Attacks = attacks.Select(includeCaster => Bare<SkillMetadataAttack>() with { Range = Range(includeCaster, ApplyTargetType.Friendly) }).ToArray() }],
    };

    // Fount of Renewal 11100172 has the one attack; Eagle Squad 10500291 a friendly Exclude attack and a Priority one for his own buff.
    [Test]
    public void ASkillWithAPriorityAttackLeavesTheCasterToIt() {
        Assert.That(FieldSkill.TakesCasterByPriority(Level(IncludeCasterType.Exclude)), Is.False, "Fount of Renewal's shape");
        Assert.That(FieldSkill.TakesCasterByPriority(Level(IncludeCasterType.Exclude, IncludeCasterType.Last)), Is.False);
        Assert.That(FieldSkill.TakesCasterByPriority(Level(IncludeCasterType.Exclude, IncludeCasterType.Priority)), Is.True, "Eagle Squad's shape");
    }

    // "Last" fills the pool first: with as many other players as the target count the caster is left out, with fewer he is the last target.
    [Test]
    public void AFullPoolLeavesTheCasterOut() {
        SkillMetadataRange fount = FieldSkill.CasterRange(Range(IncludeCasterType.Exclude, ApplyTargetType.Friendly), npcCaster: false, holdsCaster: true, playerCaster: true);
        var field = Bare<FieldManager>();
        SetField(field, "<Players>k__BackingField", new ConcurrentDictionary<int, FieldPlayer>());
        FieldPlayer caster = Placed(1), first = Placed(2), second = Placed(3);
        foreach (FieldPlayer player in new[] { caster, first, second }) {
            field.Players[player.ObjectId] = player;
        }
        Prism[] prisms = [fount.GetPrism(Vector3.Zero, 0, fount.ApplyTarget)];
        Assert.That(field.GetTargets(caster, prisms, fount, 2).ToArray(), Is.EquivalentTo(new IActor[] { first, second }), "a full pool");
        Assert.That(field.GetTargets(caster, prisms, fount, 8).ToArray(), Is.EquivalentTo(new IActor[] { first, second, caster }), "a solo or small party");
    }

    // The 11 friendly player regions with IncludeCaster Exclude, from the data: whether the caster counts (PI-13's list).
    [TestCase(10100101, true)] // Defender of the Faith: no other attack takes him
    [TestCase(10400151, false)] // Heavenly Wings: attack 1 takes him by Priority (10400152)
    [TestCase(10400153, false)] // same shape
    [TestCase(10400161, true)] // Angelic Ray: attack 0 hostile, attack 1 friendly Exclude
    [TestCase(10400201, true)]
    [TestCase(10400212, true)] // Divine Wave: one attack
    [TestCase(10500291, false)] // Eagle Squad: attack 1 takes him by Priority (10500293)
    [TestCase(11100172, true)] // Fount of Renewal: one attack
    [TestCase(19900032, true)] // GM splashes: one attack
    [TestCase(19900042, true)]
    [TestCase(19900052, true)]
    [Category("Data")]
    public void TheRealSkillsCountTheirCasterAsListed(int skillId, bool counted) {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        StoredSkillMetadata skill = context.SkillMetadata.Single(stored => stored.Id == skillId);
        foreach (SkillMetadataLevel level in skill.Levels.Values) {
            SkillMetadataAttack friendly = level.Motions.SelectMany(motion => motion.Attacks)
                .First(attack => attack.Range is { ApplyTarget: ApplyTargetType.Friendly, IncludeCaster: IncludeCasterType.Exclude });
            IncludeCasterType result = FieldSkill.CasterRange(friendly.Range, npcCaster: false, holdsCaster: true, playerCaster: !FieldSkill.TakesCasterByPriority(level)).IncludeCaster;
            Assert.That(result, Is.EqualTo(counted ? IncludeCasterType.Last : IncludeCasterType.Exclude));
        }
    }

    private static FieldPlayer Placed(int objectId) {
        var player = Bare<FieldPlayer>();
        SetField(player, "<ObjectId>k__BackingField", objectId);
        SetField(player, "<Transform>k__BackingField", new Transform { Position = Vector3.Zero });
        return player;
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }
}
