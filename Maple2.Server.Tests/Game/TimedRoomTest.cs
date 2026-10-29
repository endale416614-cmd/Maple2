using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Tools;
using Maple2.Tools.Extensions;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

[TestFixture]
public class TimedRoomTest {
    [Test]
    public void TicksSince() {
        long now = (1L << 31) + 1;
        int end = (now + 30000).Truncate32();
        Assert.That((now + 1000).TicksSince(end), Is.LessThan(0));
        Assert.That((now + 31000).TicksSince(end), Is.GreaterThan(0));

        // Test wrap around
        now = (1L << 32) - 5000;
        end = (now + 30000).Truncate32();
        Assert.That((now + 1000).TicksSince(end), Is.LessThan(0));
        Assert.That((now + 31000).TicksSince(end), Is.GreaterThan(0));

        // Test normal case
        Assert.That((1000L).TicksSince(0), Is.EqualTo(1000));
    }

    [Test]
    public void ReturnMapId() {
        // Stack built as new LimitedStack<int>(3, 2000062) then Push(80000009) gives 2000062 for room 80000009 and leaves Count == 1
        var returnMaps = new LimitedStack<int>(3, 2000062);
        returnMaps.Push(80000009);
        Assert.That(RoomTimer.ReturnMapId(returnMaps, 80000009), Is.EqualTo(2000062));
        Assert.That(returnMaps.Count, Is.EqualTo(1));

        // new LimitedStack<int>(3, 2000062) alone gives 2000062
        returnMaps = new LimitedStack<int>(3, 2000062);
        Assert.That(RoomTimer.ReturnMapId(returnMaps, 80000009), Is.EqualTo(2000062));

        // new LimitedStack<int>(3, 80000009) gives 80000009 (nothing below it)
        returnMaps = new LimitedStack<int>(3, 80000009);
        Assert.That(RoomTimer.ReturnMapId(returnMaps, 80000009), Is.EqualTo(80000009));

        // Stack [2000062, 2000001] with room 80000009 gives 2000001 and keeps Count == 2
        returnMaps = new LimitedStack<int>(3, 2000062);
        returnMaps.Push(2000001);
        Assert.That(RoomTimer.ReturnMapId(returnMaps, 80000009), Is.EqualTo(2000001));
        Assert.That(returnMaps.Count, Is.EqualTo(2));
    }

    [Test]
    public void LeaveMapId() {
        // Stack [2000062, 2000001] with solo instance 2000313 gives 2000001 and keeps Count == 2
        var returnMaps = new LimitedStack<int>(3, 2000062);
        returnMaps.Push(2000001);
        Assert.That(RoomTimer.LeaveMapId(returnMaps, 2000313), Is.EqualTo(2000001));
        Assert.That(returnMaps.Count, Is.EqualTo(2));

        // Telescope room 52100021 pushed itself above 2000062 and gives 2000062
        returnMaps = new LimitedStack<int>(3, 2000062);
        returnMaps.Push(52100021);
        Assert.That(RoomTimer.LeaveMapId(returnMaps, 52100021), Is.EqualTo(2000062));

        // A lone room entry gives Lith Harbor, not the room itself
        returnMaps = new LimitedStack<int>(3, 52100021);
        Assert.That(RoomTimer.LeaveMapId(returnMaps, 52100021), Is.EqualTo(Constant.DefaultReturnMapId));
    }

    [Test]
    public void LoginMapId() {
        // Non-pocket realm should return the saved map ID
        Assert.That(RoomTimer.LoginMapId(2000001, false, new LimitedStack<int>(3, 2000062)), Is.EqualTo(2000001));

        // Pocket realm with a map below it should return that map
        var stack = new LimitedStack<int>(3, 2000062);
        stack.Push(80000007);
        Assert.That(RoomTimer.LoginMapId(80000007, true, stack), Is.EqualTo(2000062));

        // Pocket realm with a different return map should return that map
        stack = new LimitedStack<int>(3, 2000065);
        Assert.That(RoomTimer.LoginMapId(80000020, true, stack), Is.EqualTo(2000065));

        // Pocket realm with no map below should return default (Lith Harbor)
        stack = new LimitedStack<int>(3, 80000007);
        Assert.That(RoomTimer.LoginMapId(80000007, true, stack), Is.EqualTo(Constant.DefaultReturnMapId));
    }
}
