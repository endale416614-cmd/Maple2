using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Util;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class TrophyCountTest {
    [Test]
    public void NextCounter() {
        Assert.That(AchievementManager.NextCounter(ConditionType.level, 12, 15), Is.EqualTo(15));
        Assert.That(AchievementManager.NextCounter(ConditionType.level, 15, 12), Is.EqualTo(15));
        Assert.That(AchievementManager.NextCounter(ConditionType.trophy_point, 40, 38), Is.EqualTo(40));
        Assert.That(AchievementManager.NextCounter(ConditionType.killcount, 3, 10), Is.EqualTo(10));
        Assert.That(AchievementManager.NextCounter(ConditionType.item_gear_score, 900, 800), Is.EqualTo(900));
        Assert.That(AchievementManager.NextCounter(ConditionType.npc, 12, 1), Is.EqualTo(13));
        Assert.That(AchievementManager.NextCounter(ConditionType.interior_level, 10, 1), Is.EqualTo(10));
        Assert.That(AchievementManager.NextCounter(ConditionType.interior_point, 1100, 300), Is.EqualTo(1100));
        // Golden: the client's text counts the record on a single item, and pet_change_talents.msb sends both counters at 1, then at 2.
        Assert.That(AchievementManager.NextCounter(ConditionType.item_remake_option_record, 20, 3), Is.EqualTo(20));
        Assert.That(AchievementManager.NextCounter(ConditionType.pet_remake_option_record, 1, 2), Is.EqualTo(2));
        Assert.That(AchievementManager.NextCounter(ConditionType.item_remake_option, 20, 1), Is.EqualTo(21));
        Assert.That(AchievementManager.NextCounter(ConditionType.pet_remake_option, 1, 1), Is.EqualTo(2));
    }

    [Test]
    public void Check() {
        // hero_achieve with Codes Integers [21300024, 21300025] and no target is true for codeLong 21300024 and false for 21300026
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            null
        ), null!, codeLong: 21300024), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            null
        ), null!, codeLong: 21300026), Is.False);

        // hero_achieve with those codes and Target Integers [1] is true for targetLong 1, false for targetLong 2
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [1]
            }
        ), null!, targetLong: 1, codeLong: 21300024), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21300024, 21300025]
            },
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [1]
            }
        ), null!, targetLong: 2, codeLong: 21300024), Is.False);

        // dungeon_first_clear [21001001, 25001001] true for 25001001
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_first_clear,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21001001, 25001001]
            },
            null
        ), null!, codeLong: 25001001), Is.True);
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_first_clear,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [21001001, 25001001]
            },
            null
        ), null!, codeLong: 21001001), Is.True);

        // dungeon_reward [27000] true for 27000
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.dungeon_reward,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [27000]
            },
            null
        ), null!, codeLong: 27000), Is.True);

        // quest_revise_achieve [90000660] true for 90000660
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.quest_revise_achieve,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [90000660]
            },
            null
        ), null!, codeLong: 90000660), Is.True);

        // trophy_point with Codes Range (20000000, 29999999) true for 20000000
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.trophy_point,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 20000000, Max = 29999999 }
            },
            null
        ), null!, codeLong: 20000000), Is.True);

        // controller with Codes Integers [50400002] and no target is true for codeLong 50400002 and false for 50300023 (its cube id)
        Assert.That(ConditionUtil.Check(Coded(ConditionType.controller, 50400002), null!, codeLong: 50400002), Is.True);
        Assert.That(ConditionUtil.Check(Coded(ConditionType.controller, 50400002), null!, codeLong: 50300023), Is.False);
    }

    private static Maple2.Model.Metadata.ConditionMetadata Coded(ConditionType type, params int[] codes) {
        return new Maple2.Model.Metadata.ConditionMetadata(
            type,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = codes
            },
            null
        );
    }

    [Test]
    public void BossKillCodes() {
        // npc_assist_bonus and npc_lasthit with Codes [23000018] are true for codeLong 23000018 and false for 23000019
        foreach (ConditionType type in new[] { ConditionType.npc_assist_bonus, ConditionType.npc_lasthit }) {
            Assert.That(ConditionUtil.Check(Coded(type, 23000018), null!, codeLong: 23000018), Is.True, type.ToString());
            Assert.That(ConditionUtil.Check(Coded(type, 23000018), null!, codeLong: 23000019), Is.False, type.ToString());
        }
    }

    [Test]
    public void MiniGameClearCodes() {
        // minigame_clear [61000006] (Crazy Runners) is true for 61000006 and false for 61000004 (Ludibrium Escape)
        Assert.That(ConditionUtil.Check(Coded(ConditionType.minigame_clear, 61000006), null!, codeLong: 61000006), Is.True);
        Assert.That(ConditionUtil.Check(Coded(ConditionType.minigame_clear, 61000006), null!, codeLong: 61000004), Is.False);
    }

    private static Achievement FiveGrades(int current, int reward, params int[] earned) {
        var grades = Enumerable.Range(1, 5).ToDictionary(g => g, g => new AchievementMetadataGrade(g, new ConditionMetadata(ConditionType.interact_object, g, null, null), null));
        var achievement = new Achievement(new AchievementMetadata(22200013, "", false, AchievementCategory.Adventure, [], grades)) { CurrentGrade = current, RewardGrade = reward };
        foreach (int grade in earned) achievement.Grades[grade] = 1;
        return achievement;
    }

    private static (int, int) WrittenGrades(Achievement achievement) {
        var writer = new ByteWriter();
        achievement.WriteTo(writer);
        var reader = new ByteReader(writer.Buffer[..writer.Length]);
        reader.Read<AchievementStatus>();
        reader.ReadInt();
        return (reader.ReadInt(), reader.ReadInt());
    }

    [Test]
    public void ClaimStopsAtEarnedGrades() {
        // Gilded Globetrotter after tier IV: grade 5 is the next grade, not earned, so a claim stops after grade 4
        Achievement achievement = FiveGrades(5, 4, 1, 2, 3, 4);
        Assert.That(AchievementManager.CanClaim(achievement, 4), Is.True);
        Assert.That(AchievementManager.CanClaim(achievement, 5), Is.False);

        achievement.Grades[5] = 1;
        Assert.That(AchievementManager.CanClaim(achievement, 5), Is.True);
        Assert.That(AchievementManager.CanClaim(achievement, 6), Is.False);
    }

    [Test]
    public void ClientSeesNoRewardGradePastCurrent() {
        // an old save with reward grade 6 on an unfinished trophy is sent as (5, 5); the finished shape (5, 6) and an ordinary (5, 4) are sent as they are
        Assert.That(WrittenGrades(FiveGrades(5, 6, 1, 2, 3, 4)), Is.EqualTo((5, 5)));
        Assert.That(WrittenGrades(FiveGrades(5, 6, 1, 2, 3, 4, 5)), Is.EqualTo((5, 6)));
        Assert.That(WrittenGrades(FiveGrades(5, 4, 1, 2, 3, 4)), Is.EqualTo((5, 4)));
    }

    [Test]
    public void SkillHitCodes() {
        // skill_damage_npc [50000374] is true for 50000374 and false for 50000376
        Assert.That(ConditionUtil.Check(Coded(ConditionType.skill_damage_npc, 50000374), null!, codeLong: 50000374), Is.True);
        Assert.That(ConditionUtil.Check(Coded(ConditionType.skill_damage_npc, 50000374), null!, codeLong: 50000376), Is.False);

        // skill_die [50000342] is true for 50000342 and false for 50000374
        Assert.That(ConditionUtil.Check(Coded(ConditionType.skill_die, 50000342), null!, codeLong: 50000342), Is.True);
        Assert.That(ConditionUtil.Check(Coded(ConditionType.skill_die, 50000342), null!, codeLong: 50000374), Is.False);
    }

    [Test]
    public void FieldMissionCodes() {
        // field_mission [72000149, 72000150] is true for 72000150 and false for 72000154
        Assert.That(ConditionUtil.Check(Coded(ConditionType.field_mission, 72000149, 72000150), null!, codeLong: 72000150), Is.True);
        Assert.That(ConditionUtil.Check(Coded(ConditionType.field_mission, 72000149, 72000150), null!, codeLong: 72000154), Is.False);

        // field_mission with Codes Range (72000736, 72000806) is true for 72000800 and false for 72000807
        Maple2.Model.Metadata.ConditionMetadata cartographer = new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.field_mission,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 72000736, Max = 72000806 }
            },
            null
        );
        Assert.That(ConditionUtil.Check(cartographer, null!, codeLong: 72000800), Is.True);
        Assert.That(ConditionUtil.Check(cartographer, null!, codeLong: 72000807), Is.False);

        // field_mission without Codes (Master of Adventure) is true for 72000149
        Assert.That(ConditionUtil.Check(new Maple2.Model.Metadata.ConditionMetadata(ConditionType.field_mission, 0, null, null), null!, codeLong: 72000149), Is.True);
    }

    [Test]
    public void HeroAchieveGradeTarget() {
        // hero_achieve_grade with Codes Range (21220001, 21230999) and no target is true for codeLong 21230002 and false for 21231002
        Maple2.Model.Metadata.ConditionMetadata merciless = new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve_grade,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 21220001, Max = 21230999 }
            },
            null
        );
        Assert.That(ConditionUtil.Check(merciless, null!, targetLong: 1, codeLong: 21230002), Is.True);
        Assert.That(ConditionUtil.Check(merciless, null!, targetLong: 1, codeLong: 21231002), Is.False);

        // hero_achieve_grade with Codes [22300068..22300072] and Target Integers [1] is true for targetLong 1, false for targetLong 2
        Maple2.Model.Metadata.ConditionMetadata rumbler = new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.hero_achieve_grade,
            0,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [22300068, 22300069, 22300070, 22300071, 22300072]
            },
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Integers = [1]
            }
        );
        Assert.That(ConditionUtil.Check(rumbler, null!, targetLong: 1, codeLong: 22300070), Is.True);
        Assert.That(ConditionUtil.Check(rumbler, null!, targetLong: 2, codeLong: 22300070), Is.False);
    }

    [Test]
    public void TargetRanges() {
        // skill with no Codes and Target Range (1, 5) is true for targetLong 3, 1 and 5 and false for 6
        Maple2.Model.Metadata.ConditionMetadata skill = new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.skill,
            0,
            null,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 1, Max = 5 }
            }
        );
        Assert.That(ConditionUtil.Check(skill, null!, targetLong: 3), Is.True);
        Assert.That(ConditionUtil.Check(skill, null!, targetLong: 1), Is.True);
        Assert.That(ConditionUtil.Check(skill, null!, targetLong: 5), Is.True);
        Assert.That(ConditionUtil.Check(skill, null!, targetLong: 6), Is.False);

        // fish with Target Range (100, 200) is true for 150 and false for 250
        Maple2.Model.Metadata.ConditionMetadata fish = new Maple2.Model.Metadata.ConditionMetadata(
            ConditionType.fish,
            0,
            null,
            new Maple2.Model.Metadata.ConditionMetadata.Parameters {
                Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 100, Max = 200 }
            }
        );
        Assert.That(ConditionUtil.Check(fish, null!, targetLong: 150), Is.True);
        Assert.That(ConditionUtil.Check(fish, null!, targetLong: 250), Is.False);
    }

    [Test]
    public void MissionIds() {
        // Integers [72000149, 72000150] give exactly those ids; Range (72000736, 72000738) gives the three ids; no codes give none
        Assert.That(AchievementManager.MissionIds(new Maple2.Model.Metadata.ConditionMetadata.Parameters {
            Integers = [72000149, 72000150]
        }), Is.EqualTo(new[] { 72000149, 72000150 }));
        Assert.That(AchievementManager.MissionIds(new Maple2.Model.Metadata.ConditionMetadata.Parameters {
            Range = new Maple2.Model.Metadata.ConditionMetadata.Range<int> { Min = 72000736, Max = 72000738 }
        }), Is.EqualTo(new[] { 72000736, 72000737, 72000738 }));
        Assert.That(AchievementManager.MissionIds(null), Is.Empty);
    }
}
