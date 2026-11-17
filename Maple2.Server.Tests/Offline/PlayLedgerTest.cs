using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game.Dungeon;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;
using NUnit.Framework;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Maple2.Server.Tests.Offline;

/// <summary>TM-01 (BT-237, BT-240): the play ledger is its own file, one line per event, ids and numbers only, rolled at a cap,
/// and nothing of it reaches the server log.</summary>
[TestFixture]
[NonParallelizable]
public class PlayLedgerTest {
    private sealed class Lines : ILogEventSink {
        public readonly List<string> Items = [];
        public void Emit(LogEvent logEvent) => Items.Add(logEvent.RenderMessage());
    }

    private string folder = "";
    private string? savedPath;
    private long savedMax, savedSnapshot;

    [SetUp]
    public void SetUp() {
        savedPath = PlayLedger.FilePath;
        savedMax = PlayLedger.MaxBytes;
        savedSnapshot = PlayLedger.SnapshotMs;
        PlayLedger.SnapshotMs = long.MaxValue;
        folder = Path.Combine(Path.GetTempPath(), "ledger-" + Guid.NewGuid().ToString("N"));
        PlayLedger.FilePath = Path.Combine(folder, PlayLedger.FileName);
    }

    [TearDown]
    public void TearDown() {
        PlayLedger.FilePath = savedPath;
        PlayLedger.MaxBytes = savedMax;
        PlayLedger.SnapshotMs = savedSnapshot;
        if (Directory.Exists(folder)) {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static T Bare<T>() where T : class {
        var instance = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
        GC.SuppressFinalize(instance);
        return instance;
    }

    private static void Set(object target, string name, object? value) {
        for (Type? type = target.GetType(); type != null; type = type.BaseType) {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private string[] Ledger() => File.Exists(PlayLedger.FilePath) ? File.ReadAllLines(PlayLedger.FilePath!) : [];

    // The line without its UTC stamp.
    private static string Body(string line) {
        Assert.That(line, Does.Match(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ "));
        return line[21..];
    }

    private static DungeonRoomRecord Run(int maxRevivals, int durationTick) {
        var limit = Bare<DungeonRoomLimitMetadata>() with { MaxRevivalCount = maxRevivals };
        var record = new DungeonRoomRecord(Bare<DungeonRoomMetadata>() with { Limit = limit, DurationTick = durationTick });
        record.Start(1000);
        return record;
    }

    [Test]
    public void ADeathIsOneLineOfIds() {
        PlayLedger.Death(21000001, 10000042, 12345, 80000);
        Assert.That(Ledger(), Has.Length.EqualTo(1));
        Assert.That(Body(Ledger()[0]), Is.EqualTo("death npc=21000001 skill=10000042 hit=12345 maxhp=80000 pct=15.4"));
    }

    [Test]
    public void ADungeonEndHoldsTimeDeathsRevivalsAndTheRaisedShare() {
        DungeonRoomRecord run = Run(maxRevivals: 5, durationTick: 900000);
        run.Revivals[7] = 2;
        object npc = new();
        PlayLedger.PlayerHit(npc, false, run, 100, 150);
        PlayLedger.PlayerHit(npc, false, run, 100, 100);
        PlayLedger.CountDeath(run);
        PlayLedger.CountDeath(run);
        run.State = DungeonState.Clear;

        PlayLedger.DungeonEnd(2000531, run, 1000 + 300_000);
        Assert.That(Body(Ledger()[0]), Is.EqualTo("dungeon room=2000531 result=Clear seconds=300 clockleft=600 deaths=2 revivalsleft=3 raised=20.0"));

        // No room timer, no revival limit, no hit: -1, -1 and "-"
        DungeonRoomRecord plain = Run(maxRevivals: 0, durationTick: 0);
        plain.State = DungeonState.Fail;
        PlayLedger.DungeonEnd(23000001, plain, 1000 + 61_000);
        Assert.That(Body(Ledger()[1]), Is.EqualTo("dungeon room=23000001 result=Fail seconds=61 clockleft=-1 deaths=0 revivalsleft=-1 raised=-"));
    }

    [Test]
    public void ABossKillHoldsSecondsAndTheRaisedShare() {
        object boss = new();
        PlayLedger.PlayerHit(boss, true, null, 100, 150);
        PlayLedger.BossKill(2000531, 21000009, boss);
        Assert.That(Body(Ledger()[0]), Does.Match(@"^boss map=2000531 npc=21000009 seconds=[0-5] raised=33\.3$"));

        PlayLedger.BossKill(2000531, 21000010, new object());
        Assert.That(Body(Ledger()[1]), Is.EqualTo("boss map=2000531 npc=21000010 seconds=-1 raised=-"), "a boss no player hit");
    }

    [Test]
    public void ALevelUpHoldsItsSourceAndTheSessionsExpBySource() {
        PlayLedger.Exp(100, "tm01-kill");
        PlayLedger.Exp(50, "tm01-quest");
        PlayLedger.Exp(0, "tm01-none");
        Assert.That(Ledger(), Is.Empty, "gains alone write nothing before the 5 minutes");

        PlayLedger.LevelUp(70, 71, "tm01-quest");
        Assert.That(Body(Ledger()[0]), Is.EqualTo("levelup from=70 to=71 source=tm01-quest"));
        Assert.That(Body(Ledger()[1]), Does.Match(@"^exp .*tm01-kill=100 tm01-quest=50( |$)"));
        Assert.That(Ledger()[1], Does.Not.Contain("tm01-none"));
    }

    [Test]
    public void TheHighestRegionCountIsWrittenAtEachHundred() {
        PlayLedger.Regions(77001, 99);
        Assert.That(Ledger(), Is.Empty);
        PlayLedger.Regions(77001, 100);
        PlayLedger.Regions(77001, 150);
        PlayLedger.Regions(77001, 230);
        PlayLedger.Regions(77001, 120);
        PlayLedger.Regions(77002, 101);
        Assert.That(Ledger().Select(Body), Is.EqualTo(new[] { "regions map=77001 max=100", "regions map=77001 max=230", "regions map=77002 max=101" }));
    }

    private static void AddRegions(int mapId, int count) {
        var field = Bare<FieldManager>();
        var regions = new ConcurrentDictionary<int, FieldSkill>();
        typeof(FieldManager).GetField("fieldSkills", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, regions);
        typeof(FieldManager).GetField("<MapId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(field, mapId);
        var caster = Bare<FieldNpc>();
        var skill = new SkillMetadata(70000001, 1, null, Bare<SkillMetadataProperty>(), null!, null!);
        for (int id = 1; id <= count; id++) {
            var region = Bare<FieldSkill>();
            Set(region, "<ObjectId>k__BackingField", id);
            Set(region, "<Value>k__BackingField", skill);
            Set(region, "<Caster>k__BackingField", caster);
            typeof(FieldManager).GetMethod("AddRegion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(field, [region]);
        }
    }

    [Test]
    public void AddRegionReportsItsCount() {
        AddRegions(77003, 100);
        Assert.That(Ledger().Select(Body), Is.EqualTo(new[] { "regions map=77003 max=100" }));
    }

    [Test]
    public void AddRegionWithTheLedgerOffWritesNothing() {
        PlayLedger.FilePath = null;
        AddRegions(77006, 150);
        Assert.That(Directory.Exists(folder), Is.False);
    }

    [Test]
    public void ACommonMonsterOutsideARunGetsNoTallyButABossDoes() {
        object common = new();
        object boss = new();
        PlayLedger.PlayerHit(common, false, null, 100, 150);
        PlayLedger.PlayerHit(boss, true, null, 100, 150);
        PlayLedger.BossKill(1, 10, common);
        PlayLedger.BossKill(1, 11, boss);
        Assert.That(Body(Ledger()[0]), Is.EqualTo("boss map=1 npc=10 seconds=-1 raised=-"), "no tally for a common monster outside a run");
        Assert.That(Body(Ledger()[1]), Does.Match(@"^boss map=1 npc=11 seconds=[0-5] raised=33\.3$"));
    }

    [Test]
    public void AFailedRollKeepsAppendingUpToTwiceTheCapAndNeverThrows() {
        PlayLedger.MaxBytes = 400;
        Directory.CreateDirectory(folder);
        string path = PlayLedger.FilePath!;
        string old = Path.Combine(folder, PlayLedger.OldFileName);
        File.WriteAllText(old, "old");
        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.None)) {
            Assert.DoesNotThrow(() => {
                for (int i = 0; i < 10; i++) { // about 660 bytes: past the cap, under twice the cap
                    PlayLedger.Death(i, 2, 3, 4);
                }
            });
            string[] lines = File.ReadAllLines(path);
            Assert.That(lines, Has.Length.EqualTo(10), "every line is in the main file although the roll fails");
            Assert.That(lines[^1], Does.Contain("npc=9 "));
            Assert.DoesNotThrow(() => {
                for (int i = 10; i < 40; i++) {
                    PlayLedger.Death(i, 2, 3, 4);
                }
            });
            Assert.That(new FileInfo(path).Length, Is.LessThan(2 * 400 + 100), "capped at twice the limit");
        }

        PlayLedger.Death(99, 2, 3, 4); // the old file is free again: the roll works
        Assert.That(File.ReadAllLines(path), Has.Length.EqualTo(1));
        Assert.That(new FileInfo(old).Length, Is.GreaterThanOrEqualTo(400));
    }

    [Test]
    public void NothingReachesTheServerLogEvenWhenTheWriteFails() {
        var lines = new Lines();
        ILogger before = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(lines).CreateLogger();
        try {
            DungeonRoomRecord run = Run(3, 900000);
            run.State = DungeonState.Clear;
            void Everything() {
                PlayLedger.Death(1, 2, 3, 4);
                PlayLedger.PlayerHit(run, true, run, 1, 2);
                PlayLedger.CountDeath(run);
                PlayLedger.DungeonEnd(5, run, 2000);
                PlayLedger.BossKill(1, 2, run);
                PlayLedger.LevelUp(1, 2, "tm01-zlog");
                PlayLedger.Exp(5, "tm01-zlog");
                PlayLedger.Regions(77004, 100);
            }

            Everything();
            Assert.That(Ledger(), Has.Length.GreaterThan(5), "the lines went to the file");

            // A folder that cannot be made (a file is in its place): the write is dropped, the game goes on, the log stays empty
            string blocked = Path.Combine(folder, "blocked");
            File.WriteAllText(blocked, "");
            PlayLedger.FilePath = Path.Combine(blocked, PlayLedger.FileName);
            Assert.DoesNotThrow(Everything);
        } finally {
            Log.Logger = before;
        }
        Assert.That(lines.Items, Is.Empty);
    }

    [Test]
    public void WithoutAFolderNothingIsWritten() {
        PlayLedger.FilePath = null;
        PlayLedger.Death(1, 2, 3, 4);
        PlayLedger.Regions(77005, 500);
        Assert.That(Directory.Exists(folder), Is.False);
    }

    [Test]
    public void TheFileIsRolledAtItsCapSoItDoesNotGrowWithoutEnd() {
        PlayLedger.MaxBytes = 400;
        for (int i = 0; i < 100; i++) {
            PlayLedger.Death(i, 2, 3, 4);
        }
        string path = PlayLedger.FilePath!;
        string old = Path.Combine(folder, PlayLedger.OldFileName);
        Assert.That(new FileInfo(path).Length, Is.LessThan(400 + 100), "the file stops at the cap plus one line");
        Assert.That(new FileInfo(old).Length, Is.GreaterThanOrEqualTo(400).And.LessThan(400 + 100));
        Assert.That(File.ReadAllLines(path)[^1], Does.Contain("npc=99 "), "the newest line is in the file");
        Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(2));
    }
}
