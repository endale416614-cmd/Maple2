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

    private static List<string> LinesOf(string trigger) {
        return Maple2.Server.Game.Trigger.TriggerRing.Lines().FindAll(line => line.Contains($" {trigger} "));
    }

    private static string Folded(string trigger, string state, int n) {
        return $@"^\d\d:\d\d:\d\d\.\d{{3}} {trigger} {state} \(x{n}, last \d\d:\d\d:\d\d\.\d{{3}}\)$";
    }

    [Test]
    public void Add_FoldsATwoStateLoopIntoTwoCountedLines() {
        for (int i = 0; i < 50; i++) {
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-loop losteve", "check", i * 200);
            if (i == 10) {
                Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-other main", "idle", 2050);
            }
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-loop losteve", "base", i * 200 + 100);
        }

        var loop = LinesOf("tt01-loop losteve");
        Assert.That(loop.Count, Is.EqualTo(2));
        Assert.That(loop[0], Does.Match(Folded("tt01-loop losteve", "check", 50)));
        Assert.That(loop[1], Does.Match(Folded("tt01-loop losteve", "base", 50)));

        var other = LinesOf("tt01-other main");
        Assert.That(other.Count, Is.EqualTo(1));
        Assert.That(other[0], Does.EndWith("tt01-other main idle"));
    }

    [Test]
    public void Add_FoldsASelfLoopAndMarksTheRingChanged() {
        string tempFile = Path.GetTempFileName();

        try {
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-self trigger_02", "ready", 0);
            Maple2.Server.Game.Trigger.TriggerRing.Flush(tempFile);
            File.Delete(tempFile);

            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-self trigger_02", "ready", 100);
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-self trigger_02", "ready", 200);
            Maple2.Server.Game.Trigger.TriggerRing.Flush(tempFile);

            Assert.That(File.Exists(tempFile), Is.True);
            string[] own = Array.FindAll(File.ReadAllLines(tempFile), line => line.Contains(" tt01-self trigger_02 "));
            Assert.That(own.Length, Is.EqualTo(1));
            Assert.That(own[0], Does.Match(Folded("tt01-self trigger_02", "ready", 3)));
        } finally {
            if (File.Exists(tempFile)) {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void Add_FoldsALongerCycleIntoItsLines() {
        for (int i = 0; i < 20; i++) {
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-cycle main", "Ready", i * 300);
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-cycle main", "Battle", i * 300 + 100);
            Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-cycle main", "Check", i * 300 + 200);
        }

        var own = LinesOf("tt01-cycle main");
        Assert.That(own.Count, Is.EqualTo(3));
        Assert.That(own[0], Does.Match(Folded("tt01-cycle main", "Ready", 20)));
        Assert.That(own[1], Does.Match(Folded("tt01-cycle main", "Battle", 20)));
        Assert.That(own[2], Does.Match(Folded("tt01-cycle main", "Check", 20)));
    }

    [Test]
    public void Add_StartsANewLineAfterFiveQuietSeconds() {
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Ready", 0);
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Battle", 100);
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Ready", 4000);
        // Counted: the window runs from the line's last entry (4000), not its first.
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Ready", 8000);
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Clear", 8100);
        // 5101 ms after the line's last entry: a new line.
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-quiet main", "Ready", 13101);

        var own = LinesOf("tt01-quiet main");
        Assert.That(own.Count, Is.EqualTo(4));
        Assert.That(own[0], Does.Match(Folded("tt01-quiet main", "Ready", 3)));
        Assert.That(own[1], Does.EndWith("tt01-quiet main Battle"));
        Assert.That(own[2], Does.EndWith("tt01-quiet main Clear"));
        Assert.That(own[3], Does.EndWith("tt01-quiet main Ready"));
    }

    [Test]
    public void Add_StartsANewLineWhenTheTriggersLinesLeftTheRing() {
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-gone main", "A", 0);
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-gone main", "B", 100);
        for (int i = 0; i < 200; i++) {
            Maple2.Server.Game.Trigger.TriggerRing.Add($"tt01-filler {i}");
        }
        Maple2.Server.Game.Trigger.TriggerRing.Add("tt01-gone main", "A", 200);

        var own = LinesOf("tt01-gone main");
        Assert.That(own.Count, Is.EqualTo(1));
        Assert.That(own[0], Does.EndWith("tt01-gone main A"));
        Assert.That(Maple2.Server.Game.Trigger.TriggerRing.Lines().Count, Is.EqualTo(200));
    }
}