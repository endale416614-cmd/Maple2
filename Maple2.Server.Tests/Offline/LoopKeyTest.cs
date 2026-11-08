using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>NB-19b (BT-212): a motion with a LoopCount plays its sequence's loop 1 + LoopCount times: the keys in the loop every pass,
/// end and the keys after loopend once, after the last pass. The keys are the data's, in its order (end before loopend); the counts and
/// times are the GMS captures' (SkillUse to SkillDamage Target, and to the next motion's SkillUse).</summary>
[TestFixture]
public class LoopKeyTest {
    private static readonly AnimationKey End = new("end", 0.633333f);
    private static readonly AnimationKey LoopEnd = new("loopend", 0.613f);
    private static readonly AnimationKey P0 = new("p0", 0.1f);

    // Ishura 23000113 (23000113_B_IshuraRbladerDark), Attack_02_D.
    private static AnimationRecord Ishura(int loopCount) {
        var sequence = new AnimationSequenceMetadata("Attack_02_D", 24, 0.633333f,
            [new AnimationKey("start", 0), End, new AnimationKey("emo_skill", 0), new AnimationKey("loopstart", 0.02f), LoopEnd, P0]);
        return new AnimationRecord(sequence, 1f, AnimationType.Skill, loopCount: loopCount);
    }

    // Skill 50001336 L1, motion 1 (LoopCount 5): p0 and loopend play in every pass, end only after the sixth, even in a tick that passes
    // loopend and end at once. Captured: p0 six times a cast (5 casts), the next motion's SkillUse at 3.60 s (0.633 + 5 x 0.593). The
    // attacks no key names fire at each loopend while the record loops, and at end: 1 + LoopCount times (Dark Woodada 21000082,
    // LoopCount 1: captured at 0.50 and 0.98 s).
    [Test]
    public void ALoopCountOfFivePlaysTheLoopSixTimes() {
        AnimationRecord record = Ishura(5);
        int passes = 0;
        while (!AnimationManager.KeyPlays(record, End, 0.6f, 0.64f)) {
            Assert.That(passes, Is.LessThan(5), "end plays after the sixth pass");
            Assert.That(AnimationManager.KeyPlays(record, P0, 0.09f, 0.11f), Is.True, $"p0 in pass {passes + 1}");
            Assert.That(AnimationManager.KeyPlays(record, LoopEnd, 0.6f, 0.62f), Is.True, $"loopend in pass {passes + 1}");
            Assert.That(record.IsLooping, Is.True, "another pass follows this loopend");
            record.LoopPassed();
            passes++;
        }

        Assert.That(passes, Is.EqualTo(5));
        Assert.That(record.IsLooping, Is.False, "the last pass's loopend fires no attack; end does");
        Assert.That(AnimationManager.KeyPlays(record, P0, 0.09f, 0.11f), Is.True, "p0 in the last pass");
    }

    // Without a LoopCount the sequence plays straight through, as before.
    [Test]
    public void WithoutALoopCountEndPlaysAtOnce() {
        AnimationRecord record = Ishura(0);

        Assert.That(AnimationManager.KeyPlays(record, End, 0.6f, 0.64f), Is.True);
        Assert.That(AnimationManager.KeyPlays(record, P0, 0.1f, 0.2f), Is.False, "a key plays only in the tick that passes it");
    }

    // Black Birk 40000056 (02020036_M_Konovis), skill 40027021 motion 0 (LoopCount 2): Attack_01_B has end and loopend both at 0.4, so
    // end waits for the third pass there too.
    [Test]
    public void EndAtLoopendWaitsForTheLastPass() {
        var end = new AnimationKey("end", 0.4f);
        var sequence = new AnimationSequenceMetadata("Attack_01_B", 1, 0.4f,
            [new AnimationKey("start", 0), end, new AnimationKey("emo_skill", 0), new AnimationKey("loopstart", 0.02f), new AnimationKey("loopend", 0.4f)]);
        var record = new AnimationRecord(sequence, 1f, AnimationType.Skill, loopCount: 2);

        Assert.That(AnimationManager.KeyPlays(record, end, 0.39f, 0.41f), Is.False, "first pass");
        record.LoopPassed();
        Assert.That(AnimationManager.KeyPlays(record, end, 0.39f, 0.41f), Is.False, "second pass");
        record.LoopPassed();
        Assert.That(AnimationManager.KeyPlays(record, end, 0.39f, 0.41f), Is.True, "third pass");
    }

    // Mason 24120001 (11003537_M_Mason), skill 60200007 motion 0 (LoopCount 1): Attack_01_G has no loop keys, so it plays straight to its
    // end and the cast goes on (the captures hit p0 a second time, at 1.11 s, which this does not model).
    [Test]
    public void ALoopCountWithoutLoopKeysPlaysStraight() {
        var end = new AnimationKey("end", 1f);
        var p0 = new AnimationKey("p0", 0.95f);
        var sequence = new AnimationSequenceMetadata("Attack_01_G", 15, 1f, [new AnimationKey("start", 0), end, new AnimationKey("emo_skill", 0), p0]);
        var record = new AnimationRecord(sequence, 1f, AnimationType.Skill, loopCount: 1);

        Assert.That(AnimationManager.KeyPlays(record, p0, 0.94f, 0.96f), Is.True);
        Assert.That(AnimationManager.KeyPlays(record, end, 0.99f, 1.01f), Is.True);
    }
}
