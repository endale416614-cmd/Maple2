using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Maple2.Model.Game;
using Maple2.Server.Core.Constants;
using Port = Maple2.Server.Game.LuaFunctions.Lua;

namespace Maple2.Server.Tests.Lua;

[NonParallelizable]
public class LuaTests {
    private readonly Maple2.Lua.Lua maple2Lua = new Maple2.Lua.Lua(Target.LOCALE);

    [OneTimeTearDown]
    public void OneTimeTearDown() {
        maple2Lua.Dispose();
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringThrowingStar() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13460307); // Exceptional Adventuring Throwing Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(4177));
            Assert.That(baseGearScore, Is.EqualTo(4177));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(0));
            Assert.That(enchantScore, Is.EqualTo(0));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(4177));
            Assert.That(totalGearScore, Is.EqualTo(4177));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringThrowingStar_Enchanted() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13460307); // Exceptional Adventuring Throwing Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(4178));
            Assert.That(baseGearScore, Is.EqualTo(4178));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(2088));
            Assert.That(enchantScore, Is.EqualTo(2088));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(6266));
            Assert.That(totalGearScore, Is.EqualTo(6266));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringKnife() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13160314); // Exceptional Adventuring Knife

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(4178));
            Assert.That(baseGearScore, Is.EqualTo(4178));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(2088));
            Assert.That(enchantScore, Is.EqualTo(2088));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(6266));
            Assert.That(totalGearScore, Is.EqualTo(6266));
        });
    }

    [Test]
    public void TestCalcItemLevel_RookStar() {
        // Arrange
        const int gearScore = 7;
        const int rarity = 1;
        var itemType = new ItemType(13400218); // Rook's Star

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0);

        // Throwing stars and daggers have half the gear score
        int maple2LuaTotalGearScore = maple2LuaBaseGearScore + maple2LuaEnchantScore;
        int totalGearScore = baseGearScore + enchantScore;
        if (itemType.IsThrowingStar || itemType.IsDagger) {
            maple2LuaTotalGearScore /= 2;
            maple2LuaEnchantScore /= 2;
            maple2LuaBaseGearScore = maple2LuaTotalGearScore - maple2LuaEnchantScore;

            totalGearScore /= 2;
            enchantScore /= 2;
            baseGearScore = totalGearScore - enchantScore;
        }

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(35));
            Assert.That(baseGearScore, Is.EqualTo(35));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(0));
            Assert.That(enchantScore, Is.EqualTo(0));

            // total
            Assert.That(maple2LuaTotalGearScore, Is.EqualTo(35));
            Assert.That(totalGearScore, Is.EqualTo(35));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringCodex() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(14060270); // Exceptional Adventuring Codex

        // Act
        int maple2LuaResult = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0).Item1;
        int luaResult = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 0, limitBreakLevel: 0).Item1;

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(8355));
            Assert.That(luaResult, Is.EqualTo(8355));
        });
    }

    [Test]
    public void TestCalcItemLevel_ExceptionalAdventuringScepter() {
        // Arrange
        const int gearScore = 67;
        const int rarity = 4;
        var itemType = new ItemType(13360308); // Exceptional Adventuring Scepter

        // Act
        (int maple2LuaBaseGearScore, int maple2LuaEnchantScore) = maple2Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);
        (int baseGearScore, int enchantScore) = Server.Game.LuaFunctions.Lua.CalcItemLevel(gearScore, rarity, itemType.Type, enchantLevel: 10, limitBreakLevel: 0);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaBaseGearScore, Is.EqualTo(8355));
            Assert.That(baseGearScore, Is.EqualTo(8355));

            Assert.That(maple2LuaEnchantScore, Is.EqualTo(4177));
            Assert.That(enchantScore, Is.EqualTo(4177));

            // total
            Assert.That(maple2LuaBaseGearScore + maple2LuaEnchantScore, Is.EqualTo(12532));
            Assert.That(baseGearScore + enchantScore, Is.EqualTo(12532));
        });
    }

    [TestCase(0, 0, 1f)]
    [TestCase(100, 0, 1.1f)]
    [TestCase(1000, 0, 2f)]
    [TestCase(2000, 0, 2.5f)]
    [TestCase(3000, 14, 4f)]
    [TestCase(3500, 14, 4.5f)]
    [TestCase(4000, 14, 5f)]
    public void TestCalcCritDamage(int criticalDamage, int mode, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcCritDamage(criticalDamage, mode);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcCritDamage(criticalDamage, mode);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(80, 586, 111, 50, 0, 0, 0.13f)] // random test character
    public void TestCalcPlayerCritRate(int jobCode, long luk, long critRate, long critResistance, int finalCapV, int mode, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcPlayerCritRate(jobCode, luk, critRate, critResistance, finalCapV, mode);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcPlayerCritRate(jobCode, luk, critRate, critResistance, finalCapV, mode);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(100, 50, 0.08f)] // guessed random values
    public void TestCalcNpcCritRate(long critRate, long critResistance, float expected) {
        // Act
        float maple2LuaResult = maple2Lua.CalcNpcCritRate(0, critRate, critResistance);
        float luaResult = Server.Game.LuaFunctions.Lua.CalcNpcCritRate(0, critRate, critResistance);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(1, 0)]
    [TestCase(8, 0)]
    [TestCase(10, 100)]
    [TestCase(90, 900)]
    public void TestCalcRevivalPrice(int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcRevivalPrice((ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcRevivalPrice((ushort) level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(5, 10, 137)]
    [TestCase(10, 10, 275)]
    [TestCase(15, 20, 1099)]
    [TestCase(5, 50, 2635)]
    [TestCase(5, 90, 7214)]
    public void CalcTaxiCharge(int distance, int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcTaxiCharge(distance, (ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcTaxiCharge(distance, level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(10, 30000)]
    [TestCase(50, 50000)]
    [TestCase(90, 70000)]
    public void CalcAirTaxiCharge(int level, int expected) {
        // Act
        int maple2LuaResult = maple2Lua.CalcAirTaxiCharge((ushort) level);
        int luaResult = Server.Game.LuaFunctions.Lua.CalcAirTaxiCharge(level);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult, Is.EqualTo(expected));
            Assert.That(luaResult, Is.EqualTo(expected));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 10)] // fire prism star
    public void TestConstantValueCap(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, int expected) {
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueCap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueCap(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected));
            Assert.That(luaResult.Item1, Is.EqualTo(expected));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 67659f)] // fire prism star
    public void TestConstantValueWapMax(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, float expected) {
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueWapMax(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueWapMax(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult.Item1, Is.EqualTo(expected).Within(0.01f));
        });
    }

    [TestCase(0, 1, 34, 0, 80, 6, 70, 50008f)] // fire prism star
    public void TestConstantValueWapMin(int baseValue, int deviation, int itemType, int jobCode, int optionLevelFactor, int grade, int levelLimit, float expected) {
        // Act
        (float, float) maple2LuaResult = maple2Lua.ConstantValueWapMin(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);
        (float, float) luaResult = Server.Game.LuaFunctions.Lua.ConstantValueWapMin(baseValue, deviation, itemType, jobCode, optionLevelFactor, grade, (ushort) levelLimit);

        // Assert
        Assert.Multiple(() => {
            Assert.That(maple2LuaResult.Item1, Is.EqualTo(expected).Within(0.01f));
            Assert.That(luaResult.Item1, Is.EqualTo(expected).Within(0.01f));
        });
    }

    // FT-02: every function of Lua.cs on a grid against the client's bytecode. A grid stays inside what the bytecode accepts:
    // a Lua error throws and leaves the next call's result wrong.
    private sealed record Formula(IEnumerable<int[]> Cases, Func<Maple2.Lua.Lua, int[], object> Original, Func<int[], object> Ported);

    // Cases of a grid that differ today, each held by a bug row. A fix lowers the count and fails the test:
    // lower the count, and drop the entry when the row closes.
    private static readonly Dictionary<string, (string Row, int Cases)> KnownMismatches = new() {
        ["CalcKillCount"] = ("BT-201", 648),
        ["CalcItemLevel"] = ("BT-202", 34416),
        ["CalcGetItemRemakeIngredientNew"] = ("BT-203", 2400),
        ["CalcGetPetRemakeIngredient"] = ("BT-204", 12),
        ["StaticValueAddwap"] = ("BT-205", 9),
        ["StaticValueWapMax"] = ("BT-205", 40),
        ["StaticValueHp"] = ("BT-205", 373),
        ["StaticValueNdd"] = ("BT-205", 99),
        ["StaticValuePap"] = ("BT-205", 206),
        ["StaticValueMap"] = ("BT-205", 206),
        ["StaticValuePar"] = ("BT-205", 369),
        ["StaticValueMar"] = ("BT-205", 129),
        ["ConstantValueAddwap"] = ("BT-205", 23),
        ["ConstantValueAddndd"] = ("BT-205", 1),
        ["ConstantValueNdd"] = ("BT-205", 11),
        ["ConstantValueMar"] = ("BT-205", 32),
        ["ConstantValueWapMin"] = ("BT-205", 111),
        ["ConstantValueWapMax"] = ("BT-205", 132),
    };

    private const int Stride = 211;
    private static readonly int[] Grades = [1, 2, 3, 4, 5, 6];
    private static readonly int[] Levels = [1, 10, 30, 50, 52, 56, 60, 70, 80, 100];
    private static readonly int[] Factors = [0, 1, 10, 16, 30, 45, 50, 54, 60, 70, 80, 85];
    private static readonly int[] Jobs = [0, 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110];
    private static readonly int[] Weapons = [30, 31, 32, 33, 34, 50, 51, 52, 53, 54, 55, 56];
    private static readonly int[] Protectives = [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 41];
    private static readonly int[] Gear = [12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 30, 31, 32, 33, 34, 40, 41, 50, 51, 52, 53, 54, 55, 56];
    private static readonly int[] KillCounts = [0, 1, 19, 20, 21, 29, 30, 31, 39, 40, 41, 49, 50, 51, 99, 101, 1000];

    /// <summary>Every stride-th case of the product of the axes, so a prime stride walks all of them; stride 1 is the full product.</summary>
    private static IEnumerable<int[]> Grid(int stride, params int[][] axes) {
        long total = axes.Aggregate(1L, (product, axis) => product * axis.Length);
        for (long i = 0; i < total; i += stride) {
            long rest = i;
            yield return axes.Select(axis => {
                int value = axis[rest % axis.Length];
                rest /= axis.Length;
                return value;
            }).ToArray();
        }
    }

    private static IEnumerable<int[]> Stat(int[] types) => Grid(Stride, [0, 5, 18, 100, 1500], [1, 2], types, Jobs, Factors, Grades, Levels, [0, 1]);

    private static readonly Dictionary<string, Formula> Formulas = new() {
        ["CalcCritDamage"] = new(Grid(1, [0, 1, 100, 500, 999, 1000, 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 10000], [0, 14]),
            (lua, a) => lua.CalcCritDamage(a[0], a[1]), a => Port.CalcCritDamage(a[0], a[1])),
        ["CalcPlayerCritRate"] = new(Grid(1, [1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 999], [0, 100, 586, 2000, 20000], [0, 111, 1000, 10000], [0, 1, 50, 500, 2000], [0, 14]),
            (lua, a) => lua.CalcPlayerCritRate(a[0], a[1], a[2], a[3], 0, 0f, a[4]), a => Port.CalcPlayerCritRate(a[0], a[1], a[2], a[3], 0, a[4])),
        ["CalcNpcCritRate"] = new(Grid(1, [0, 100], [0, 100, 1000, 10000], [0, 1, 50, 500, 2000]),
            (lua, a) => lua.CalcNpcCritRate(a[0], a[1], a[2]), a => Port.CalcNpcCritRate(a[0], a[1], a[2])),
        ["CalcItemLevel"] = new(Grid(1, [0, 1, 7, 20, 50, 52, 55, 60, 64, 67, 70, 80, 82], Grades, Gear, [0, 1, 5, 10, 12, 15], [0, 30, 59, 60, 69, 70, 99]),
            (lua, a) => lua.CalcItemLevel(a[0], a[1], a[2], a[3], a[4]), a => Port.CalcItemLevel(a[0], a[1], a[2], a[3], a[4])),
        ["CalcItemSocketUnlockIngredient"] = new(Grid(1, [0, 12, 19, 20, 34], Grades, [1, 10, 49, 50, 51, 55, 56, 57, 60, 70, 80, 100], [0, 1], [0, 1, 2]),
            (lua, a) => lua.CalcItemSocketUnlockIngredient(a[0], a[1], (ushort) a[2], a[3], a[4]), a => Port.CalcItemSocketUnlockIngredient(a[0], a[1], a[2], a[3], a[4])),
        ["CalcGetGemStonePutOffPrice"] = new(Grid(1, Grades, [0, 1, 2, 5, 7, 8, 9, 10, 15], [0, 1]),
            (lua, a) => lua.CalcGetGemStonePutOffPrice(a[0], (ushort) a[1], a[2]), a => Port.CalcGetGemStonePutOffPrice(a[0], (ushort) a[1], a[2])),
        ["CalcResolvePenaltyPrice"] = new(Grid(1, Levels, [0, 1, 5, 10], [-1, 0, 1, 2]),
            (lua, a) => lua.CalcResolvePenaltyPrice((ushort) a[0], a[1], a[2]), a => Port.CalcResolvePenaltyPrice((ushort) a[0], a[1], a[2])),
        ["CalcRevivalPrice"] = new(Grid(1, [0, 1, 8, 9, 10, 50, 90, 100]),
            (lua, a) => lua.CalcRevivalPrice((ushort) a[0]), a => Port.CalcRevivalPrice((ushort) a[0])),
        ["CalcKillCountBonusExpRate"] = new(Grid(1, KillCounts),
            (lua, a) => lua.CalcKillCountBonusExpRate(a[0]), a => Port.CalcKillCountBonusExpRate(a[0])),
        ["CalcKillCountGrade"] = new(Grid(1, KillCounts),
            (lua, a) => lua.CalcKillCountGrade(a[0]), a => Port.CalcKillCountGrade(a[0])),
        ["CalcKillCountMsg"] = new(Grid(1, KillCounts),
            (lua, a) => lua.CalcKillCountMsg(a[0]), a => Port.CalcKillCountMsg(a[0])),
        ["CalcKillCount"] = new(Grid(1, [1, 10, 44, 50, 60, 90], [1, 5, 10, 45, 49, 50, 55, 70], [0, 1000, 4999, 5000, 9000], [0, 1, 2, 5, 100]),
            (lua, a) => lua.CalcKillCount((ushort) a[0], (ushort) a[1], a[2], a[3]), a => Port.CalcKillCount(a[0], a[1], a[2], a[3])),
        ["CalcBlackMarketCostRate"] = new(Grid(1, [0, 1, 2]),
            (lua, a) => lua.CalcBlackMarketCostRate(a[0]), a => Port.CalcBlackMarketCostRate(a[0])),
        ["CalcBlackMarketBuyFeeCost"] = new(Grid(1, [0, 1, 9, 10, 99, 100, 1000, 12345, 1000000], Grades, [0, 1, 50, 70], [0, 1]),
            (lua, a) => lua.CalcBlackMarketBuyFeeCost(a[0], a[1], (ushort) a[2], a[3]), a => Port.CalcBlackMarketBuyFeeCost(a[0], a[1], a[2], a[3])),
        ["CalcBlackMarketRegisterDepositMax"] = new(Grid(1),
            (lua, _) => lua.CalcBlackMarketRegisterDepositMax(), _ => Port.CalcBlackMarketRegisterDepositMax()),
        ["CalcBlackMarketRegisterDepositPercent"] = new(Grid(1),
            (lua, _) => lua.CalcBlackMarketRegisterDepositPercent(), _ => Port.CalcBlackMarketRegisterDepositPercent()),
        ["CalcBlackMarketRegisterDeposit"] = new(Grid(1, [0, 1, 99, 100, 101, 1000, 12345, 10000000, 100000000]),
            (lua, a) => lua.CalcBlackMarketRegisterDeposit(a[0]), a => Port.CalcBlackMarketRegisterDeposit(a[0])),
        ["CalcGatheringObjectSuccessRate"] = new(Grid(1, [0, 1, 5, 10, 20, 50, 100, 200, 500], [0, 5, 10, 20, 100], [1, 5, 10, 20, 50, 100, 200], [0, 1]),
            (lua, a) => lua.CalcGatheringObjectSuccessRate(a[0], a[1], a[2], a[3]), a => Port.CalcGatheringObjectSuccessRate(a[0], a[1], a[2], a[3])),
        ["CalcGatheringObjectMaxCount"] = new(Grid(1, [0, 5, 10, 20, 100], [1, 5, 10, 20, 50, 100, 200]),
            (lua, a) => lua.CalcGatheringObjectMaxCount(a[0], a[1]), a => Port.CalcGatheringObjectMaxCount(a[0], a[1])),
        ["CalcSendMailFee"] = new(Grid(1, [0, 1, 100, 101, 1000, 123456], [0, 1, 5], [0, 1, 5], [0, 1, 5], [0, 1, 5], [0, 1, 5], [0, 1, 5]),
            (lua, a) => lua.CalcSendMailFee(a[0], a[1], a[2], a[3], a[4], a[5], a[6]), a => Port.CalcSendMailFee(a[0], a[1], a[2], a[3], a[4], a[5], a[6])),
        // The bytecode's wrapper returns an int, and the one caller casts the port's float to int.
        ["CalcNpcSpawnWeight"] = new(Grid(1, [0, 1, 2, 3, 4], [0, 1, 2, 3, 4], [0, 1, 10, 50, 100], [-5, 0, 1, 5, 10, 11, 12, 20]),
            (lua, a) => lua.CalcNpcSpawnWeight(a[0], a[1], a[2], a[3]), a => (int) Port.CalcNpcSpawnWeight(a[0], a[1], a[2], a[3])),
        ["CalcTaxiCharge"] = new(Grid(1, [0, 1, 2, 5, 10, 15, 30], [1, 10, 24, 25, 26, 50, 70, 90, 100]),
            (lua, a) => lua.CalcTaxiCharge(a[0], (ushort) a[1]), a => Port.CalcTaxiCharge(a[0], a[1])),
        ["CalcAirTaxiCharge"] = new(Grid(1, [1, 9, 10, 11, 50, 90, 100]),
            (lua, a) => lua.CalcAirTaxiCharge((ushort) a[0]), a => Port.CalcAirTaxiCharge(a[0])),
        ["CalcRevivalMerat"] = new(Grid(1, [1, 50, 90], [0, 1, 2, 3, 4, 5, 10, 50]),
            (lua, a) => lua.CalcRevivalMeret((ushort) a[0], a[1]), a => Port.CalcRevivalMerat(a[0], a[1])),
        ["GetMesoRevivalDailyMaxCount"] = new(Grid(1),
            (lua, _) => lua.GetMesoRevivalDailyMaxCount(), _ => Port.GetMesoRevivalDailyMaxCount()),
        ["CalcRevivalMeso"] = new(Grid(1, [1, 10, 11, 50, 90, 100], [0, 1, 2, 3], [0, 1, 2, 3, 4]),
            (lua, a) => lua.CalcRevivalMeso((ushort) a[0], a[1], a[2]), a => Port.CalcRevivalMeso(a[0], a[1], a[2])),
        ["CalcGetItemRemakeIngredientNew"] = new(Grid(1, Gear, [0, 1, 5, 9, 10, 14, 15, 20], Grades, Levels),
            (lua, a) => lua.CalcGetItemRemakeIngredientNew(a[0], a[1], a[2], a[3]), a => Port.CalcGetItemRemakeIngredientNew(a[0], a[1], a[2], a[3])),
        ["CalcGetPetRemakeIngredient"] = new(Grid(1, [0, 1, 2, 5, 14, 15, 20], Grades, [0, 1]),
            (lua, a) => lua.CalcGetPetRemakeIngredient(a[0], a[1], a[2]), a => Port.CalcGetPetRemakeIngredient(a[0], a[1], a[2])),
        ["CalcItemSocketMaxCount"] = new(Grid(1, [0, 11, 12, 13, 18, 19, 20, 21, 30, 34, 50], Grades, [1, 10, 49, 50, 51, 60, 70, 80, 100], [0, 1]),
            (lua, a) => lua.CalcItemSocketMaxCount(a[0], a[1], (ushort) a[2], a[3]), a => Port.CalcItemSocketMaxCount(a[0], a[1], a[2], a[3])),
        // PE-03: rarities 1 to 5 only, as the bytecode's tables hold five; grade (the first argument) is not read by calc_petEnchantExp.
        ["CalcPetExp"] = new(Grid(1, [0, 1, 22000, 55000, 90000, 123457], [1, 49, 50, 51, 60, 70, 80]),
            (lua, a) => lua.CalcPetExp(a[0], (ushort) a[1]), a => Port.CalcPetExp(a[0], a[1])),
        ["CalcPetEnchantExp"] = new(Grid(1, [1, 5], [1, 2, 3, 4, 5], [1, 2, 12, 50], [0, 1, 20416, 89999], [0, 1, 23, 24, 25, 40, 59, 60, 100], [0, 24, 60, 70], [1, 50, 60, 70]),
            (lua, a) => lua.CalcPetEnchantExp(a[0], a[1], (ushort) a[2], a[3], a[4], a[5], (ushort) a[6]), a => Port.CalcPetEnchantExp(a[0], a[1], a[2], a[3], a[4], a[5], a[6])),
        ["CalcPetEnchantCost"] = new(Grid(1, [1, 2, 3, 4, 5], [1, 3, 5], [0, 1, 2, 12, 49, 50], [0, 20416]),
            (lua, a) => lua.CalcPetEnchantCost(a[0], a[1], (ushort) a[2], a[3]), a => Port.CalcPetEnchantCost(a[0], a[1], a[2], a[3])),
        ["CalcPetEnchantBookCost"] = new(Grid(1, [1, 2, 3, 4, 5]),
            (lua, a) => lua.CalcPetEnchantBookCost(a[0]), a => Port.CalcPetEnchantBookCost(a[0])),
        ["StaticRateAbp"] = new(Stat(Gear),
            (lua, a) => lua.StaticRateAbp(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticRateAbp(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueAddwap"] = new(Stat(Weapons),
            (lua, a) => lua.StaticValueAddWap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueAddwap(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueWapMax"] = new(Stat(Weapons),
            (lua, a) => lua.StaticValueWapMax(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueWapMax(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueHp"] = new(Stat(Gear),
            (lua, a) => lua.StaticValueHp(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueHp(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueAddNdd"] = new(Stat(Protectives),
            (lua, a) => lua.StaticValueAddNdd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueAddNdd(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueNdd"] = new(Stat(Protectives),
            (lua, a) => lua.StaticValueNdd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueNdd(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValuePap"] = new(Stat(Gear),
            (lua, a) => lua.StaticValuePap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValuePap(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueMap"] = new(Stat(Gear),
            (lua, a) => lua.StaticValueMap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueMap(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValuePar"] = new(Stat(Gear),
            (lua, a) => lua.StaticValuePar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValuePar(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["StaticValueMar"] = new(Stat(Gear),
            (lua, a) => lua.StaticValueMar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.StaticValueMar(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7])),
        ["ConstantValueAddwap"] = new(Stat(Weapons),
            (lua, a) => lua.ConstantValueAddWap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueAddwap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueStr"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueStr(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueStr(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueInt"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueInt(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueInt(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueDex"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueDex(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueDex(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueLuk"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueLuk(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueLuk(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueHp"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueHp(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueHp(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueCap"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueCap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueCap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueAddndd"] = new(Stat(Protectives),
            (lua, a) => lua.ConstantValueAddNdd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueAddndd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueNdd"] = new(Stat(Protectives),
            (lua, a) => lua.ConstantValueNdd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueNdd(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueMar"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueMar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueMar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValuePar"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValuePar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValuePar(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueMap"] = new(Stat(Gear),
            (lua, a) => lua.ConstantValueMap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueMap(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueWapMin"] = new(Stat(Weapons),
            (lua, a) => lua.ConstantValueWapMin(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueWapMin(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
        ["ConstantValueWapMax"] = new(Stat(Weapons),
            (lua, a) => lua.ConstantValueWapMax(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], a[7]), a => Port.ConstantValueWapMax(a[0], a[1], a[2], a[3], a[4], a[5], (ushort) a[6], (ushort) a[7])),
    };

    private static IEnumerable<string> FormulaNames => Formulas.Keys;

    private static object?[] Values(object result) => result is ITuple tuple ? Enumerable.Range(0, tuple.Length).Select(i => tuple[i]).ToArray() : [result];

    // A number may differ by float noise only: the port computes in float, the bytecode in double. NaN on both sides is 0 over 0 on both sides.
    private static bool Same(object? original, object? ported) {
        if (original is string || ported is string) {
            return Equals(original, ported);
        }

        double a = Convert.ToDouble(original), b = Convert.ToDouble(ported);
        return a.Equals(b) || Math.Abs(a - b) <= 0.0001 + Math.Abs(a) * 0.000001;
    }

    [TestCaseSource(nameof(FormulaNames))]
    public void BytecodeGrid(string name) {
        Formula formula = Formulas[name];
        (string row, int known) = KnownMismatches.GetValueOrDefault(name, ("no row", 0));
        var mismatches = new List<string>();
        foreach (int[] args in formula.Cases) {
            object?[] original = Values(formula.Original(maple2Lua, args));
            object?[] ported = Values(formula.Ported(args));
            // AddWap and AddNdd: the bytecode returns one value and the port two, of which the callers read the first.
            if (!original.Zip(ported).All(pair => Same(pair.First, pair.Second))) {
                mismatches.Add($"({string.Join(",", args)}) bytecode {string.Join("|", original)} port {string.Join("|", ported)}");
            }
        }

        Assert.That(mismatches, Has.Count.EqualTo(known), $"{name} ({row}): {string.Join("; ", mismatches.Take(10))}");
    }

    [Test]
    public void EveryFunctionHasABytecodeGrid() {
        Assert.That(Formulas.Keys, Is.EquivalentTo(typeof(Port).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name)));
    }

    // The bytecode picks one of the two lines at random, and so does the port, so this case is not on the grid.
    [Test]
    public void CalcKillCountMsg_At100IsOneOfTwo() {
        Assert.Multiple(() => {
            Assert.That(maple2Lua.CalcKillCountMsg(100), Is.AnyOf("s_killcount_msg4", "s_killcount_msg5"));
            Assert.That(Port.CalcKillCountMsg(100), Is.AnyOf("s_killcount_msg4", "s_killcount_msg5"));
        });
    }

    // Golden: Data/lua/Precompiled.m2d luapack.o of the shipped GMS client, 74,626 bytes. The grids mean the client only while the package carries these bytes.
    [Test]
    public void TheBytecodeIsTheShippedClients() {
        using System.IO.Stream stream = typeof(Maple2.Lua.Lua).Assembly.GetManifestResourceStream("Maple2.Lua.luapack.o")!;
        Assert.That(Convert.ToHexString(SHA256.HashData(stream)), Is.EqualTo("4342722EABED4C6BD8C722A686EFE710685F6817CFF02F5B34B16F9429F00F49"));
    }
}
