using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Maple2.Database.Context;
using Maple2.Tools;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// Every OFFLINE_* switch the servers read through OfflineProfile is resolved under Legacy, Adapted and no mode, and compared
/// with the 'Switches by mode' table in docs/ADAPTATIONS.md. The switches come from a scan of the server sources, because the
/// names and defaults are literals inside initializers and method bodies that reflection cannot see.
/// </summary>
public class ModeMatrixTest {
    private static readonly Regex Call = new(@"OfflineProfile\.(Flag|Option)\(\s*""(OFFLINE_[A-Z0-9_]+)""\s*,\s*(true|false)\s*\)");
    private static readonly Regex AnyCall = new(@"OfflineProfile\.(Flag|Option)\(");
    private static readonly Regex Literal = new(@"""(OFFLINE_[A-Z0-9_]+)""");
    private static readonly Regex Row = new(@"^\|\s*`(OFFLINE_[A-Z0-9_]+)`\s*\|[^|\n]*\|\s*(on|off)\s*\|\s*(on|off)\s*\|", RegexOptions.Multiline);

    [Test]
    public void EverySwitchMatchesItsRow() {
        List<string> mismatches = Mismatches(Switches(Files()), Rows(ReadDoc()));

        Assert.That(mismatches, Is.Empty, string.Join(Environment.NewLine, mismatches));
    }

    [Test]
    public void MissingRowAndWrongValueFail() {
        Dictionary<string, (bool IsFlag, bool Default)> switches = Switches(Files());
        Dictionary<string, (bool Legacy, bool Adapted)> rows = Rows(ReadDoc());

        var withNewSwitch = new Dictionary<string, (bool IsFlag, bool Default)>(switches);
        foreach ((string name, (bool IsFlag, bool Default) value) in Switches([("fake.cs", "OfflineProfile.Flag(\"OFFLINE_NEW_SWITCH\", true)")])) {
            withNewSwitch[name] = value;
        }
        Assert.That(Mismatches(withNewSwitch, rows), Has.Some.StartsWith("OFFLINE_NEW_SWITCH: no row"));

        var flipped = new Dictionary<string, (bool Legacy, bool Adapted)>(rows) {
            ["OFFLINE_CHEATS"] = (true, rows["OFFLINE_CHEATS"].Adapted),
        };
        Assert.That(Mismatches(switches, flipped), Has.Some.StartsWith("OFFLINE_CHEATS under Legacy"));
    }

    [Test]
    public void NoSwitchBypassesTheProfile() {
        List<(string File, string Text)> files = Files();
        HashSet<string> switches = Switches(files).Keys.ToHashSet();

        // TODO: a name built at run time ("OFFLINE_" + text) is not seen; check the reads again if a switch name is ever built that way.
        var bypasses = new List<string>();
        foreach ((string file, string text) in files) {
            foreach (Match match in Literal.Matches(text)) {
                string name = match.Groups[1].Value;
                if (switches.Contains(name) || name.EndsWith("_FILE", StringComparison.Ordinal) ||
                    name is "OFFLINE_MODE" or "OFFLINE_PROFILE" or "OFFLINE_TESTKIT_INBOX") {
                    continue;
                }
                bypasses.Add($"{name} in {file}: not read through OfflineProfile.Flag or Option");
            }
        }

        Assert.That(bypasses, Is.Empty, string.Join(Environment.NewLine, bypasses));
    }

    private static List<(string File, string Text)> Files() {
        return Directory.EnumerateFiles(Paths.SOLUTION_DIR, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Paths.SOLUTION_DIR, path))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj" or "Maple2.Server.Tests"))
            .Select(relative => (relative, File.ReadAllText(Path.Combine(Paths.SOLUTION_DIR, relative))))
            .ToList();
    }

    private static Dictionary<string, (bool IsFlag, bool Default)> Switches(IEnumerable<(string File, string Text)> sources) {
        var switches = new Dictionary<string, (bool IsFlag, bool Default)>();
        foreach ((string file, string text) in sources) {
            MatchCollection calls = Call.Matches(text);
            Assert.That(calls.Count, Is.EqualTo(AnyCall.Matches(text).Count),
                $"{file}: an OfflineProfile.Flag or Option call whose name or default is not a literal");
            foreach (Match call in calls) {
                string name = call.Groups[2].Value;
                (bool IsFlag, bool Default) value = (call.Groups[1].Value == "Flag", call.Groups[3].Value == "true");
                if (switches.TryGetValue(name, out (bool IsFlag, bool Default) previous)) {
                    Assert.That(value, Is.EqualTo(previous), $"{file}: {name} is read with another kind or default elsewhere");
                }
                switches[name] = value;
            }
        }
        return switches;
    }

    private static string ReadDoc() {
        // TODO: the table is read from the superproject checkout, so a Maple2-only checkout fails here; copy the table into Maple2 if Maple2 is ever tested on its own.
        string path = Path.GetFullPath(Path.Combine(Paths.SOLUTION_DIR, "..", "docs", "ADAPTATIONS.md"));
        Assert.That(File.Exists(path), Is.True, "docs/ADAPTATIONS.md not found beside the Maple2 folder");
        return File.ReadAllText(path);
    }

    private static Dictionary<string, (bool Legacy, bool Adapted)> Rows(string doc) {
        var rows = new Dictionary<string, (bool Legacy, bool Adapted)>();
        foreach (Match row in Row.Matches(doc)) {
            string name = row.Groups[1].Value;
            Assert.That(rows.ContainsKey(name), Is.False, $"{name}: two rows in docs/ADAPTATIONS.md");
            rows[name] = (row.Groups[2].Value == "on", row.Groups[3].Value == "on");
        }
        return rows;
    }

    private static List<string> Mismatches(Dictionary<string, (bool IsFlag, bool Default)> switches, Dictionary<string, (bool Legacy, bool Adapted)> rows) {
        var messages = new List<string>();
        foreach ((string name, (bool isFlag, bool defaultValue)) in switches.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            if (!rows.TryGetValue(name, out (bool Legacy, bool Adapted) row)) {
                messages.Add($"{name}: no row in docs/ADAPTATIONS.md 'Switches by mode'");
                continue;
            }

            // An Option is the same in every mode; a Flag is off under Legacy and its default under Adapted and when no mode is set.
            foreach ((string mode, string? value, bool expected) in new[] { ("Legacy", "Legacy", row.Legacy), ("Adapted", "Adapted", row.Adapted), ("no mode", null, row.Adapted) }) {
                bool actual = isFlag ? OfflineProfile.Resolve(value, null, defaultValue) : OfflineProfile.ResolveOption(null, defaultValue);
                if (actual != expected) {
                    messages.Add($"{name} under {mode}: the servers give {(actual ? "on" : "off")}, the table says {(expected ? "on" : "off")}");
                }
            }
        }
        foreach (string name in rows.Keys.Where(name => !switches.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal)) {
            messages.Add($"{name}: row without a switch in the servers");
        }
        return messages;
    }
}
