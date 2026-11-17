using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.ActorStateComponent;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>VR-01b: every NPC with an AI runs a minute of game time on the field rig (VR-01a): no exception, the field's regions stay
/// under the caps (RN-01: 1000 for one caster, 2000 in all), the NPC is not held invincible to the end, and a cast of it ends.</summary>
[Category("Data")]
[NonParallelizable]
public class NpcSweepTest {
    private const int Seconds = 60;
    private const int HeldSeconds = 20; // invincible this long at the end, or one cast this long, is a hold with no end
    private const int CasterCap = 1000, FieldCap = 2000;

    /// <summary>NPCs the sweep reports and no code fixes yet: id, then why. An entry that no longer fails is reported, not an error.</summary>
    private static readonly Dictionary<int, string> Known = new() {
        // A cast that is still the same cast after 56 s.
        [21400005] = "Bursting Bubbling: a cast of skill 49013001 (motions Damg_A, Attack_002_A) still runs after 56 s; cause not traced",
        [27100108] = "Bursting Bubbling: a cast of skill 49013001 (motions Damg_A, Attack_002_A) still runs after 56 s; cause not traced",
        [31000001] = "Bursting Bubbling: a cast of skill 49013001 (motions Damg_A, Attack_002_A) still runs after 56 s; cause not traced",
        [23000078] = "Nutaman: a cast of skill 50101231 still runs after 51 s; one of its motions (Attack_Idle_B) has LoopCount 99",
        [23000079] = "Nutaman: a cast of skill 50101233 still runs after 51 s; one of its motions (Attack_Idle_B) has LoopCount 99",
        [34000140] = "Mutant King Slime: an effect mounts the player on ride 99910260 and the mount fails, logged as an error",
    };

    private static FieldRig Open() {
        FieldRig? rig = FieldRig.TryOpen();
        if (rig == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }
        rig!.Immortal = true;
        return rig;
    }

    /// <summary>An enabled buff of the NPC, not past its end, that makes a hit on it no damage.</summary>
    internal static bool Invincible(FieldNpc npc, long tick) =>
        npc.Buffs.EnumerateBuffs().Any(buff => buff.Enabled && (buff.Metadata.Status.Invincible || DefensiveEffects.Invincible(buff.Id, buff.Level) is 1 or 2)
            && (buff.EndTick <= buff.StartTick || tick <= buff.EndTick));

    /// <summary>The rule an NPC broke in its minute, or null.</summary>
    internal static string? Run(FieldRig rig, NpcMetadata metadata) {
        rig.Clean();
        rig.Errors.Clear();
        try {
            FieldNpc npc = rig.SpawnNpc(metadata.Id, new Vector3(200, 0, 0));
            int invincibleFor = 0, castFor = 0, mostCasterRegions = 0, mostRegions = 0;
            object? cast = null;
            for (int second = 1; second <= Seconds; second++) {
                rig.Advance(1000);
                // A missing AI file is logged at the spawn and the default AI runs: the behaviour registry's "degraded", not a rule of the sweep.
                if (rig.Errors.FirstOrDefault(line => !line.Contains("setting as default AI")) is { } error) {
                    return "logged an error: " + error;
                }
                mostRegions = Math.Max(mostRegions, rig.Regions);
                mostCasterRegions = Math.Max(mostCasterRegions, rig.MostRegionsOfOneCaster);
                invincibleFor = Invincible(npc, rig.Field.FieldTick) ? invincibleFor + 1 : 0;
                object? now = npc.MovementState.CastTask;
                castFor = now != null && ReferenceEquals(now, cast) ? castFor + 1 : 0;
                cast = now;
            }
            if (mostCasterRegions > CasterCap || mostRegions > FieldCap) {
                return $"regions over the cap: {mostCasterRegions} of one caster (cap {CasterCap}), {mostRegions} in the field (cap {FieldCap})";
            }
            if (invincibleFor >= HeldSeconds) {
                return $"invincible for the last {invincibleFor} s";
            }
            if (castFor >= HeldSeconds) {
                return $"one cast ran {castFor} s and was still running (skill {(cast as MovementState.NpcSkillCastTask)?.SkillId})";
            }
            return null;
        } catch (Exception ex) {
            return $"threw {ex.GetType().Name}: {ex.Message} at {string.Join(" | ", (ex.StackTrace ?? "").Split((char) 10).Take(6).Select(frame => frame.Trim()))}";
        }
    }

