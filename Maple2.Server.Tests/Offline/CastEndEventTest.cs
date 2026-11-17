using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Enum;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-07 (BT-262): event 14 (OnSkillCastEnd) was raised at every attack-point packet, 0.2 s into Shield Booster's motion, so its
/// +450 resistance and counterattack chance lasted 0.2 s instead of the captured 1.29 s. It is now raised where the motion ends.</summary>
[TestFixture]
public class CastEndEventTest {
    private static SkillMetadata Skill(int id) => new(id, 1, null, (SkillMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(SkillMetadataProperty)), null!, null!);

    [Test]
    public void TheEndKeyOfASkillMotionNamesTheSkill() {
        var record = new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161));
        Assert.That(FieldPlayer.CastEndSkillId("end", record), Is.EqualTo(10100161));
    }

    [Test]
    public void OtherKeysAndOtherMotionsNameNothing() {
        var record = new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161));
        Assert.That(FieldPlayer.CastEndSkillId("attack", record), Is.EqualTo(0));
        Assert.That(FieldPlayer.CastEndSkillId("end", new AnimationRecord(null!, 1, AnimationType.Move)), Is.EqualTo(0));
        Assert.That(FieldPlayer.CastEndSkillId("end", null), Is.EqualTo(0));
    }

    [Test]
    public void TheEndKeyRaisesTheEventOnceForThePlayingSkill() {
        Probe player = Player(new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161)));
        player.KeyframeEvent("attack");
        Assert.That(player.Raised, Is.Empty, "another key");
        player.KeyframeEvent("end");
        Assert.That(player.Raised, Is.EqualTo(new[] { (EventConditionType.OnSkillCastEnd, 10100161) }));
    }

    [Test]
    public void AReplacedMotionRaisesNothingForTheFirstSkill() {
        Probe player = Player(new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161)));
        player.Animation.Current = new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10600211)); // the next cast, before the first one's end key
        player.KeyframeEvent("end");
        Assert.That(player.Raised, Is.EqualTo(new[] { (EventConditionType.OnSkillCastEnd, 10600211) }), "only the playing skill");
    }

    [Test]
    public void ACancelBeforeTheEndKeyRaisesTheEventOnceAndAfterItNone() {
        Probe player = Player(new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161)));
        player.CancelCastEnd();
        Assert.That(player.Raised, Is.EqualTo(new[] { (EventConditionType.OnSkillCastEnd, 10100161) }));

        Probe ended = Player(new AnimationRecord(null!, 1, AnimationType.Skill, Skill(10100161)) { EndTick = 5000 }); // the end key was hit
        ended.CancelCastEnd();
        Assert.That(ended.Raised, Is.Empty);

        Probe misc = Player(new AnimationRecord(null!, 1, AnimationType.Misc));
        misc.CancelCastEnd();
        Assert.That(misc.Raised, Is.Empty, "no skill motion");
    }

    /// <summary>A player whose real buff manager holds one enabled buff, so an event reaches ApplyEffects, which records it.</summary>
    private sealed class Probe : FieldPlayer {
        public List<(EventConditionType Type, int SkillId)> Raised = []; // an uninitialized instance runs no initializer: Create sets it
        private BuffManager? buffs;

        private Probe() : base(null!, null!) { }

        public override BuffManager Buffs => buffs ??= Listener(this);

        public override void ApplyEffects(SkillEffectMetadata[] effects, IActor caster, IActor owner, EventConditionType type = EventConditionType.Activate, int skillId = 0, int buffId = 0, params IActor[] targets) {
            Raised.Add((type, skillId));
        }

        public static Probe Create() {
            var probe = (Probe) RuntimeHelpers.GetUninitializedObject(typeof(Probe));
            probe.Raised = [];
            return probe;
        }
    }

    private static Probe Player(AnimationRecord playing) {
        Probe player = Probe.Create();
        var animation = (AnimationManager) RuntimeHelpers.GetUninitializedObject(typeof(AnimationManager));
        animation.Current = playing;
        typeof(Probe).BaseType!.BaseType!.GetField("<Animation>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.SetValue(player, animation);
        return player;
    }

    private static BuffManager Listener(IActor owner) {
        var manager = new BuffManager(owner);
        var store = (ConcurrentDictionary<int, List<Buff>>) typeof(BuffManager).GetField("buffs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var stub = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        var metadata = new AdditionalEffectMetadata(10100163, 1, (BeginCondition) RuntimeHelpers.GetUninitializedObject(typeof(BeginCondition)), (AdditionalEffectMetadataProperty) RuntimeHelpers.GetUninitializedObject(typeof(AdditionalEffectMetadataProperty)), null!, null!, null!, null!, null, null!, null, null, [], [], []);
        var buff = new Buff(metadata, 1, stub, stub, 0, 0, 0);
        buff.Enable();
        store[10100163] = [buff];
        return manager;
    }
}
