using System;
using System.IO;
using NUnit.Framework;

namespace Maple2.Server.Tests.Tools;

[TestFixture]
public class EnumSkipLogTests {
    private enum TestEnum {
        Value1,
        Value2,
        Value3
    }

    [Flags]
    private enum FlagsEnum {
        Flag1 = 1,
        Flag2 = 2,
        Flag3 = 4
    }

    [SetUp]
    public void SetUp() {
        Maple2.Tools.Extensions.EnumSkipLog.Clear();
    }

    [Test]
    public void ParseOrDefault_KnownValue_ParsesSuccessfully() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("Value1", TestEnum.Value3, "test");
        Assert.That(result, Is.EqualTo(TestEnum.Value1));
    }

    [Test]
    public void ParseOrDefault_KnownValueIgnoreCase_ParsesSuccessfully() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("value1", TestEnum.Value3, "test", ignoreCase: true);
        Assert.That(result, Is.EqualTo(TestEnum.Value1));
    }

    [Test]
    public void ParseOrDefault_UnknownValue_ReturnsFallbackAndLogs() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("UnknownValue", TestEnum.Value3, "test");
        Assert.That(result, Is.EqualTo(TestEnum.Value3));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries, Contains.Item("test: TestEnum 'UnknownValue'"));
    }

    [Test]
    public void ParseOrDefault_NullValue_ReturnsFallbackAndDoesNotLog() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>(null, TestEnum.Value3, "test");
        Assert.That(result, Is.EqualTo(TestEnum.Value3));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void ParseOrDefault_EmptyValue_ReturnsFallbackAndDoesNotLog() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("", TestEnum.Value3, "test");
        Assert.That(result, Is.EqualTo(TestEnum.Value3));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void ParseOrDefault_UndefinedNumericString_ReturnsFallbackAndLogs() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("999", TestEnum.Value3, "test");
        Assert.That(result, Is.EqualTo(TestEnum.Value3));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries, Contains.Item("test: TestEnum '999'"));
    }

    [Test]
    public void TryParseOrSkip_KnownValue_ParsesSuccessfully() {
        var success = Maple2.Tools.Extensions.EnumSkipLog.TryParseOrSkip<TestEnum>("Value1", "test", out TestEnum result);
        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(TestEnum.Value1));
    }

    [Test]
    public void TryParseOrSkip_UnknownValue_ReturnsFalseAndDefaultResult() {
        var success = Maple2.Tools.Extensions.EnumSkipLog.TryParseOrSkip<TestEnum>("UnknownValue", "test", out TestEnum result);
        Assert.That(success, Is.False);
        Assert.That(result, Is.EqualTo(default(TestEnum)));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries, Contains.Item("test: TestEnum 'UnknownValue'"));
    }

    [Test]
    public void TryParseOrSkip_NullValue_ReturnsFalseAndDefaultResult() {
        var success = Maple2.Tools.Extensions.EnumSkipLog.TryParseOrSkip<TestEnum>(null, "test", out TestEnum result);
        Assert.That(success, Is.False);
        Assert.That(result, Is.EqualTo(default(TestEnum)));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void TryParseOrSkip_EmptyValue_ReturnsFalseAndDefaultResult() {
        var success = Maple2.Tools.Extensions.EnumSkipLog.TryParseOrSkip<TestEnum>("", "test", out TestEnum result);
        Assert.That(success, Is.False);
        Assert.That(result, Is.EqualTo(default(TestEnum)));
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void Clear_EmptiesEntries() {
        Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("UnknownValue", TestEnum.Value3, "test");
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.GreaterThan(0));
        Maple2.Tools.Extensions.EnumSkipLog.Clear();
        Assert.That(Maple2.Tools.Extensions.EnumSkipLog.Entries.Count, Is.EqualTo(0));
    }

    [Test]
    public void WriteLog_WritesEntriesToFile() {
        Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("UnknownValue1", TestEnum.Value3, "test");
        Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<TestEnum>("UnknownValue2", TestEnum.Value3, "test");

        string tempPath = Path.GetTempFileName();
        Maple2.Tools.Extensions.EnumSkipLog.WriteLog(tempPath);

        var lines = File.ReadAllLines(tempPath);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("test: TestEnum 'UnknownValue1'"));
        Assert.That(lines[1], Is.EqualTo("test: TestEnum 'UnknownValue2'"));

        File.Delete(tempPath);
    }

    [Test]
    public void WriteLog_EmptyEntries_WritesEmptyFile() {
        Maple2.Tools.Extensions.EnumSkipLog.Clear();
        string tempPath = Path.GetTempFileName();
        Maple2.Tools.Extensions.EnumSkipLog.WriteLog(tempPath);

        var lines = File.ReadAllLines(tempPath);
        Assert.That(lines, Has.Length.EqualTo(0));

        File.Delete(tempPath);
    }

    [Test]
    public void FlagsEnum_ParseOrDefault_AcceptsAnyValidParse() {
        var result = Maple2.Tools.Extensions.EnumSkipLog.ParseOrDefault<FlagsEnum>("Flag1", FlagsEnum.Flag3, "test");
        Assert.That(result, Is.EqualTo(FlagsEnum.Flag1));
    }

    [Test]
    public void FlagsEnum_TryParseOrSkip_AcceptsAnyValidParse() {
        var success = Maple2.Tools.Extensions.EnumSkipLog.TryParseOrSkip<FlagsEnum>("Flag1", "test", out FlagsEnum result);
        Assert.That(success, Is.True);
        Assert.That(result, Is.EqualTo(FlagsEnum.Flag1));
    }
}