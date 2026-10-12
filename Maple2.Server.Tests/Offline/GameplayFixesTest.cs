using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Trigger;
using Maple2.Server.Game.Trigger.Helpers;
using Maple2.Server.Game.Util;
using HTrigger = Maple2.Server.Game.Trigger.Helpers.Trigger;

namespace Maple2.Server.Tests.Offline;

/// <summary>Gameplay defects found while play-testing the offline edition: trigger groups, item_exist quests, token shops.</summary>
public class GameplayFixesTest {
    private sealed class Leaf(bool value) : ICondition {
        public string Name => "leaf";
        public string? NextState { get; set; }
        public LinkedList<IAction> Actions { get; set; } = [];
        public bool Evaluate(TriggerContext context) => value;
    }

    private static HTrigger Parse(string xml) {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return TriggerCache.ParseTrigger("test_map", "test_trigger", document);
    }

    /// <summary>Replaces every parsed leaf condition below <paramref name="group"/> with a fixed true/false leaf, in document order.</summary>
    private static void ReplaceLeaves(IGroupCondition group, Queue<bool> values) {
        LinkedListNode<ICondition>? node = group.Conditions.First;
        while (node != null) {
            if (node.Value is IGroupCondition nested) {
                ReplaceLeaves(nested, values);
            } else {
                node.Value = new Leaf(values.Dequeue());
            }
            node = node.Next;
        }
    }

    private static bool Evaluate(ICondition condition, params bool[] leaves) {
        ReplaceLeaves((IGroupCondition) condition, new Queue<bool>(leaves));
        return condition.Evaluate(null!);
    }

    // Real trigger 02000486_bf/101_buff, state "타임": any_one(monster_dead 900, monster_dead 901) -> Move01.
    private const string AnyOneXml = """
        <ms2><state name="A"><onEnter></onEnter><condition name="any_one"><group><condition name="monster_dead" spawn_ids="900" /><condition name="monster_dead" spawn_ids="901" /></group><transition state="Move01" /></condition></state><state name="Move01"><onEnter></onEnter></state></ms2>
        """;

    // Real trigger 02000486_bf/103_timer, state "설명": all_of(monster_dead 900, monster_dead 901) -> 타이머종료.
    private const string AllOfXml = """
        <ms2><state name="A"><onEnter></onEnter><condition name="all_of"><group><condition name="monster_dead" spawn_ids="900" /><condition name="monster_dead" spawn_ids="901" /></group><transition state="End" /></condition></state><state name="End"><onEnter></onEnter></state></ms2>
        """;

    [Test]
    public void AnyOneGroup_KeepsChildrenAndTransition() {
        HTrigger trigger = Parse(AnyOneXml);
        ICondition condition = trigger.States[0].Conditions.Single();

        Assert.That(condition, Is.InstanceOf<HTrigger.GroupAnyOne>());
        Assert.That(((IGroupCondition) condition).Conditions, Has.Count.EqualTo(2));
        // The <transition> sits beside <group> in the XML (265 group conditions in the data); it used to be read from inside <group> and lost.
        Assert.That(condition.NextState, Is.EqualTo("Move01"));

        Assert.That(Evaluate(condition, false, false), Is.False);
        Assert.That(Evaluate(condition, true, false), Is.True);
        Assert.That(Evaluate(condition, false, true), Is.True);
        Assert.That(Evaluate(condition, true, true), Is.True);
    }

    [Test]
    public void AllOfGroup_KeepsChildrenAndTransition() {
        HTrigger trigger = Parse(AllOfXml);
        ICondition condition = trigger.States[0].Conditions.Single();

        Assert.That(condition, Is.InstanceOf<HTrigger.GroupAllOf>());
        Assert.That(((IGroupCondition) condition).Conditions, Has.Count.EqualTo(2));
        Assert.That(condition.NextState, Is.EqualTo("End"));

        // An all_of without its children used to be vacuously true and fired immediately.
        Assert.That(Evaluate(condition, false, false), Is.False);
        Assert.That(Evaluate(condition, true, false), Is.False);
        Assert.That(Evaluate(condition, false, true), Is.False);
        Assert.That(Evaluate(condition, true, true), Is.True);
    }

