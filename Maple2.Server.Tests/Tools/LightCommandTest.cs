using System.Numerics;
using Maple2.Server.Game.Commands;

namespace Maple2.Server.Tests.Tools;

public class LightCommandTest {
    [Test]
    public void ParseColor_ReadsThreeValuesFrom0To255() {
        Assert.Multiple(() => {
            Assert.That(LightCommand.ParseColor("120, 120, 120"), Is.EqualTo(new Vector3(120, 120, 120)));
            Assert.That(LightCommand.ParseColor("0,0,0"), Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(LightCommand.ParseColor("256,0,0"), Is.Null);
            Assert.That(LightCommand.ParseColor("1,2"), Is.Null);
            Assert.That(LightCommand.ParseColor("a,b,c"), Is.Null);
        });
    }
}
