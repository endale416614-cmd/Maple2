using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Formulas;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.VectorMath;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class DefensiveEffectsTest {
    [Test]
    public void DamageTakenMultiplier_EachBuffMultiplies() {
        Assert.That(Damage.DamageTakenMultiplier([]), Is.EqualTo(1).Within(1e-6));
        Assert.That(Damage.DamageTakenMultiplier([0.25f]), Is.EqualTo(0.75).Within(1e-6), "Iron Shield level 1");
        Assert.That(Damage.DamageTakenMultiplier([0.8f]), Is.EqualTo(0.2).Within(1e-6), "Iron Shield level 12");
        Assert.That(Damage.DamageTakenMultiplier([0.25f, 0.1f]), Is.EqualTo(0.675).Within(1e-6), "with Iron Defense");
        Assert.That(Damage.DamageTakenMultiplier([1.2f]), Is.EqualTo(0).Within(1e-6), "never below 0");
    }

    [Test]
    public void Parse() {
        var config = DefensiveEffects.Parse("""{"Effects": {"10100151": {"1": [0.25, 0.25], "12": [0.8, 0.8]}}}""");
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects[10100151][12], Is.EqualTo(new[] { 0.8f, 0.8f }));
    }

    [Test]
    public void ShippedFile_IronShieldAndIronDefense() {
        string path = Path.Combine(Paths.SOLUTION_DIR, "defensive-effects.json");
        Assert.That(File.Exists(path), Is.True, "defensive-effects.json ships next to the solution");
        var config = DefensiveEffects.Parse(File.ReadAllText(path));
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Effects[10100151][1], Is.EqualTo(new[] { 0.25f, 0.25f }), "Iron Shield level 1");
        Assert.That(config.Effects[10100151][12], Is.EqualTo(new[] { 0.8f, 0.8f }), "Iron Shield level 12");
        Assert.That(config.Effects.ContainsKey(10100152), Is.False, "the PvP copy is left out");
        Assert.That(config.Effects[10100061][1], Is.EqualTo(new[] { 0.1f, 0.1f }), "Iron Defense");
        Assert.That(DefensiveEffects.Rate(10100151, 12, magic: true), Is.EqualTo(0.8f));
        Assert.That(DefensiveEffects.Rate(999999, 1, magic: false), Is.EqualTo(0), "unknown effect");
        Assert.That(DefensiveEffects.Rate(10100151, 99, magic: false), Is.EqualTo(0), "unknown level");
    }

    // NB-21: DefensiveProperty invincible from the NA client, 122 levels of 107 effects.
    [Test]
    public void ShippedFile_InvincibleValues() {
        var config = DefensiveEffects.Parse(File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "defensive-effects.json")));
        Assert.That(config?.Invincible, Is.Not.Null);
        Dictionary<int, int> byValue = config!.Invincible!.Values.SelectMany(levels => levels.Values).GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        Assert.That(byValue, Is.EquivalentTo(new Dictionary<int, int> { [1] = 26, [2] = 90, [3] = 5, [150] = 1 }));
        Assert.That(config.Invincible.Count, Is.EqualTo(107));
        Assert.That(DefensiveEffects.Invincible(69000401, 1), Is.EqualTo(1), "Mason 24120005's spawn buff");
        Assert.That(DefensiveEffects.Invincible(50170019, 1), Is.EqualTo(2), "Pink Bean 23502021's pogo stick");
        Assert.That(DefensiveEffects.Invincible(70000107, 1), Is.EqualTo(3), "the map scripts' hold");
        Assert.That(DefensiveEffects.Invincible(10100151, 1), Is.EqualTo(0), "Iron Shield");
    }

    // NB-21: the hit an invincible target takes, as the GMS captures show it: type 4 for 0 while an enabled buff has invincible 1, type 8
    // for 0 with 2; invincible 3 (sent disabled in its one capture), a disabled buff and a buff past its end change nothing.
    [Test]
    public void InvincibleHit_AsCaptured() {
        Assert.That(DefensiveEffects.InvincibleHit([Effect(69000401)], 0), Is.EqualTo((DamageType) 4), "Mason 24120005, Mason Phases.msb");
        Assert.That(DefensiveEffects.InvincibleHit([Effect(50170019)], 0), Is.EqualTo((DamageType) 8), "Pink Bean 23502021, Port_54344-6028F.msb");
        Assert.That(DefensiveEffects.InvincibleHit([Effect(50170019), Effect(69000401)], 0), Is.EqualTo(DamageType.Invincible), "1 wins over 2");
        Assert.That(DefensiveEffects.InvincibleHit([Effect(70000107)], 0), Is.Null, "invincible 3");
        Assert.That(DefensiveEffects.InvincibleHit([Effect(10100151)], 0), Is.Null, "Iron Shield");
        Assert.That(DefensiveEffects.InvincibleHit([], 0), Is.Null);
        Assert.That(DefensiveEffects.InvincibleHit([Effect(50170019, 5000)], 5000), Is.EqualTo((DamageType) 8), "at its end");
        Assert.That(DefensiveEffects.InvincibleHit([Effect(50170019, 5000)], 5001), Is.Null, "past its end");
        Buff disabled = Effect(69000401);
        disabled.Disable();
        Assert.That(DefensiveEffects.InvincibleHit([disabled], 0), Is.Null, "a disabled buff");
    }

    // NB-21d (BT-218): a defensive-effects.json that cannot be read (another handle holds it without sharing) gives no config, as a
    // missing file does; today the IOException leaves Load, and the Lazy would throw it on every hit on a buffed target. Load is called
    // directly and the variable put back, so the other tests keep the shipped config.
    [Test]
    [NonParallelizable] // DEFENSIVE_EFFECTS_FILE is process-wide.
    public void Load_LockedFileGivesNoConfig() {
        MethodInfo load = typeof(DefensiveEffects).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;
        string? before = Environment.GetEnvironmentVariable("DEFENSIVE_EFFECTS_FILE");
        string path = Path.GetTempFileName();
        try {
            File.WriteAllText(path, """{"Effects": {"10100151": {"1": [0.25, 0.25]}}}""");
            Environment.SetEnvironmentVariable("DEFENSIVE_EFFECTS_FILE", path);
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Assert.That(load.Invoke(null, null), Is.Null, "a locked file");
            }
            Assert.That(load.Invoke(null, null), Is.Not.Null, "the same file, no longer locked");
        } finally {
            Environment.SetEnvironmentVariable("DEFENSIVE_EFFECTS_FILE", before);
            File.Delete(path);
        }
    }

    // NB-21: hits by another actor on an NPC holding Pink Bean's 50170019 are one type 8 entry for 0 each, whatever the hit count, as
    // captured (120020198 level 7, count 3; 10900207 level 5, count 0); its own hit without damage gives none. Today the first takes 300
    // health (and needs a whole field to do it), and the second gives no entry.
    [Test]
    public void ApplyDamage_InvincibleTargetTakesNoDamage() {
        FieldNpc caster = Npc(1), target = Npc(2);
        var buffs = new BuffManager(target);
        var store = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(buffs)!;
        store[50170019] = [Effect(50170019)];
        Set(target, "<Buffs>k__BackingField", buffs);
        var attack = Bare<SkillMetadataAttack>() with { CompulsionTypes = [], Damage = Bare<SkillMetadataDamage>() with { Count = 3, IsConstDamage = true, Value = 100 } };
        var damage = new DamageRecord(new SkillMetadata(120020198, 7, null, Bare<SkillMetadataProperty>(), null!, null!), attack);
        target.ApplyDamage(caster, damage, attack);
        Assert.That(damage.Targets[2].Damage, Is.EqualTo(new[] { ((DamageType) 8, 0L) }), "a hit with count 3");

        var none = attack with { Damage = attack.Damage with { Count = 0 } };
        var noDamage = new DamageRecord(new SkillMetadata(10900207, 5, null, Bare<SkillMetadataProperty>(), null!, null!), none);
        target.ApplyDamage(caster, noDamage, none);
        Assert.That(noDamage.Targets[2].Damage, Is.EqualTo(new[] { ((DamageType) 8, 0L) }), "a hit with count 0");
        var own = new DamageRecord(new SkillMetadata(10900207, 5, null, Bare<SkillMetadataProperty>(), null!, null!), none);
        target.ApplyDamage(target, own, none);
        Assert.That(own.Targets[2].Damage, Is.Empty, "its own hit with count 0");
    }

    private static FieldNpc Npc(int objectId) {
        var npc = Bare<FieldNpc>();
        Set(npc, "<ObjectId>k__BackingField", objectId);
        Set(npc, "<Transform>k__BackingField", new Transform());
        Set(npc, "<Field>k__BackingField", Bare<FieldManager>());
        return npc;
    }

    private static void Set(object target, string name, object value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    // Effect id at level 1, held from tick 0 to endTick (0: no end).
    private static Buff Effect(int id, long endTick = 0) {
        var owner = Bare<FieldNpc>();
        var metadata = new AdditionalEffectMetadata(id, 1, Bare<BeginCondition>(), Bare<AdditionalEffectMetadataProperty>(), null!, null!, null!, null!, null, null!, null, null, [], [], []);
        return new Buff(metadata, id, owner, owner, 0, endTick, 0);
    }
}
