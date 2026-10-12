using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;

namespace Maple2.Server.Tests.Offline;

/// <summary>Regressions found while play-testing the offline edition.</summary>
public class TargetingAndStateSkillTest {
    // Demiporker Spearpig (29000234), one of the Crisis in Henesys wave monsters.
    private static readonly NpcMetadataDistance WaveMob = new(Avoid: 60, Sight: 6000, SightHeightUp: 150, SightHeightDown: 150,
        LastSightRadius: 6000, LastSightHeightUp: 150, LastSightHeightDown: 150);

    [Test]
    public void FarTargetInSight_IsKept() {
        // A straggler that stayed idle by its spawn across the moat, and the area where the player fought (from the trace).
        var straggler = new Vector3(-1934, 694, 1655);
        var player = new Vector3(879, -1030, 1655);
        Assert.That(Vector3.Distance(straggler, player), Is.GreaterThan(1200), "farther than the AI's retarget range");

        // The AI's target node (nearAssociated 10..1200) used to decide this too, so the monster dropped the player.
        Assert.That(BattleState.WithinLastSight(straggler, player, WaveMob), Is.True);
    }

    [Test]
    public void TargetOutOfSight_IsDropped() {
        var npc = new Vector3(0, 0, 1655);
        Assert.That(BattleState.WithinLastSight(npc, new Vector3(6001, 0, 1655), WaveMob), Is.False, "beyond sight radius");
        Assert.That(BattleState.WithinLastSight(npc, new Vector3(100, 0, 1655 + 151), WaveMob), Is.False, "too far above");
        Assert.That(BattleState.WithinLastSight(npc, new Vector3(100, 0, 1655 - 161), WaveMob), Is.False, "too far below");
        Assert.That(BattleState.WithinLastSight(npc, new Vector3(100, 0, 1655 + 150), WaveMob), Is.True);
    }

    [Test]
    public void AirMountGlide_SurvivesTakeoffAndEndsOnLanding() {
        // Air mounts are started mid-jump; the glide must not be cancelled while the client still reports Jump/Fall.
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Glide, ActorState.Glide), Is.True);
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Glide, ActorState.Jump), Is.True);
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Glide, ActorState.Fall), Is.True);
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Glide, ActorState.Idle), Is.False);
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Glide, ActorState.Land), Is.False);
        // Other state skills keep the strict rule.
        Assert.That(FieldPlayer.IsStillInSkillState(ActorState.Swim, ActorState.Jump), Is.False);
    }
}
