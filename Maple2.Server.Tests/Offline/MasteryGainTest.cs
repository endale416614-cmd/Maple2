using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Offline;

/// <summary>BT-144: life skill consumables raise the items a gather gives and the mastery a craft gives by the buff's rate.</summary>
public class MasteryGainTest {
    [TestCase(1, 1f, 2)]
    [TestCase(3, 1f, 6)]
    [TestCase(10, 0.5f, 15)]
    [TestCase(3, 0.5f, 5)]
    [TestCase(5, 0.5f, 8)]
    [TestCase(7, 0.6f, 11)]
    [TestCase(3, 0f, 3)]
    [TestCase(0, 1f, 0)]
    public void WithRate_RoundsHalfUp(int amount, float rate, int expected) => Assert.That(MasteryManager.WithRate(amount, rate), Is.EqualTo(expected));

    [TestCase(MasteryType.Mining, SpecialAttribute.MiningRewardItem)]
    [TestCase(MasteryType.Gathering, SpecialAttribute.GatheringRewardItem)]
    [TestCase(MasteryType.Breeding, SpecialAttribute.BreedingRewardItem)]
    [TestCase(MasteryType.Farming, SpecialAttribute.FarmingRewardItem)]
    [TestCase(MasteryType.Blacksmithing, SpecialAttribute.SmithingRewardMastery)]
    [TestCase(MasteryType.Engraving, SpecialAttribute.EngravingRewardMastery)]
    [TestCase(MasteryType.Alchemist, SpecialAttribute.AlchemistRewardMastery)]
    [TestCase(MasteryType.Cooking, SpecialAttribute.CookingRewardMastery)]
    public void RateAttribute_EachLifeSkillHasItsOwn(MasteryType type, SpecialAttribute attribute) => Assert.That(MasteryManager.RateAttribute(type), Is.EqualTo(attribute));

    [TestCase(MasteryType.Fishing)]
    [TestCase(MasteryType.Music)]
    [TestCase(MasteryType.PetTaming)]
    public void RateAttribute_OthersHaveNone(MasteryType type) => Assert.That(MasteryManager.RateAttribute(type), Is.Null);

    [Test]
    public void SpecialRate_StaysOnRate() {
        var values = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Spirit, 100 } }, JobCode.Striker);
        values[SpecialAttribute.MiningRewardItem].AddRate(1f);
        values.Total();
        Assert.That(values[SpecialAttribute.MiningRewardItem].Rate, Is.EqualTo(1f));
        Assert.That(values[SpecialAttribute.MiningRewardItem].Total, Is.EqualTo(0), "so Multiplier() would read nothing");
    }

    [Test]
    public void WithBuffRate_ReadsTheRateOfItsOwnLifeSkill() {
        var values = new Stats(new Dictionary<BasicAttribute, long> { { BasicAttribute.Spirit, 100 } }, JobCode.Striker);
        values[SpecialAttribute.MiningRewardItem].AddRate(1f);
        var stats = Bare<StatsManager>();
        SetField(stats, "<Values>k__BackingField", values);
        var session = Bare<GameSession>();
        session.Stats = stats;
        var mastery = new MasteryManager(session);

        Assert.That(mastery.WithBuffRate(MasteryType.Mining, 3), Is.EqualTo(6), "a rate of 1 doubles the gather");
        Assert.That(mastery.WithBuffRate(MasteryType.Fishing, 3), Is.EqualTo(3), "fishing has no rate attribute");
    }

    private static void SetField(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
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
