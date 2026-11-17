using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-30 (BT-223, BT-219): the shield-break AI values of an NPC's shield. A reconstruction: no capture of the original's writes.
/// The real AI record of Blue Lapenta 21440002 (phase nodes on brokenShieldSuccess, brokenShieldFail and remainShieldHP >= 1) is read
/// from the data; a node is usable when its ExtraData condition holds against the NPC's AI extra data.</summary>
[Category("Data")]
[NonParallelizable]
public class ShieldBreakAiTest {
    private const string LapentaAi = "BossDungeon/EyeOfLapenta/AI_BlueLapenta.xml";
    private const int Lapenta = 21440002, Shield = 62100016; // Shield: 95,000 health at level 1, 15 s

    private static FieldRig Open() {
        FieldRig? rig = FieldRig.TryOpen();
        if (rig == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        return rig!;
    }

    /// <summary>Whether the AI record's ExtraData condition on <paramref name="key"/> holds for the NPC.</summary>
    private static bool Usable(AiMetadata ai, FieldNpc npc, string key) {
        AiMetadata.ExtraDataCondition? condition = Find(ai.Battle, key);
        Assert.That(condition, Is.Not.Null, $"{LapentaAi} has a condition on {key}");
        return AiState.PerformOperation(condition!.Op, condition.Value, npc.AiExtraData.GetValueOrDefault(key));
    }

    private static AiMetadata.ExtraDataCondition? Find(AiMetadata.Entry[] entries, string key) {
        foreach (AiMetadata.Entry entry in entries) {
            if (entry is AiMetadata.ExtraDataCondition match && match.Key == key) {
                return match;
            }
            AiMetadata.Entry[] children = entry switch {
                AiMetadata.ConditionsNode conditions => [.. conditions.Conditions, .. conditions.Entries],
                AiMetadata.Node node => node.Entries,
                _ => [],
            };
            if (Find(children, key) is { } found) {
                return found;
            }
        }
        return null;
    }

    private static (FieldNpc Npc, AiMetadata Ai) Shielded(FieldRig rig) {
        Assert.That(rig.Field.AiMetadata.TryGet(LapentaAi, out AiMetadata? ai), Is.True);
        FieldNpc npc = rig.SpawnNpc(Lapenta, new System.Numerics.Vector3(3000, 0, 0)); // out of sight: its own AI does not act
        npc.Buffs.AddBuff(npc, npc, Shield, 1, rig.Field.FieldTick);
        return (npc, ai!);
    }

    [Test]
    public void ABrokenShieldMakesTheSuccessNodeUsableAndNotBefore() {
        using FieldRig rig = Open();
        (FieldNpc npc, AiMetadata ai) = Shielded(rig);

        Assert.That(npc.AiExtraData["remainShieldHP"], Is.EqualTo(95000));
        Assert.That(Usable(ai, npc, "remainShieldHP"), Is.True, "the shield stands: the attack-with-shield node");
        Assert.That(Usable(ai, npc, "brokenShieldSuccess"), Is.False, "not before");
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.False);

        npc.Buffs.EnumerateBuffs(Shield).Single().ShieldHealth = 1;
        var hit = new DamagePropertyRecord { Rate = 1000f, AttackType = AttackType.Physical, CanCrit = false, SuperArmorBreak = true };
        FieldNpc attacker = rig.SpawnNpc(21000012, new System.Numerics.Vector3(2000, 0, 0));
        DamageCalculator.CalculateDamage(attacker, npc, hit);

        Assert.That(npc.Buffs.EnumerateBuffs(Shield), Is.Empty, "the hit broke the shield");
        Assert.That(Usable(ai, npc, "brokenShieldSuccess"), Is.True);
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.False);
        Assert.That(Usable(ai, npc, "remainShieldHP"), Is.False);
    }

    [Test]
    public void AnExpiredShieldMakesTheFailNodeUsableAndNotBefore() {
        using FieldRig rig = Open();
        (FieldNpc npc, AiMetadata ai) = Shielded(rig);

        rig.Advance(10_000);
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.False, "not before the shield ends");
        Assert.That(Usable(ai, npc, "remainShieldHP"), Is.True);

        rig.Advance(6_000);
        Assert.That(npc.Buffs.EnumerateBuffs(Shield), Is.Empty, "the 15 s shield ended");
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.True);
        Assert.That(Usable(ai, npc, "brokenShieldSuccess"), Is.False);
        Assert.That(Usable(ai, npc, "remainShieldHP"), Is.False);
    }

    [Test]
    public void ANewShieldResetsTheValues() {
        using FieldRig rig = Open();
        (FieldNpc npc, AiMetadata ai) = Shielded(rig);
        rig.Advance(16_000);
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.True);

        npc.Buffs.AddBuff(npc, npc, Shield, 1, rig.Field.FieldTick);
        Assert.That(Usable(ai, npc, "brokenShieldFail"), Is.False);
        Assert.That(npc.AiExtraData["remainShieldHP"], Is.EqualTo(95000));
    }
}
