using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-19 (BT-68, C6): an NPC attack that no key of its sequence names fires at the sequence's end, and every attack fires at once
/// when the NPC's model has no such sequence. The motions and keys are the data's, the times the GMS captures' (SkillUse to SkillDamage Target).</summary>
[TestFixture]
public class KeylessAttackTest {
    private static SkillMetadataMotion Motion(string sequence, params string[] points) {
        SkillMetadataAttack[] attacks = points.Select(point => new SkillMetadataAttack(point, 0, 0, 0, 0, 0, 0, [], null, null!, null!, null!, [], [])).ToArray();
        return new SkillMetadataMotion(new SkillMetadataMotionProperty(sequence, 1, 0, 0, true, 0), attacks, new Dictionary<string, byte>());
    }

    private static AnimationSequenceMetadata Sequence(string name, float end, params AnimationKey[] keys) {
        return new AnimationSequenceMetadata(name, 0, end, [new AnimationKey("start", 0), new AnimationKey("end", end), .. keys]);
    }

    // Bubble Friller 32001201, 49104011: p0 hits at its key (1.07 s; captured 1.13 s), p1 to p3 have none and hit at the end
    // (1.73 s; captured 1.71 to 1.74 s).
    [Test]
    public void AttacksWithoutAKey_FireAtTheEnd() {
        AnimationSequenceMetadata attack = Sequence("Attack_01_B", 1.733333f, new AnimationKey("emo_skill", 0), new AnimationKey("p0", 1.0677f));

        Assert.That(MovementState.KeylessAttackPoints(Motion("Attack_01_B", "p0", "p1", "p2", "p3"), attack), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    // Ishura 21421702, 10900062: the keys are P0 and P1, so p0, p1 and p2 all hit at the end (1.1 s; captured 1.10 s, 77 casts).
    [Test]
    public void KeyNamesKeepTheirCase() {
        AnimationSequenceMetadata splitter = Sequence("RuneBlader_TripleSplitter_A", 1.1f, new AnimationKey("P0", 0.15f), new AnimationKey("P1", 0.38f));

        Assert.That(MovementState.KeylessAttackPoints(Motion("RuneBlader_TripleSplitter_A", "p0", "p1", "p2"), splitter), Is.EqualTo(new byte[] { 0, 1, 2 }));
    }

    // Ishura 21900095, 10900062: her model has no RuneBlader_TripleSplitter_A, so the three attacks hit at once (captured 0.0 s, 5 casts).
    [Test]
    public void WithoutTheSequence_EveryAttackFires() {
        Assert.That(MovementState.KeylessAttackPoints(Motion("RuneBlader_TripleSplitter_A", "p0", "p1", "p2"), null), Is.EqualTo(new byte[] { 0, 1, 2 }));
    }

    // A point a key names fires at that key only; two attacks that share a name no key has both fire at the end.
    [Test]
    public void OnlyThePointsNoKeyNames() {
        AnimationSequenceMetadata keyed = Sequence("Attack_01_A", 1f, new AnimationKey("p0", 0.4f), new AnimationKey("p1", 0.6f));

        Assert.That(MovementState.KeylessAttackPoints(Motion("Attack_01_A", "p0", "p1", "p0"), keyed), Is.Empty);
        Assert.That(MovementState.KeylessAttackPoints(Motion("Attack_01_A", "p2", "p0", "p2"), keyed), Is.EqualTo(new byte[] { 0, 2 }));
    }
}
