using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Model.Skill;
using Maple2.Server.Tests.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Offline;

/// <summary>PI-02 (BT-267): an AI node's first use comes at its InitialCooltime, not at InitialCooltime plus Cooltime (the Lost Vayar Guardian's
/// return jump, 100 ms and 120 s, came after its window). PI-02 (BT-260, BT-55): a monster restored to full after a battle (PeaceRestore) gets its
/// reserved nodes back and its cooltimes cleared when its next battle starts 2000 ms or more after the restore, so Pamokan's jump block runs again
/// and he no longer loops; a battle that resumes sooner (a target gap), and a monster that is not restored, keep their state; AiExtraData (set by
/// map scripts) is never cleared.</summary>
[NonParallelizable]
public class AiFirstUseTest {
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const string GiantAi = "BossDungeon/SandstoneGiant/AI_SandstoneGiant.xml";
    private const string PamokanAi = "BossDungeon/BullArmorRed/AI_BullArmorRed.xml";

    private static AiMetadata.SkillNode Skill(long initial, long cooltime) =>
        new("skill", [], 6, 1, 100, false, Vector3.Zero, 0, 0, initial, cooltime, 0, false);

    [Test]
    public void FirstUseComesAtTheInitialCooltime() {
        (AiState ai, FieldManager field) = Rig();
        AiMetadata.SkillNode node = Skill(100, 120000);
        SetTick(field, 150_000);
        Assert.That(CanEvaluate(ai, node), Is.False, "the first look only starts the initial cooltime");
        SetTick(field, 150_100);
        Assert.That(CanEvaluate(ai, node), Is.False, "not yet at exactly the initial cooltime");
        SetTick(field, 150_101);
        Assert.That(CanEvaluate(ai, node), Is.True, "free once the initial cooltime is over, not initial plus cooltime");

        // Used at 150101: the ordinary cooltime applies from the use
        Coolings(ai)[NodeKey(ai, node)] = 150_101;
        SetTick(field, 150_101 + 120_000);
        Assert.That(CanEvaluate(ai, node), Is.False);
        SetTick(field, 150_101 + 120_001);
        Assert.That(CanEvaluate(ai, node), Is.True);
    }

    [Test]
    public void TheGuardiansReturnJumpIsFreeAfterItsInitialCooltime() {
        AiMetadata giant = Load(GiantAi);
        var skills = new List<AiMetadata.SkillNode>();
        void Walk(AiMetadata.Entry entry) {
            if (entry is AiMetadata.SkillNode skill) {
                skills.Add(skill);
            }
            if (entry is AiMetadata.Node node) {
                foreach (AiMetadata.Entry child in node.Entries) {
                    Walk(child);
                }
            }
            if (entry is AiMetadata.ConditionsNode conditions) {
                foreach (AiMetadata.Condition condition in conditions.Conditions) {
                    Walk(condition);
                }
            }
        }
        foreach (AiMetadata.Entry entry in giant.Battle) {
            Walk(entry);
        }
        AiMetadata.SkillNode jump = skills.Single(skill => skill.InitialCooltime == 100 && skill.Cooltime == 120000);

        (AiState ai, FieldManager field) = Rig();
        SetTick(field, 150_000);
        Assert.That(CanEvaluate(ai, jump), Is.False);
        SetTick(field, 150_000 + jump.InitialCooltime + 1);
        Assert.That(CanEvaluate(ai, jump), Is.True, "free 100 ms after the window opens, before it closes");
    }

    // RestorePeace notes the restore; AiState.Update sees the battle start. The reset waits for a real break (2000 ms), not a target gap (up to 500 ms).
    [TestCase(2000L)]
    [TestCase(2001L)]
    [TestCase(60000L)]
    public void ABattleThatStartsAfterARealBreakResetsTheAi(long gap) {
        (FieldNpc npc, AiState ai, AiMetadata pamokan) = RigNpc(peaceRestore: true);
        Restore(npc, 10_000);
        StartBattle(npc, ai, 10_000 + gap);

        Assert.Multiple(() => {
            Assert.That(((List<AiMetadata.Node>) GetField(ai, "reservedNodes")).Count, Is.EqualTo(pamokan.Reserved.Length), "the block that ran is back");
            Assert.That(Coolings(ai), Is.Empty);
            Assert.That(npc.AiExtraData["JumpMoveOn"], Is.EqualTo(1), "a value a map script set survives");
        });
    }

    [TestCase(0L)]
    [TestCase(500L)]
    [TestCase(1999L)]
    public void ABattleThatResumesAtATargetGapKeepsTheAiState(long gap) {
        (FieldNpc npc, AiState ai, _) = RigNpc(peaceRestore: true);
        Restore(npc, 10_000);
        StartBattle(npc, ai, 10_000 + gap);

        Assert.Multiple(() => {
            Assert.That(((List<AiMetadata.Node>) GetField(ai, "reservedNodes")), Is.Empty, "the reserved nodes stay as they are");
            Assert.That(Coolings(ai)["jump"], Is.EqualTo(5));
            Assert.That(npc.AiExtraData["JumpMoveOn"], Is.EqualTo(1));
        });
    }

    [Test]
    public void ABattleStartWithNoRestoreNotedChangesNothing() {
        (FieldNpc npc, AiState ai, _) = RigNpc(peaceRestore: true);
        StartBattle(npc, ai, 60_000);

        Assert.Multiple(() => {
            Assert.That(((List<AiMetadata.Node>) GetField(ai, "reservedNodes")), Is.Empty);
            Assert.That(Coolings(ai)["jump"], Is.EqualTo(5));
            Assert.That(npc.AiExtraData["JumpMoveOn"], Is.EqualTo(1));
        });
    }

