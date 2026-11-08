using System;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;

namespace Maple2.Server.Tests.Offline;

/// <summary>
/// BT-142: the client reads a 30-byte nurturing block for every Nurturing cube, so a Nurturing cube with no saved data
/// (a Rainbow Slime's Nest) still writes one, and the packets that list a home's cubes stay aligned.
/// </summary>
public class InteractCubeWriteTest {
    private static readonly Vector3B Position = new(-20, -20, 2);

    private static FunctionCubeMetadata Cube(InteractCubeControlType c, FunctionCubeMetadata.NurturingData? n = null) =>
        new(50800001, 0, ConfigurableCubeType.None, InteractCubeState.InUse, c, [], 0, n);

    private static byte[] Write(InteractCube cube) {
        var writer = new ByteWriter();
        cube.WriteTo(writer);
        return writer.Buffer[..writer.Length];
    }

    [Test]
    public void NurturingCubeWithoutData_WritesTheBlock() {
        var cube = new InteractCube(Position, Cube(InteractCubeControlType.Nurturing));
        byte[] bytes = Write(cube);

        Assert.Multiple(() => {
            Assert.That(cube.Id, Is.EqualTo("4_191724"));
            Assert.That(bytes, Has.Length.EqualTo(52));
            Assert.That(BitConverter.ToInt64(bytes, 30), Is.EqualTo(0), "Exp");
            Assert.That(BitConverter.ToInt16(bytes, 38), Is.EqualTo(1), "Stage is never 0");
            Assert.That(BitConverter.ToInt16(bytes, 40), Is.EqualTo(1), "ClaimedGiftForStage");
            Assert.That(BitConverter.ToInt16(bytes, 42), Is.EqualTo(0));
        });
    }

    [Test]
    public void FarmingCube_WritesNoBlock() {
        var cube = new InteractCube(Position, Cube(InteractCubeControlType.Farming));

        Assert.That(Write(cube), Has.Length.EqualTo(22));
    }

    [Test]
    public void NurturingCubeWithData_WritesOneBlock() {
        var data = new FunctionCubeMetadata.NurturingData(new RewardItem(1, 1, 1), new RewardItem(1, 1, 1),
            [new(300, 1, new RewardItem(1, 1, 1))], "");
        var cube = new InteractCube(Position, Cube(InteractCubeControlType.Nurturing, data));

        Assert.That(Write(cube), Has.Length.EqualTo(52));
    }
}
