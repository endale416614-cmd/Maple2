using System.Numerics;
using Maple2.Tools.VectorMath;

namespace Maple2.Server.Tests.Vector;

public class TransformTests {
    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    [Test]
    public void LookToTurnsTowardsDirection() {
        var transform = new Transform();

        transform.LookTo(new Vector3(3, 0, 0));

        Assert.That(Vector3.Distance(transform.FrontAxis, new Vector3(1, 0, 0)), Is.LessThan(1e-4f));
        Assert.That(transform.Scale, Is.EqualTo(1).Within(1e-4f));
    }

    [Test]
    public void LookToWithoutHeadingKeepsFacing() {
        var transform = new Transform();
        transform.LookTo(new Vector3(0, 1, 0));
        Vector3 facing = transform.FrontAxis;

        transform.LookTo(Vector3.Zero); // an NPC facing the spot it stands on
        transform.LookTo(new Vector3(0, 0, 5)); // straight up, no ground-plane heading
        transform.LookTo(Vector3.Normalize(Vector3.Zero)); // NaN

        Assert.That(IsFinite(transform.FrontAxis), Is.True);
        Assert.That(Vector3.Distance(transform.FrontAxis, facing), Is.LessThan(1e-4f));
        Assert.That(transform.Scale, Is.EqualTo(1).Within(1e-4f));
    }

    [Test]
    public void LookToHealsNaNTransform() {
        var transform = new Transform();
        transform.FrontAxis = new Vector3(float.NaN, float.NaN, float.NaN);
        transform.UpAxis = new Vector3(float.NaN, float.NaN, float.NaN);

        transform.LookTo(new Vector3(-1, 0, 0));

        Assert.That(IsFinite(transform.FrontAxis), Is.True);
        Assert.That(IsFinite(transform.UpAxis), Is.True);
        Assert.That(Vector3.Distance(transform.FrontAxis, new Vector3(-1, 0, 0)), Is.LessThan(1e-4f));
    }
}
