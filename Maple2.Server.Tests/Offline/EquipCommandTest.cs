using System.CommandLine;
using System.CommandLine.IO;
using Maple2.Server.Game.Commands;

namespace Maple2.Server.Tests.Offline;

/// <summary>/equip and /unequip: argument parsing and the cheats gate (no session needed before the gate).</summary>
public class EquipCommandTest {
    [Test]
    public void EquipRefusesWithCheatsOff() {
        var console = new TestConsole();
        int code = new EquipCommand(null!, cheatsEnabled: false).Invoke(["equip", "11020309", "--rarity", "3"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("Cheats are off"));
    }

    [Test]
    public void EquipRejectsNonNumericId() {
        var console = new TestConsole();
        int code = new EquipCommand(null!, cheatsEnabled: false).Invoke(["equip", "hat"], console);
        Assert.That(code, Is.Not.EqualTo(0));
        Assert.That(console.Error.ToString(), Does.Contain("hat"));
        Assert.That(console.Error.ToString(), Does.Not.Contain("Cheats are off"), "handler must not run on a parse error");
    }

    [Test]
    public void UnequipRefusesWithCheatsOff() {
        var console = new TestConsole();
        int code = new UnequipCommand(null!, cheatsEnabled: false).Invoke(["unequip", "CP"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("Cheats are off"));
    }
}

/// <summary>/equipsweep: argument parsing and the cheats gate.</summary>
public class EquipSweepCommandTest {
    [Test]
    public void SweepRefusesWithCheatsOff() {
        var console = new TestConsole();
        int code = new EquipSweepCommand(null!, cheatsEnabled: false).Invoke(["equipsweep", "ids.txt", "--ms", "500"], console);
        Assert.That(code, Is.EqualTo(1));
        Assert.That(console.Error.ToString(), Does.Contain("Cheats are off"));
    }

    [Test]
    public void SweepRejectsNonNumericInterval() {
        var console = new TestConsole();
        int code = new EquipSweepCommand(null!, cheatsEnabled: false).Invoke(["equipsweep", "ids.txt", "--ms", "fast"], console);
        Assert.That(code, Is.Not.EqualTo(0));
        Assert.That(console.Error.ToString(), Does.Contain("fast"));
        Assert.That(console.Error.ToString(), Does.Not.Contain("Cheats are off"), "handler must not run on a parse error");
    }
}
