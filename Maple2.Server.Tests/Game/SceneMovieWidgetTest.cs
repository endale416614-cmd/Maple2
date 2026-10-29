using Maple2.Server.Game.Model.Widget;

namespace Maple2.Server.Tests.Game;

/// <summary>A scene movie's IsStop holds only a value the client reported (TR-02, BT-129).</summary>
public class SceneMovieWidgetTest {
    [Test]
    public void IsStop_IsOnlyAReportedValue() {
        var widget = new SceneMovieWidget(null!);

        // Reactor Site's 52100105_03 clears the widget and plays a movie, then tests IsStop 0 (for a state the script lacks) before its 3 s wait.
        widget.Action("Clear", 0, "");
        Assert.That(widget.Check("IsStop", "0"), Is.False, "an unset IsStop reads as 0");

        widget.Conditions["IsStop"] = 1; // TriggerHandler stores what the client reports
        Assert.That(widget.Check("IsStop", "1"), Is.True);
        Assert.That(widget.Check("IsStop", "0"), Is.False);

        widget.Conditions["IsStop"] = 0;
        Assert.That(widget.Check("IsStop", "0"), Is.True, "a reported 0 is a value");
    }
}
