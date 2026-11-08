using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Maple2.Tools;
using NUnit.Framework;

namespace Maple2.Server.Tests.Offline;

/// <summary>UG-01: uploaded files live in the save and come over from the program folder once (BT-15).
/// Golden: a portrait path of an rc-1.2.2 install, the shop banner the release ships, the other paths as WebController writes them (its guild id is an int).</summary>
[TestFixture]
public class UgcFolderTest {
    private const string Portrait = "profiles/20000000000/f1f21ca3-d786-472f-b2ef-e84bb4daea29.png";
    private const string ShopBanner = "system/banner/homeproduct_1.png";
    private static readonly string[] Others = [
        "items/11600001/5.m2u",
        "itemicon/11600001/5.png",
        "banner/1010/6.m2u",
        "guildmark/1280523264/7.png",
        "guildmark/1280523264/banner/8.png",
        "blueprint/9/9_icon.png",
        "blueprint/9/9.png",
    ];

    private string root = null!;
    private string oldDir = null!;
    private string ugcDir = null!;

    [SetUp]
    public void SetUp() {
        root = Path.Combine(Path.GetTempPath(), "UgcFolderTest_" + Guid.NewGuid().ToString("N"));
        oldDir = Path.Combine(root, "Data");
        ugcDir = Paths.UgcDirFor(Path.Combine(root, "SaveData"));
        foreach (string file in Others.Append(Portrait).Append(ShopBanner)) {
            Write(oldDir, file, file);
        }
    }

    [TearDown]
    public void TearDown() {
        Directory.Delete(root, true);
    }

