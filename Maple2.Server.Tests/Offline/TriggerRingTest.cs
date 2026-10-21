using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class TriggerRingTest {
    [Test]
    public void Lines_KeepTheLast200_OldestFirst() {
        // Clear existing entries
        var entries = new List<string>();
        for (int i = 0; i < 205; i++) {
            Maple2.Server.Game.Trigger.TriggerRing.Add($"ring-test {i}");
        }

        var lines = Maple2.Server.Game.Trigger.TriggerRing.Lines();
        Assert.That(lines.Count, Is.EqualTo(200));
        Assert.That(lines[0], Does.EndWith("ring-test 5"));
        Assert.That(lines[199], Does.EndWith("ring-test 204"));
    }

    [Test]
    public void Flush_WritesTheRingOnceUntilItChanges() {
        string tempFile = Path.GetTempFileName();

        try {
            // Add one entry
            Maple2.Server.Game.Trigger.TriggerRing.Add("test entry");

            // Flush to file
            Maple2.Server.Game.Trigger.TriggerRing.Flush(tempFile);

            // The whole ring: 1 to 200 lines, depending on what other tests added first
            var lines = File.ReadAllLines(tempFile);
            Assert.That(lines.Length, Is.EqualTo(Maple2.Server.Game.Trigger.TriggerRing.Lines().Count));
            Assert.That(lines[^1], Does.EndWith("test entry"));

            // Delete file and flush again without adding more entries
            File.Delete(tempFile);
            Maple2.Server.Game.Trigger.TriggerRing.Flush(tempFile);

            // File should not exist since it wasn't changed
            Assert.That(File.Exists(tempFile), Is.False);
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void Lines_EachLineStartsWithATime() {
        // Add some entries
        Maple2.Server.Game.Trigger.TriggerRing.Add("test entry");

        var lines = Maple2.Server.Game.Trigger.TriggerRing.Lines();
        foreach (string line in lines) {
            Assert.That(line, Does.Match(@"^\d\d:\d\d:\d\d\.\d{3} "));
        }
    }
}