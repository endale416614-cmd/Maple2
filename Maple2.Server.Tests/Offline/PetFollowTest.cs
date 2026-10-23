using NUnit.Framework;
using Maple2.Server.Game.Manager;

namespace Maple2.Server.Tests.Offline;

[TestFixture]
public class PetFollowTest {
    [Test]
    public void Follow() {
        // Test the basic functionality of pet following
        PetManager.Follow(990001, 4242);

        // Try to take a pet that was followed when pets are not allowed - should fail
        Assert.IsFalse(PetManager.TryTakeFollowing(990001, false, out _));

        // Try to take a pet that was followed when pets are allowed - should succeed
        Assert.IsTrue(PetManager.TryTakeFollowing(990001, true, out long uid));
        Assert.AreEqual(4242, uid);

        // Try to take the same pet again - should fail (already taken)
        Assert.IsFalse(PetManager.TryTakeFollowing(990001, true, out _));

        // Test unfollow functionality
        PetManager.Follow(990002, 4243);
        PetManager.Unfollow(990002);
        Assert.IsFalse(PetManager.TryTakeFollowing(990002, true, out _));
    }
}
