using NUnit.Framework;
using Maple2.Database.Context;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class SaveStampTest {
    [Test]
    public void NoStampAdapted() {
        SaveStamp.Result result = SaveStamp.Next(null, null, SaveStamp.Adapted);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Adapted));
            Assert.That(result.History, Is.EqualTo(SaveStamp.Adapted));
            Assert.That(result.Changed, Is.True);
            Assert.That(result.WarnLegacyAfterAdapted, Is.False);
        });
    }

    [Test]
    public void NoStampLegacy() {
        SaveStamp.Result result = SaveStamp.Next(null, null, SaveStamp.Legacy);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.History, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.Changed, Is.True);
            Assert.That(result.WarnLegacyAfterAdapted, Is.False);
        });
    }

    [Test]
    public void StoredAdaptedCurrentLegacy() {
        SaveStamp.Result result = SaveStamp.Next(SaveStamp.Adapted, SaveStamp.Adapted, SaveStamp.Legacy);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.History, Is.EqualTo($"{SaveStamp.Adapted},{SaveStamp.Legacy}"));
            Assert.That(result.Changed, Is.True);
            Assert.That(result.WarnLegacyAfterAdapted, Is.True);
        });
    }

    [Test]
    public void StoredLegacyWithHistoryCurrentLegacy() {
        SaveStamp.Result result = SaveStamp.Next(SaveStamp.Legacy, $"{SaveStamp.Adapted},{SaveStamp.Legacy}", SaveStamp.Legacy);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.History, Is.EqualTo($"{SaveStamp.Adapted},{SaveStamp.Legacy}"));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.WarnLegacyAfterAdapted, Is.True);
        });
    }

    [Test]
    public void StoredLegacyWithHistoryCurrentAdapted() {
        SaveStamp.Result result = SaveStamp.Next(SaveStamp.Legacy, $"{SaveStamp.Adapted},{SaveStamp.Legacy}", SaveStamp.Adapted);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Adapted));
            Assert.That(result.History, Is.EqualTo($"{SaveStamp.Adapted},{SaveStamp.Legacy},{SaveStamp.Adapted}"));
            Assert.That(result.Changed, Is.True);
            Assert.That(result.WarnLegacyAfterAdapted, Is.False);
        });
    }

    [Test]
    public void StoredLegacyWithHistoryCurrentLegacyNoChange() {
        SaveStamp.Result result = SaveStamp.Next(SaveStamp.Legacy, SaveStamp.Legacy, SaveStamp.Legacy);
        Assert.Multiple(() => {
            Assert.That(result.Mode, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.History, Is.EqualTo(SaveStamp.Legacy));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.WarnLegacyAfterAdapted, Is.False);
        });
    }
}