    [Test]
    public void NestedGroups_AreEvaluatedRecursively() {
        // The data has no nested groups today, but the format allows them: all_of(any_one(a, b), c).
        HTrigger trigger = Parse("""
            <ms2><state name="A"><onEnter></onEnter><condition name="all_of"><group>
              <condition name="any_one"><group><condition name="monster_dead" spawn_ids="1" /><condition name="monster_dead" spawn_ids="2" /></group></condition>
              <condition name="monster_dead" spawn_ids="3" />
            </group><transition state="A" /></condition></state></ms2>
            """);
        ICondition outer = trigger.States[0].Conditions.Single();
        var inner = (IGroupCondition) ((IGroupCondition) outer).Conditions.First!.Value;
        Assert.That(inner, Is.InstanceOf<HTrigger.GroupAnyOne>());
        Assert.That(inner.Conditions, Has.Count.EqualTo(2));

        Assert.That(Evaluate(outer, false, false, true), Is.False);
        Assert.That(Evaluate(outer, true, false, false), Is.False);
        Assert.That(Evaluate(outer, true, false, true), Is.True);
        Assert.That(Evaluate(outer, false, true, true), Is.True);
    }

    [Test]
    public void GroupActions_AreKept() {
        // Real trigger 99999843/objecttest: an all_of whose <group> also holds an <action>.
        HTrigger trigger = Parse("""
            <ms2><state name="A"><onEnter></onEnter><condition name="all_of"><group><condition name="object_interacted" interact_ids="12000401" state="0" /><action name="set_interact_object" trigger_ids="12000401" state="1" /></group><transition state="PC_MOVE_01" /></condition></state></ms2>
            """);
        ICondition condition = trigger.States[0].Conditions.Single();
        Assert.That(((IGroupCondition) condition).Conditions, Has.Count.EqualTo(1));
        Assert.That(condition.Actions, Has.Count.EqualTo(1));
        Assert.That(condition.NextState, Is.EqualTo("PC_MOVE_01"));
    }

    // Quest 50100050 "Dead Men Tell No Tales" (maple-data.db quest.Conditions):
    //   {"Type":3 (item_exist),"Value":10,"Codes":{"Integers":[30001151]},"Target":{"Integers":[2000182]}}
    // Target is the map (2000182, the quest's ProgressMaps), Codes is the item id.
    private static readonly ConditionMetadata DeadMen = new(ConditionType.item_exist, 10,
        new ConditionMetadata.Parameters(Integers: [30001151]),
        new ConditionMetadata.Parameters(Integers: [2000182]));

    [Test]
    public void ItemExist_QuestWithMapTarget_CountsInThatMapOnly() {
        Assert.That(DeadMen.Check(null!, targetLong: 2000182, codeLong: 30001151), Is.True);
        Assert.That(DeadMen.Check(null!, targetLong: 2000183, codeLong: 30001151), Is.False, "other map");
        Assert.That(DeadMen.Check(null!, targetLong: 2000182, codeLong: 30001152), Is.False, "other item");
        Assert.That(DeadMen.Check(null!, targetLong: 0, codeLong: 30001151), Is.False, "no map reported");
    }

    [Test]
    public void ItemExist_QuestsWithoutTarget_AreUnaffected() {
        // 595 of the 604 item_exist conditions have no Target, e.g. quest 10001005 "Those Who Leave":
        //   {"Type":3,"Value":1,"Codes":{"Integers":[30000163]}}
        var noTarget = new ConditionMetadata(ConditionType.item_exist, 1,
            new ConditionMetadata.Parameters(Integers: [30000163]), null);
        Assert.That(noTarget.Check(null!, targetLong: 2000182, codeLong: 30000163), Is.True);
        Assert.That(noTarget.Check(null!, targetLong: 0, codeLong: 30000163), Is.True);
        Assert.That(noTarget.Check(null!, targetLong: 2000182, codeLong: 30000164), Is.False);

        // Quest 20001480 "Triage": one of the 9 targeted conditions, item 30000285 in map 2000013.
        var triage = new ConditionMetadata(ConditionType.item_exist, 5,
            new ConditionMetadata.Parameters(Integers: [30000285]),
            new ConditionMetadata.Parameters(Integers: [2000013]));
        Assert.That(triage.Check(null!, targetLong: 2000013, codeLong: 30000285), Is.True);
        Assert.That(triage.Check(null!, targetLong: 2000182, codeLong: 30000285), Is.False);
    }

