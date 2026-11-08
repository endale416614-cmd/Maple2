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
            ConditionType.hero_achieve_grade, ConditionType.field_mission, ConditionType.guild_join, ConditionType.guild_exp, ConditionType.guild_attendance, ConditionType.use_merat, ConditionType.minigame_clear,
            ConditionType.home_bank, ConditionType.home_goto, ConditionType.create_blueprint, ConditionType.interior_level,
            ConditionType.interior_exp_offset, ConditionType.interior_point, ConditionType.controller,
            ConditionType.item_remake_option, ConditionType.item_remake_option_record, ConditionType.pet_remake_option, ConditionType.pet_remake_option_record,
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

        string currency = File.ReadAllText(Path.Combine(game, "Manager", "CurrencyManager.cs"));
        int meretSend = currency.IndexOf("CurrencyPacket.UpdateMeret(Currency, delta)", StringComparison.Ordinal);
        int spent = currency.IndexOf("ConditionType.use_merat, -delta", StringComparison.Ordinal);
        int redSetter = currency.IndexOf("public long GameMeret", StringComparison.Ordinal);
        Assert.That(spent, Is.GreaterThan(meretSend), "use_merat should come after the Meret packet, as in the original");
        Assert.That(spent, Is.LessThan(redSetter), "use_merat belongs to the Meret setter, not the Red Meret one");

        string miniGame = File.ReadAllText(Path.Combine(game, "Trigger", "TriggerContext.MiniGame.cs"));
        int cleared = miniGame.IndexOf("player.Session.MiniGameRecord = null;", StringComparison.Ordinal);
        int finish = miniGame.IndexOf("ConditionType.minigame_clear, codeLong: Field.MapId", StringComparison.Ordinal);
        int losers = miniGame.IndexOf("if (isOnlyWinner)", StringComparison.Ordinal);
        Assert.That(finish, Is.GreaterThan(cleared), "minigame_clear should come after the record is cleared, so a run counts once");
        Assert.That(finish, Is.LessThan(losers), "only the winners' loop counts minigame_clear");

        string quests = File.ReadAllText(Path.Combine(game, "Manager", "QuestManager.cs"));
        Assert.That(quests, Does.Contain("if (type != ConditionType.map && quest.Metadata.Basic is { Type: QuestType.FieldMission"), "a map condition names its own map, so the field mission gate must not apply to it (72000066)");

        string achievements = File.ReadAllText(Path.Combine(game, "Manager", "AchievementManager.cs"));
        Assert.That(achievements, Does.Contain("CanClaim(achievement, grade); grade++"), "the claim loop should stop at unearned grades (BT-151)");

        string guild = File.ReadAllText(Path.Combine(game, "Manager", "GuildManager.cs"));
        int addTag = guild.IndexOf("if (addTag) {", StringComparison.Ordinal);
        int join = guild.IndexOf("ConditionType.guild_join);", StringComparison.Ordinal);
        int joinReq = guild.IndexOf("ConditionType.guild_join_req);", StringComparison.Ordinal);
        Assert.That(join, Is.GreaterThan(addTag), "guild_join should be reported only for a real join: the login call runs before session.Dungeon exists");
        Assert.That(join, Is.LessThan(joinReq), "guild_join should stay inside the addTag block");
        Assert.That(join, Is.LessThan(guild.IndexOf('}', addTag)), "guild_join should stay inside the addTag block: the login call runs before session.Dungeon exists");
    }

    [Test]
    public void HomeEventsSitWhereTheyCount() {
        string handlers = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "PacketHandlers");

        string home = File.ReadAllText(Path.Combine(handlers, "HomeHandler.cs"));
        int goHome = home.IndexOf("ConditionType.home_goto", StringComparison.Ordinal);
        int migrate = home.IndexOf("session.Migrate(home.Indoor.MapId", StringComparison.Ordinal);
        Assert.That(goHome, Is.GreaterThan(0), "home_goto should be reported by the go home handler");
        Assert.That(goHome, Is.LessThan(migrate), "home_goto should come before the move, as in the original");

        string bank = File.ReadAllText(Path.Combine(handlers, "HomeBankHandler.cs"));
        int homeCase = bank.IndexOf("case Command.Home:", StringComparison.Ordinal);
        int premiumCase = bank.IndexOf("case Command.Premium:", StringComparison.Ordinal);
        int used = bank.IndexOf("ConditionType.home_bank", StringComparison.Ordinal);
        Assert.That(used, Is.GreaterThan(homeCase), "home_bank should be reported by the home safe");
        Assert.That(used, Is.LessThan(premiumCase), "home_bank should not be reported by the premium safe");
        Assert.That(bank, Does.Contain("HomeBankCallCooltime / 1000"), "the safe's cooldown table holds milliseconds");

        string doctor = File.ReadAllText(Path.Combine(handlers, "HomeDoctorHandler.cs"));
        Assert.That(doctor, Does.Contain("HomeDoctorCallCooltime / 1000"), "the doctor's cooldown table holds milliseconds");
    }

    [Test]
    public void ControllerUseCarriesTheItem() {
        string game = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game");
        string handler = File.ReadAllText(Path.Combine(game, "PacketHandlers", "FunctionCubeHandler.cs"));
        Assert.That(handler, Does.Contain("ConditionType.controller, codeLong: fieldInteract.ItemId"), "a furniture use should report the item placed");
        string state = File.ReadAllText(Path.Combine(game, "Manager", "Field", "FieldManager", "FieldManager.State.cs"));
        Assert.That(state, Does.Contain("ItemId = cube.ItemId"), "the function cube should carry the item placed");
    }

    [Test]
    public void ControllerCodesAreSwitchFurniture() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        List<AchievementMetadata> trophies = context.AchievementMetadata.ToList()
            .Where(trophy => trophy.Grades.Values.Any(grade => grade.Condition.Type == ConditionType.controller)).ToList();
        int[] codes = trophies.SelectMany(trophy => trophy.Grades.Values.SelectMany(grade => grade.Condition.Codes?.Integers ?? [])).Distinct().ToArray();
        Dictionary<int, ItemMetadata> items = context.ItemMetadata.Where(item => codes.Contains(item.Id)).ToDictionary(item => item.Id);
        Dictionary<int, FunctionCubeMetadata> cubes = context.FunctionCubeMetadata.ToDictionary(cube => cube.Id);

        var notSwitch = new List<int>();
        foreach (int code in codes) {
            if (!items.TryGetValue(code, out ItemMetadata? item) || item.Install is null || !cubes.TryGetValue(item.Install.ObjectCubeId, out FunctionCubeMetadata? cube) || cube.ControlType != InteractCubeControlType.Switch) {
                notSwitch.Add(code);
            }
        }

        Assert.That(notSwitch, Is.Empty, "every code should be furniture with a switch cube");
        Assert.That(trophies, Has.Count.EqualTo(111));
        Assert.That(codes, Has.Length.EqualTo(130));
    }

    [Test]
    public void RemakeEventsFollowThePreview() {
        string handler = File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Game", "PacketHandlers", "ChangeAttributesHandler.cs"));
        int counted = handler.IndexOf("item.TimeChangedOption++;", StringComparison.Ordinal);
        int preview = handler.IndexOf("ChangeAttributesPacket.PreviewItem(session.ChangeAttributesItem)", StringComparison.Ordinal);
        int select = handler.IndexOf("private static void HandleSelect(", StringComparison.Ordinal);
        Assert.That(counted, Is.GreaterThan(0), "the attempt count should be found");
        Assert.That(preview, Is.GreaterThan(counted), "the record should count this attempt");
        // Golden: pet_change_talents.msb, each attempt sends the preview, 23200038, then 23200039; keeping a roll sends no trophy. A /autoreroll run reports once, with its attempts.
        foreach (string kind in new[] { "pet", "item" }) {
            int attempt = handler.IndexOf($"ConditionType.{kind}_remake_option, counter: run.Attempts);", StringComparison.Ordinal);
            int record = handler.IndexOf($"ConditionType.{kind}_remake_option_record, counter: item.TimeChangedOption);", StringComparison.Ordinal);
            Assert.That(attempt, Is.GreaterThan(preview), kind);
            Assert.That(record, Is.GreaterThan(attempt), kind);
            Assert.That(record, Is.LessThan(select), kind);
        }
    }

    [Test]
    public void RemakeTrophiesNeedOnlyTheEvent() {
        string? path = DataDb.Find();
        if (path == null) {
            Assert.Ignore("maple-data.db not found (set MAPLE_DATA_DB)");
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        // Golden: the trophies' grades in the data. They name no code and no target, so the event needs neither.
        (int Id, ConditionType Type, long[] Values)[] expected = [
            (23200024, ConditionType.item_remake_option, new long[] { 1, 30, 100, 300, 1000 }),
            (23200025, ConditionType.item_remake_option_record, new long[] { 1, 10, 20 }),
            (23200038, ConditionType.pet_remake_option, new long[] { 1, 30, 100, 300, 1000 }),
            (23200039, ConditionType.pet_remake_option_record, new long[] { 1, 10, 20 }),
        ];
        foreach ((int id, ConditionType type, long[] values) in expected) {
            AchievementMetadata trophy = context.AchievementMetadata.Single(row => row.Id == id);
            Assert.That(trophy.Grades.Values.OrderBy(grade => grade.Grade).Select(grade => grade.Condition.Value).ToArray(), Is.EqualTo(values), $"trophy {id}");
            foreach (AchievementMetadataGrade grade in trophy.Grades.Values) {
                Assert.That(grade.Condition.Type, Is.EqualTo(type), $"trophy {id}");
                Assert.That(grade.Condition.Check(null!), Is.True, $"trophy {id}");
            }
        }
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