    [Test]
    public void AMonsterThatIsNotRestoredNotesNothingAndKeepsItsAiState() {
        (FieldNpc npc, AiState ai, _) = RigNpc(peaceRestore: false);
        Restore(npc, 10_000);
        StartBattle(npc, ai, 60_000);

        Assert.Multiple(() => {
            Assert.That(((List<AiMetadata.Node>) GetField(ai, "reservedNodes")), Is.Empty);
            Assert.That(Coolings(ai)["jump"], Is.EqualTo(5));
            Assert.That(npc.AiExtraData["JumpMoveOn"], Is.EqualTo(1));
        });
    }

    [Test]
    public void AResumedBattleClearsTheNoteSoALaterStartDoesNotReset() {
        (FieldNpc npc, AiState ai, _) = RigNpc(peaceRestore: true);
        Restore(npc, 10_000);
        StartBattle(npc, ai, 10_500);             // a target gap: kept, note cleared
        SetField(ai, "scriptedEvent", false);
        SetTick(npc.Field, 20_000);
        ai.Update(20_000);                        // not in battle: the edge can be seen again
        StartBattle(npc, ai, 60_000);             // a new battle with no restore in between

        Assert.Multiple(() => {
            Assert.That(((List<AiMetadata.Node>) GetField(ai, "reservedNodes")), Is.Empty);
            Assert.That(Coolings(ai)["jump"], Is.EqualTo(5));
        });
    }

    /// <summary>A bare NPC as PeaceRestoreTest builds it, with Pamokan's AI on it: the reserved block ran (the list is empty), a cooltime is
    /// set and a map script's value is in the extra data.</summary>
    private static (FieldNpc, AiState, AiMetadata) RigNpc(bool peaceRestore) {
        AiMetadata pamokan = Load(PamokanAi);
        Assert.That(pamokan.Reserved, Is.Not.Empty);
        var stat = new NpcMetadataStat(new Dictionary<BasicAttribute, long> { [BasicAttribute.Health] = 1000 }, [], [], [], []);
        var metadata = new NpcMetadata(23100041, "Pamokan", "", null!, stat, null!, null!, null!, null!, null!, null!, null!, null, null!);
        var npc = Bare<FieldNpc>();
        SetField(npc, "<Value>k__BackingField", new Npc(metadata, null));
        SetField(npc, "<Field>k__BackingField", Bare<FieldManager>());
        SetField(npc, "<Stats>k__BackingField", new StatsManager(npc));
        SetField(npc, "DamageDealers", new ConcurrentDictionary<int, DamageRecordTarget>());
        SetField(npc, "<PeaceRestore>k__BackingField", peaceRestore);
        SetField(npc, "BattleState", new BattleState(npc));
        SetField(npc, "TaskState", new TaskState(npc));
        MovementState movement = Bare<MovementState>();
        SetField(movement, "actor", npc);
        SetField(npc, "MovementState", movement);
        SetField(npc, nameof(FieldNpc.AiExtraData), new Dictionary<string, int> { ["JumpMoveOn"] = 1 });

        var ai = Bare<AiState>();
        SetField(ai, "actor", npc);
        SetField(ai, "<AiMetadata>k__BackingField", pamokan);
        SetField(ai, "reservedNodes", new List<AiMetadata.Node>());
        SetField(ai, "coolTimes", new Dictionary<string, long> { ["jump"] = 5 });
        SetField(ai, "aiStack", Activator.CreateInstance(typeof(AiState).GetField("aiStack", Any)!.FieldType)!);
        SetField(npc, nameof(FieldNpc.AiState), ai);
        return (npc, ai, pamokan);
    }

    // The real RestorePeace (it notes the tick on the AI) at a field tick
    private static void Restore(FieldNpc npc, long tick) {
        SetTick(npc.Field, tick);
        npc.RestorePeace();
    }

    // AiState.Update with the battle on (a scripted event holds it): the edge from not in battle to in battle is seen there
    private static void StartBattle(FieldNpc npc, AiState ai, long tick) {
        SetTick(npc.Field, tick);
        SetField(ai, "scriptedEvent", true);
        ai.Update(tick);
    }

    private static (AiState, FieldManager) Rig() {
        var field = Bare<FieldManager>();
        var npc = Bare<FieldNpc>();
        SetField(npc, "<Field>k__BackingField", field);
        SetField(npc, nameof(FieldNpc.AiExtraData), new Dictionary<string, int>());
        var ai = Bare<AiState>();
        SetField(ai, "actor", npc);
        SetField(ai, "coolTimes", new Dictionary<string, long>());
        return (ai, field);
    }

    private static bool CanEvaluate(AiState ai, AiMetadata.Entry node) => (bool) typeof(AiState).GetMethod("CanEvaluateNode", Any)!.Invoke(ai, [node])!;

    private static string NodeKey(AiState ai, AiMetadata.Entry node) => (string) typeof(AiState).GetMethod("GetNodeKey", Any)!.Invoke(ai, [node])!;

    private static Dictionary<string, long> Coolings(AiState ai) => (Dictionary<string, long>) GetField(ai, "coolTimes");

    private static void SetTick(FieldManager field, long tick) => SetField(field, "<FieldTick>k__BackingField", tick);

    private static AiMetadata Load(string name) {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context.AiMetadata.Where(ai => ai.Name == name).ToList().Single();
    }

    private static object GetField(object target, string name) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
            if (field != null) {
                return field.GetValue(target)!;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, Any | BindingFlags.DeclaredOnly);
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
}
