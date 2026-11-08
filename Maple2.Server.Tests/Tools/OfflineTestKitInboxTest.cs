using System;
using System.Collections.Generic;
using System.IO;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Tools;

// The dungeon harness drops chat command files and the server runs them for the player in the world (V-03g).
[TestFixture]
public class OfflineTestKitInboxTest {
    private string folder = null!;

    [SetUp]
    public void SetUp() {
        folder = Path.Combine(Path.GetTempPath(), "OfflineTestKitInboxTest-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
    }

    [TearDown]
    public void TearDown() {
        if (Directory.Exists(folder)) {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void RunsTheLinesInFileOrder_AndRemovesTheFiles() {
        File.WriteAllText(Path.Combine(folder, "2.cmd"), "/killall\n");
        File.WriteAllText(Path.Combine(folder, "1.cmd"), "/godmode\n\n");
        var ran = new List<string>();

        int count = OfflineTestKitInbox.Process(folder, line => {
            ran.Add(line);
            return true;
        });

        Assert.That(ran, Is.EqualTo(new[] { "/godmode", "/killall" }));
        Assert.That(count, Is.EqualTo(2));
        Assert.That(Directory.GetFiles(folder), Is.Empty);
    }

    [Test]
    public void NoPlayerInTheWorld_KeepsTheFiles() {
        File.WriteAllText(Path.Combine(folder, "1.cmd"), "/godmode");
        File.WriteAllText(Path.Combine(folder, "2.cmd"), "/killall");
        var ran = new List<string>();

        int count = OfflineTestKitInbox.Process(folder, line => {
            ran.Add(line);
            return false;
        });

        Assert.That(count, Is.EqualTo(0));
        Assert.That(ran, Is.EqualTo(new[] { "/godmode" }));
        Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(2));
    }

    [Test]
    public void MissingFolder_RunsNothing() {
        int count = OfflineTestKitInbox.Process(Path.Combine(folder, "missing"), _ => throw new AssertionException("ran a line"));

        Assert.That(count, Is.EqualTo(0));
    }

    // BT-137. A holder that lets others read is what a guard on the read alone misses: the lines run, the delete fails, and the next call runs them again.
    [TestCase(FileAccess.ReadWrite, FileShare.None)]
    [TestCase(FileAccess.Write, FileShare.Read)]
    [TestCase(FileAccess.Read, FileShare.Read)]
    [TestCase(FileAccess.Read, FileShare.ReadWrite)]
    public void HeldByAnotherStream_WaitsForTheNextCall_AndRunsOnce(FileAccess access, FileShare share) {
        string path = Path.Combine(folder, "1.cmd");
        File.WriteAllText(path, "/godmode\n");
        var ran = new List<string>();
        Func<string, bool> run = line => {
            ran.Add(line);
            return true;
        };

        using (new FileStream(path, FileMode.Open, access, share)) {
            Assert.That(OfflineTestKitInbox.Process(folder, run), Is.EqualTo(0));
            Assert.That(ran, Is.Empty);
            Assert.That(File.Exists(path), Is.True);
        }

        Assert.That(OfflineTestKitInbox.Process(folder, run), Is.EqualTo(1));
        Assert.That(ran, Is.EqualTo(new[] { "/godmode" }));
        Assert.That(Directory.GetFiles(folder), Is.Empty);
    }

    [Test]
    public void HeldFile_StopsThere_SoTheNameOrderHolds() {
        File.WriteAllText(Path.Combine(folder, "1.cmd"), "/godmode");
        File.WriteAllText(Path.Combine(folder, "2.cmd"), "/heal");
        File.WriteAllText(Path.Combine(folder, "3.cmd"), "/killall");
        var ran = new List<string>();
        Func<string, bool> run = line => {
            ran.Add(line);
            return true;
        };

        using (new FileStream(Path.Combine(folder, "2.cmd"), FileMode.Open, FileAccess.Read, FileShare.None)) {
            Assert.That(OfflineTestKitInbox.Process(folder, run), Is.EqualTo(1));
            Assert.That(ran, Is.EqualTo(new[] { "/godmode" }));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(2));
        }

        Assert.That(OfflineTestKitInbox.Process(folder, run), Is.EqualTo(2));
        Assert.That(ran, Is.EqualTo(new[] { "/godmode", "/heal", "/killall" }));
        Assert.That(Directory.GetFiles(folder), Is.Empty);
    }

    [Test]
    public void WhileItsLinesRun_NoOneElseCanOpenTheFile() {
        string path = Path.Combine(folder, "1.cmd");
        File.WriteAllText(path, "/godmode\n");

        int count = OfflineTestKitInbox.Process(folder, _ => {
            Assert.Throws<IOException>(() => File.ReadAllText(path));
            return true;
        });

        Assert.That(count, Is.EqualTo(1));
        Assert.That(File.Exists(path), Is.False);
    }
}
