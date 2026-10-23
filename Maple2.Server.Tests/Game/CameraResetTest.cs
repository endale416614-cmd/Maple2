using System.Collections.Generic;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Trigger;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class CameraResetTest {
    [Test]
    public void ReleaseCameras_ReturnsCorrectCameras() {
        // Arrange
        var camera30 = new TriggerObjectCamera(new Ms2TriggerCamera(30, true)) { Visible = true };
        var camera31 = new TriggerObjectCamera(new Ms2TriggerCamera(31, true)) { Visible = true };
        var camera40 = new TriggerObjectCamera(new Ms2TriggerCamera(40, true)) { Visible = false };
        var cameras = new List<TriggerObjectCamera> { camera30, camera31, camera40 };

        // Act
        var released = TriggerContext.ReleaseCameras(cameras);

        // Assert
        Assert.That(released.Count, Is.EqualTo(2));
        Assert.That(released, Contains.Item(camera30));
        Assert.That(released, Contains.Item(camera31));
        Assert.That(released, Does.Not.Contain(camera40));

        // Verify that the released cameras are no longer visible
        Assert.That(camera30.Visible, Is.False);
        Assert.That(camera31.Visible, Is.False);
        Assert.That(camera40.Visible, Is.False);

        // A second reset finds nothing left to switch off
        Assert.That(TriggerContext.ReleaseCameras(cameras), Is.Empty);
    }

    [Test]
    public void ReleaseCameras_NoCamerasVisible() {
        // Arrange
        var camera30 = new TriggerObjectCamera(new Ms2TriggerCamera(30, true)) { Visible = false };
        var camera31 = new TriggerObjectCamera(new Ms2TriggerCamera(31, true)) { Visible = false };
        var cameras = new List<TriggerObjectCamera> { camera30, camera31 };

        // Act
        var released = TriggerContext.ReleaseCameras(cameras);

        // Assert
        Assert.That(released.Count, Is.EqualTo(0));
    }

    [Test]
    public void ReleaseCameras_AllCamerasVisible() {
        // Arrange
        var camera30 = new TriggerObjectCamera(new Ms2TriggerCamera(30, true)) { Visible = true };
        var camera31 = new TriggerObjectCamera(new Ms2TriggerCamera(31, true)) { Visible = true };
        var cameras = new List<TriggerObjectCamera> { camera30, camera31 };

        // Act
        var released = TriggerContext.ReleaseCameras(cameras);

        // Assert
        Assert.That(released.Count, Is.EqualTo(2));
        Assert.That(released, Contains.Item(camera30));
        Assert.That(released, Contains.Item(camera31));

        // Verify that the released cameras are no longer visible
        Assert.That(camera30.Visible, Is.False);
        Assert.That(camera31.Visible, Is.False);
    }
}