    private static void Write(string folder, string relative, string text) {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Read(string folder, string relative) => File.ReadAllText(Path.Combine(folder, relative));

    private string Marker => Path.Combine(ugcDir, UgcFolder.MARKER);

    [Test]
    public void TheFolderIsInTheSaveAndOnlyWithoutOneInTheProgramFolder() {
        Assert.Multiple(() => {
            Assert.That(Paths.UgcDirFor(Path.Combine("home", "SaveData")), Is.EqualTo(Path.Combine("home", "SaveData", "ugc")));
            Assert.That(Paths.UgcDirFor(Path.Combine("home", "SaveData", "Second")), Is.EqualTo(Path.Combine("home", "SaveData", "Second", "ugc")));
            Assert.That(Paths.UgcDirFor(null), Is.EqualTo(Paths.WEB_DATA_DIR));
            Assert.That(Paths.UgcDirFor(""), Is.EqualTo(Paths.WEB_DATA_DIR));
        });
    }

    [Test]
    public void TheFolderFollowsTheSaveTheLauncherPassesAtEachUse() {
        string? before = Environment.GetEnvironmentVariable("SQLITE_DIR");
        try {
            Environment.SetEnvironmentVariable("SQLITE_DIR", Path.Combine(root, "SaveData"));
            Assert.That(Paths.UGC_DIR, Is.EqualTo(ugcDir));
            Environment.SetEnvironmentVariable("SQLITE_DIR", null);
            Assert.That(Paths.UGC_DIR, Is.EqualTo(Paths.WEB_DATA_DIR));
        } finally {
            Environment.SetEnvironmentVariable("SQLITE_DIR", before);
        }
    }

    [Test]
    public void EveryUploadedFileComesOverTheOldOnesStayAndTheShopBannerDoesNotMove() {
        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(failed, Is.Empty);
            Assert.That(copied, Is.EqualTo(Others.Length + 1));
            foreach (string file in Others.Append(Portrait)) {
                Assert.That(Read(ugcDir, file), Is.EqualTo(file));
                Assert.That(Read(oldDir, file), Is.EqualTo(file));
            }
            Assert.That(File.Exists(Path.Combine(ugcDir, ShopBanner)), Is.False);
            Assert.That(Read(oldDir, ShopBanner), Is.EqualTo(ShopBanner));
            Assert.That(File.Exists(Marker), Is.True);
        });
    }

    [Test]
    public void TheCopyIsOnce() {
        UgcFolder.CopyOnce(oldDir, ugcDir);
        // A new portrait deletes the character's folder (WebController.UploadProfileAvatar): the old one must not come back.
        Directory.Delete(Path.Combine(ugcDir, "profiles"), true);
        Write(oldDir, "banner/1010/7.m2u", "written by an older server after the copy");

        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(copied, Is.Zero);
            Assert.That(failed, Is.Empty);
            Assert.That(Directory.Exists(Path.Combine(ugcDir, "profiles")), Is.False);
            Assert.That(File.Exists(Path.Combine(ugcDir, "banner/1010/7.m2u")), Is.False);
        });
    }

    [Test]
    public void AFileInTheSaveIsNeverOverwritten() {
        Write(ugcDir, Portrait, "newer");
        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(failed, Is.Empty);
            Assert.That(copied, Is.EqualTo(Others.Length));
            Assert.That(Read(ugcDir, Portrait), Is.EqualTo("newer"));
            Assert.That(Read(oldDir, Portrait), Is.EqualTo(Portrait));
        });
    }

    [Test]
    public void TheNextStartFinishesAfterACrash() {
        // What a crash leaves: one file whole, one cut off under its .part name, no marker.
        Write(ugcDir, Others[0], Others[0]);
        Write(ugcDir, Portrait + ".part", "cut");

        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(failed, Is.Empty);
            Assert.That(copied, Is.EqualTo(Others.Length));
            Assert.That(Read(ugcDir, Portrait), Is.EqualTo(Portrait));
            Assert.That(File.Exists(Path.Combine(ugcDir, Portrait + ".part")), Is.False);
            Assert.That(File.Exists(Marker), Is.True);
        });
    }

    [Test]
    public void AFailedFileIsReportedKeptAndTriedAgain() {
        string inTheWay = Path.Combine(ugcDir, "profiles");
        Directory.CreateDirectory(ugcDir);
        File.WriteAllText(inTheWay, "a file where the folder must go");

        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(failed, Has.Count.EqualTo(1));
            Assert.That(failed[0], Does.Contain("f1f21ca3-d786-472f-b2ef-e84bb4daea29.png"));
            Assert.That(copied, Is.EqualTo(Others.Length));
            Assert.That(File.Exists(Marker), Is.False);
            Assert.That(Read(oldDir, Portrait), Is.EqualTo(Portrait));
        });

        File.Delete(inTheWay);
        (copied, failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(failed, Is.Empty);
            Assert.That(copied, Is.EqualTo(1));
            Assert.That(Read(ugcDir, Portrait), Is.EqualTo(Portrait));
            Assert.That(File.Exists(Marker), Is.True);
        });
    }

    [Test]
    public void EachSaveGetsItsOwnCopy() {
        string second = Paths.UgcDirFor(Path.Combine(root, "SaveData", "Second"));
        UgcFolder.CopyOnce(oldDir, ugcDir);
        UgcFolder.CopyOnce(oldDir, second);
        Directory.Delete(Path.Combine(second, "profiles"), true);
        Assert.Multiple(() => {
            Assert.That(Read(ugcDir, Portrait), Is.EqualTo(Portrait));
            Assert.That(File.Exists(Path.Combine(second, Portrait)), Is.False);
            Assert.That(Read(second, Others[0]), Is.EqualTo(Others[0]));
        });
    }

    [Test]
    public void NoOldFolderIsNoProblem() {
        Directory.Delete(oldDir, true);
        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, ugcDir);
        Assert.Multiple(() => {
            Assert.That(copied, Is.Zero);
            Assert.That(failed, Is.Empty);
            Assert.That(File.Exists(Marker), Is.True);
        });
    }

    [Test]
    public void WithoutASaveFolderNothingMoves() {
        (int copied, List<string> failed) = UgcFolder.CopyOnce(oldDir, oldDir);
        Assert.Multiple(() => {
            Assert.That(copied, Is.Zero);
            Assert.That(failed, Is.Empty);
            Assert.That(File.Exists(Path.Combine(oldDir, UgcFolder.MARKER)), Is.False);
        });
    }

    [Test]
    public void OnlyTheShopBannerIsReadFromTheProgramFolder() {
        string controllers = Path.Combine(Paths.SOLUTION_DIR, "Maple2.Server.Web", "Controllers");
        string[] files = Directory.GetFiles(controllers, "*.cs", SearchOption.AllDirectories);
        Assert.Multiple(() => {
            Assert.That(files.Where(file => File.ReadAllText(file).Contains(nameof(Paths.WEB_DATA_DIR))).Select(Path.GetFileName), Is.EqualTo(new[] { "SystemController.cs" }));
            Assert.That(files.Count(file => File.ReadAllText(file).Contains(nameof(Paths.UGC_DIR))), Is.EqualTo(7));
        });
    }
}