    [Test]
    public void TokenShop_ChargesTotalPriceForQuantity() {
        const int unitCost = 30;
        const int quantity = 5;
        var balances = new Dictionary<CurrencyType, long> { [CurrencyType.ValorToken] = 1000 };

        // Buy() computes price = unit cost * quantity, checks it, and must deduct that same total.
        bool paid = ShopManager.TryPayToken(ShopCurrencyType.ValorToken, unitCost * quantity,
            type => balances[type], (type, value) => balances[type] = value);

        Assert.That(paid, Is.True);
        Assert.That(balances[CurrencyType.ValorToken], Is.EqualTo(1000 - unitCost * quantity));
    }

    [Test]
    public void TokenShop_RefusesWhenTotalIsUnaffordable_AndChargesNothing() {
        // Can afford one unit (30) but not five (150).
        var balances = new Dictionary<CurrencyType, long> { [CurrencyType.Treva] = 100 };

        bool paid = ShopManager.TryPayToken(ShopCurrencyType.Treva, 30 * 5,
            type => balances[type], (type, value) => balances[type] = value);

        Assert.That(paid, Is.False);
        Assert.That(balances[CurrencyType.Treva], Is.EqualTo(100));
        Assert.That(ShopManager.TryPayToken(ShopCurrencyType.Meso, 1, _ => 100, (_, _) => Assert.Fail("not a token")), Is.False);
    }

    private static NpcMetadataBasic Basic(int friendly, int attackGroup = 0, int defenseGroup = 0) =>
        new(friendly, attackGroup, defenseGroup, 0, 0, 0, 0, 36, 1, false, 0, 0, 0, 36, [], [], 0);

    // Real NPC data (Friendly, AttackGroup, DefenseGroup): monsters attack allies, barriers and gates, not story NPCs or cannons.
    [Test]
    public void CombatGroups_MonstersAttackAlliesBarriersAndGates_Only() {
        NpcMetadataBasic pipeZombie = Basic(0, 1, 2);      // 24000206, Cusp of Life wave monster
        NpcMetadataBasic portalBarrier = Basic(1, 2, 1);   // 24000201, spawn 99
        NpcMetadataBasic gate = Basic(1, 2, 1);            // 24000208
        NpcMetadataBasic cannon = Basic(1, 2, 4);          // 24000217
        NpcMetadataBasic katvan = Basic(2, 2, 1);          // 11000044, story NPC
        NpcMetadataBasic teamCannon = Basic(201, 2, 4);    // 26000001, PvP

        Assert.That(CombatGroups.MonsterCanAttack(pipeZombie, portalBarrier), Is.True);
        Assert.That(CombatGroups.MonsterCanAttack(pipeZombie, gate), Is.True);
        Assert.That(CombatGroups.MonsterCanAttack(pipeZombie, cannon), Is.False, "defense group 4");
        Assert.That(CombatGroups.MonsterCanAttack(pipeZombie, katvan), Is.False, "story NPC");
        Assert.That(CombatGroups.MonsterCanAttack(pipeZombie, teamCannon), Is.False);
        Assert.That(CombatGroups.MonsterCanAttack(portalBarrier, gate), Is.False, "an ally is not a monster");
        Assert.That(CombatGroups.MonsterCanAttack(Basic(0, 0, 0), portalBarrier), Is.False, "skill dummy without attack group");
    }

    // Cusp of Life (dungeon 21002001, map 02000066) failed as soon as wave 1 started: monster_dead 99 only searched Mobs,
    // but its Portal Barrier 24000201 is a friendly NPC (Friendly 1) kept in Npcs.
    [Test]
    public void MonsterDead_CountsFriendlyCombatNpcs_ButNotStoryNpcs() {
        Assert.That(TriggerContext.CountsForMonsterDead(Basic(0)), Is.True, "monster");
        Assert.That(TriggerContext.CountsForMonsterDead(Basic(1)), Is.True, "Portal Barrier 24000201 / guardian / cannon");
        Assert.That(TriggerContext.CountsForMonsterDead(Basic(2)), Is.False, "Katvan 11000044 in 52000138 act3_wave2_move");
    }
}
