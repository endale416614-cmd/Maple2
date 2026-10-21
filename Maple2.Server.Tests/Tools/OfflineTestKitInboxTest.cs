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
}
