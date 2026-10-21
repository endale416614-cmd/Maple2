using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Offline;

/// <summary>/godmode --protect (PT-01): no damage taken, but no one-hit kill, so a play test measures real damage.</summary>
public class GodModeProtectTest {
    [Test]
    public void Protect_KeepsGodModeWithoutTheOneHitKill() {
        const long character = 990000001;
        Assume.That(OfflineCheats.Enabled, "cheats are off in this environment");

        Assert.That(OfflineCheats.ToggleGodMode(character, protectOnly: true), Is.True);
        Assert.That(OfflineCheats.IsGodMode(character), Is.True, "protected from damage");
        Assert.That(OfflineCheats.IsGodModeKill(character), Is.False, "hits keep their damage");

        Assert.That(OfflineCheats.ToggleGodMode(character, protectOnly: false), Is.False, "second toggle turns it off");
        Assert.That(OfflineCheats.IsGodMode(character), Is.False);

        Assert.That(OfflineCheats.ToggleGodMode(character, protectOnly: false), Is.True);
        Assert.That(OfflineCheats.IsGodModeKill(character), Is.True, "plain /godmode still kills in one hit");
        OfflineCheats.ToggleGodMode(character);
        Assert.That(OfflineCheats.IsGodMode(character), Is.False);
    }
}
