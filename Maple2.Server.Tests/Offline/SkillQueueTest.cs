using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Game.PacketHandlers;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>SK-12: a held skill's cast outlasts what the player does meanwhile (BT-162).</summary>
[TestFixture]
public class SkillQueueTest {
    private static SkillRecord Cast(long uid, ActorState state = ActorState.None) {
        var property = new SkillMetadataProperty(default, default, default, default, default, state, false, false, false, false, false, false, 0, 0, string.Empty);
        return new SkillRecord(new SkillMetadata(10400121, 1, null, property, null!, null!), uid, null!);
    }

    [Test]
    public void AHeldCastOutlastsFifteenLaterCasts() {
        var queue = new SkillQueue();
        queue.Add(Cast(1));
        for (long uid = 2; uid <= 16; uid++) {
            queue.Add(Cast(uid));
        }
        Assert.That(queue.Get(1), Is.Not.Null);

        queue.Add(Cast(17));

        Assert.That(queue.Get(1), Is.Null);
        Assert.That(queue.GetMostRecent()!.CastUid, Is.EqualTo(17));
    }

    [Test]
    public void AStateCastTakesNoSlot() {
        var queue = new SkillQueue();
        queue.Add(Cast(1));
        for (long uid = 2; uid <= 101; uid++) {
            queue.AddState(Cast(uid, ActorState.SwimDash));
        }

        Assert.That(queue.Get(1), Is.Not.Null);
        Assert.That(queue.Get(101), Is.Null);
        Assert.That(queue.GetMostRecent()!.CastUid, Is.EqualTo(1));
        Assert.That(queue.StateSkill!.CastUid, Is.EqualTo(101));
    }

    [Test]
    public void OnlyACastWithAStateBecomesTheStateSkill() {
        var queue = new SkillQueue();
        queue.AddState(Cast(1));
        queue.Add(Cast(2));
        Assert.That(queue.StateSkill, Is.Null);

        queue.Add(Cast(3, ActorState.SwimDash));

        Assert.That(queue.StateSkill!.CastUid, Is.EqualTo(3));
        Assert.That(queue.Get(3), Is.Not.Null);
    }

    [Test]
    public void OnlyACastWithASequencePlaysAMotion() {
        Assert.That(SkillHandler.PlaysMotion("Priest_HolySpirit_A"), Is.True);
        Assert.That(SkillHandler.PlaysMotion(string.Empty), Is.False);
        Assert.That(SkillHandler.PlaysMotion(null), Is.False);
    }
}
