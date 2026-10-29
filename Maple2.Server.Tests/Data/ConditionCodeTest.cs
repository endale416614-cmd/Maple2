using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Maple2.Database.Context;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Maple2.Server.Tests.Data;

/// <summary>
/// Every condition type the server reports (a ConditionUpdate, Update or Recount call in Maple2.Server.Game) passes CheckCode for the
/// first code of every trophy grade and quest condition that has codes, so an event without its case fails here.
/// It checks codes, not targets: CheckTarget refusals are not covered.
/// </summary>
[Category("Data")]
[NonParallelizable]
public class ConditionCodeTest {
    private static readonly Regex Event = new(@"(?:ConditionUpdate|Update|Recount)\(\s*ConditionType\.(\w+)");
    // CheckCode reads and changes the session's collections for these, so they cannot be checked without a session.
    private static readonly HashSet<ConditionType> SessionTypes = [ConditionType.item_collect, ConditionType.item_collect_revise, ConditionType.fish_collect, ConditionType.fish_goldmedal];
    private static readonly HashSet<int> KnownRefusedQuests = [
        // TODO: the ensemble quests code their map, but play_ensenble_time is reported with the map as its target and has no CheckCode case; drop these ids when that event is fixed.
        50000108, 50000109, 50000110, 50000111, 50000112, 50000113, 50000114, 70700043, 70700044, 70700045, 70700046, 70700047, 70700048, 70700049,
        // TODO: these quests list several code ranges, which the ingest stores as Strings (ConvertCodes); drop these ids when the ingest reads several ranges.
        89040021, 89050131, 89050148,
    ];

    [Test]
    public void NewEventsAreReported() {
        Assert.That(Reported(), Is.SupersetOf(new[] {
            ConditionType.npc_assist_bonus, ConditionType.npc_lasthit, ConditionType.skill_damage_npc, ConditionType.skill_die,
            ConditionType.hero_achieve_grade, ConditionType.field_mission,
        }));
    }

    [Test]
    public void CodedConditionsPassCheckCode() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        HashSet<ConditionType> reported = Reported();
        var failures = new List<string>();
        foreach (AchievementMetadata trophy in context.AchievementMetadata.ToList()) {
            foreach (AchievementMetadataGrade grade in trophy.Grades.Values) {
                if (Refused(grade.Condition, reported)) {
                    failures.Add($"trophy {trophy.Id} grade {grade.Grade}: {grade.Condition.Type}");
                }
            }
        }
        foreach (QuestMetadata quest in context.QuestMetadata.ToList()) {
            if (KnownRefusedQuests.Contains(quest.Id)) {
                continue;
            }
            foreach (ConditionMetadata condition in quest.Conditions) {
                if (Refused(condition, reported)) {
                    failures.Add($"quest {quest.Id}: {condition.Type}");
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
    }

    [Test]
    public void EventsSitWhereTheyCount() {
        string game = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game");

        string actor = File.ReadAllText(Path.Combine(game, "Model", "Field", "Actor", "Actor.cs"));
        int hit = actor.IndexOf("ConditionType.skill_damage_npc, codeLong: damage.SkillId", StringComparison.Ordinal);
        int countCheck = actor.IndexOf("if (attack.Damage.Count <= 0)", StringComparison.Ordinal);
        int death = actor.IndexOf("ConditionType.skill_die, codeLong: damage.SkillId", StringComparison.Ordinal);
        int received = actor.IndexOf("OnDamageReceived(caster, positiveDamage);", StringComparison.Ordinal);
        int buffEvents = actor.IndexOf("foreach ((DamageType damageType, long amount) in targetRecord.Damage)", StringComparison.Ordinal);
        Assert.That(hit, Is.GreaterThan(0), "skill_damage_npc should be reported by ApplyDamage");
        Assert.That(hit, Is.LessThan(countCheck), "skill_damage_npc should come before the zero-damage return (a grab deals no damage)");
        Assert.That(received, Is.GreaterThan(0), "OnDamageReceived should be found");
        Assert.That(death, Is.GreaterThan(received), "skill_die should come after the hit took its health");
        Assert.That(death, Is.LessThan(buffEvents), "skill_die should come before the buff events");

        string npc = File.ReadAllText(Path.Combine(game, "Model", "Field", "Actor", "FieldNpc.cs"));
        int countKill = npc.IndexOf("private void CountKill(", StringComparison.Ordinal);
        int handleDealers = npc.IndexOf("private void HandleDamageDealers(", StringComparison.Ordinal);
        int assist = npc.IndexOf("ConditionType.npc_assist_bonus, codeLong: Value.Id", StringComparison.Ordinal);
        int lastHit = npc.IndexOf("ConditionType.npc_lasthit, codeLong: Value.Id", StringComparison.Ordinal);
        Assert.That(countKill, Is.GreaterThan(0), "CountKill should be found");
        Assert.That(assist, Is.GreaterThan(countKill), "npc_assist_bonus should be reported by CountKill");
        Assert.That(assist, Is.LessThan(handleDealers), "npc_assist_bonus should be reported by CountKill");
        Assert.That(lastHit, Is.GreaterThan(countKill), "npc_lasthit should be reported by CountKill");
        Assert.That(lastHit, Is.LessThan(handleDealers), "npc_lasthit should be reported by CountKill");
    }

    private static bool Refused(ConditionMetadata condition, HashSet<ConditionType> reported) {
        if (condition.Codes == null || !reported.Contains(condition.Type) || SessionTypes.Contains(condition.Type)) {
            return false;
        }
        string codeString = condition.Codes.Strings?.FirstOrDefault() ?? "";
        long codeLong = condition.Codes.Integers?.FirstOrDefault() ?? condition.Codes.Range?.Min ?? 0;
        return !(condition with { Target = null, PartyCount = 0, GuildPartyCount = 0 }).Check(null!, codeString: codeString, codeLong: codeLong);
    }

    private static HashSet<ConditionType> Reported() {
        string game = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game");
        return Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(game, path).Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj"))
            .SelectMany(path => Event.Matches(File.ReadAllText(path)))
            .Select(match => Enum.Parse<ConditionType>(match.Groups[1].Value))
            .ToHashSet();
    }
}