    private static List<(NpcMetadata Npc, string Failure)> Sweep(FieldRig rig, IEnumerable<NpcMetadata> npcs, out int count) {
        var failures = new List<(NpcMetadata, string)>();
        count = 0;
        foreach (NpcMetadata npc in npcs) {
            count++;
            if (Run(rig, npc) is { } failure) {
                failures.Add((npc, failure));
            }
        }
        return failures;
    }

    private static void Judge(List<(NpcMetadata Npc, string Failure)> failures, int count, Stopwatch time, string kind) {
        var report = new StringBuilder();
        report.AppendLine($"{kind}: {count} NPCs, {failures.Count} failed, {time.Elapsed.TotalSeconds:0} s");
        foreach ((NpcMetadata npc, string failure) in failures) {
            report.AppendLine($"{npc.Id}\t{npc.Name}\t{npc.AiPath}\t{failure}");
        }
        if (kind == "sweep") {
            report.AppendLine("skip entries that did not fail: " + string.Join(", ", Known.Keys.Except(failures.Select(entry => entry.Npc.Id))));
        }
        TestContext.Out.Write(report.ToString());
        if (Environment.GetEnvironmentVariable("VR_SWEEP_OUT") is { Length: > 0 } path) {
            File.WriteAllText(path, report.ToString());
        }

        Assert.That(failures.Where(entry => !Known.ContainsKey(entry.Npc.Id)).Select(entry => $"{entry.Npc.Id} {entry.Npc.Name} ({entry.Npc.AiPath}): {entry.Failure}"),
            Is.Empty, "NPCs that broke a rule and are not in the skip list");
    }

    // 40361001 is invincible with no duration; 40500022 level 1 for 2 s.
    [Test]
    public void AnInvincibleBuffIsSeenUntilItsEnd() {
        using FieldRig rig = Open();
        FieldNpc npc = rig.SpawnNpc(21000012, new Vector3(2000, 0, 0));
        npc.Buffs.AddBuff(npc, npc, 40500022, 1, rig.Field.FieldTick);
        Assert.That(Invincible(npc, rig.Field.FieldTick), Is.True, "while it lasts");
        Assert.That(Invincible(npc, rig.Field.FieldTick + 5000), Is.False, "past its end");

        npc.Buffs.AddBuff(npc, npc, 40361001, 1, rig.Field.FieldTick);
        rig.Advance(30_000);
        Assert.That(Invincible(npc, rig.Field.FieldTick), Is.True, "a buff with no end is still held after 30 s");
    }

    /// <summary>A fast sample for the normal suite: every NPC of class 3 and up (bosses and dungeon bosses) and every 20th of the rest.</summary>
    [Test]
    public void ABossAndASampleOfTheRestRunAMinute() {
        using FieldRig rig = Open();
        List<NpcMetadata> sample = rig.NpcsWithAi.Where((npc, index) => npc.Basic.Class >= 3 || index % 20 == 0).ToList();
        var time = Stopwatch.StartNew();
        List<(NpcMetadata Npc, string Failure)> failures = Sweep(rig, sample, out int count);
        Judge(failures, count, time, "sample");
    }

    /// <summary>Every NPC with an AI: dotnet test Maple2/Maple2.Server.Tests/Maple2.Server.Tests.csproj -c Release --filter Category=Gate</summary>
    [Test]
    [Explicit("about 4,000 NPCs of one minute each; run with --filter Category=Gate")]
    [Category("Gate")]
    public void EveryNpcWithAnAiRunsAMinute() {
        using FieldRig rig = Open();
        var time = Stopwatch.StartNew();
        List<(NpcMetadata Npc, string Failure)> failures = Sweep(rig, rig.NpcsWithAi, out int count);
        Judge(failures, count, time, "sweep");
    }
}
