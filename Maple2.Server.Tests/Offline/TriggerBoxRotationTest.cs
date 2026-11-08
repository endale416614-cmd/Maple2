using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Tools.Collision;

namespace Maple2.Server.Tests.Offline;

/// <summary>TB-01: a trigger box turns by its rotation in the map data (BT-206). Golden: Trial by Ordeal's fight area, box 1003 of
/// 02020027_bf in Exported.m2d, and the 3,986 positions the client sent (UserSync) in that room in its four captures (Mason Phases,
/// Clone Failure 1 and 2, Fortress_Rumble__Quest_Objective_Completio).</summary>
public class TriggerBoxRotationTest {
    private const float Turn = 45.000652f; // box 1003's rotation Z

    private static TriggerBox Box1003(float rotationZ) =>
        new(new Ms2TriggerBox(new Vector3(-150, 75, 2775), new Vector3(3000, 10000, 3000), new Vector3(0, 0, rotationZ), 1003, false));

    // The corners of the convex hull of the captured (x, y), each at the z of a sample there, then the lowest and the highest sample.
    // A box is convex, so it holds all 3,986 samples when it holds these 58.
    private static readonly Vector3[] Captured = [
        new(-2768, 1700, 1951), new(-2767, 1689, 1951), new(-2766, 1683, 1951), new(-2765, 1678, 1951), new(-2683, 1333, 1951), new(-2640, 1205, 1951),
        new(-2637, 1200, 1951), new(-2634, 1196, 1951), new(-2436, 979, 1951), new(619, -2082, 1951), new(1112, -2360, 1951), new(1389, -2415, 1951),
        new(1396, -2415, 1951), new(2134, -2388, 2622), new(2137, -2385, 2264), new(2137, -2384, 2261), new(1906, -1874, 1951), new(1477, -1265, 1951),
        new(-1140, 2118, 1951), new(-1155, 2136, 1951), new(-1198, 2184, 1951), new(-1371, 2357, 1951), new(-1395, 2373, 1951), new(-1444, 2405, 1951),
        new(-1454, 2411, 1951), new(-1464, 2416, 1951), new(-1485, 2426, 1951), new(-1535, 2445, 1951), new(-1557, 2453, 1951), new(-1573, 2458, 1951),
        new(-1585, 2460, 1951), new(-1648, 2468, 1951), new(-1666, 2470, 1951), new(-1689, 2470, 1951), new(-1701, 2469, 1951), new(-1707, 2468, 1951),
        new(-1718, 2466, 1951), new(-1928, 2416, 1951), new(-2070, 2382, 1951), new(-2132, 2367, 1951), new(-2155, 2361, 1951), new(-2188, 2349, 1951),
        new(-2204, 2343, 1951), new(-2215, 2338, 1951), new(-2225, 2333, 1951), new(-2235, 2327, 1951), new(-2244, 2320, 1951), new(-2267, 2302, 1951),
        new(-2272, 2298, 1951), new(-2285, 2286, 1951), new(-2703, 1868, 1951), new(-2710, 1859, 1951), new(-2717, 1849, 1951), new(-2721, 1839, 1951),
        new(-2746, 1767, 1951), new(-2767, 1706, 1951), new(-2397, 1501, 1950), new(2133, -2388, 2656),
    ];

    // Portal 1, where main's move_user puts the player before the fight, and SpawnPointPC0.
    private static readonly Vector3[] Start = [new(-2625, 1575, 1950), new(-2475, 1575, 2100)];

    private static Prism PlayerAt(Vector3 position) => new(new Circle(new Vector2(position.X, position.Y), 10), position.Z, 100);

    [Test]
    public void TurnedBox_HoldsEveryCapturedPosition() {
        TriggerBox box = Box1003(Turn);
        Assert.Multiple(() => {
            Assert.That(Captured.Where(position => !box.Contains(position)), Is.Empty);
            Assert.That(Start.All(position => box.Contains(position)), Is.True, "the player starts the fight inside");
        });
    }

    [Test]
    public void UnturnedOrTurnedTheOtherWay_MissesThem() {
        Assert.Multiple(() => {
            Assert.That(Captured.Count(position => Box1003(0).Contains(position)), Is.EqualTo(16), "today's box holds 844 of the 3,986");
            Assert.That(Captured.Count(position => Box1003(-Turn).Contains(position)), Is.EqualTo(0), "turned the other way it holds 81");
            Assert.That(Start.Any(position => Box1003(0).Contains(position)), Is.False);
        });
    }

    [Test]
    public void Intersects_FollowsTheTurn() {
        Prism west = PlayerAt(Captured[0]); // 1,108 west of today's box
        Prism corner = PlayerAt(new Vector3(1300, 5000, 2000)); // inside today's box, 2,998 beyond the turned one
        Assert.Multiple(() => {
            Assert.That(Box1003(Turn).Intersects(west), Is.True);
            Assert.That(Box1003(Turn).Intersects(corner), Is.False);
            Assert.That(Box1003(0).Intersects(west), Is.False);
            Assert.That(Box1003(0).Intersects(corner), Is.True);
        });
    }

    [Test]
    public void UnturnedBox_KeepsTodaysBounds() {
        TriggerBox box = Box1003(0);
        Assert.Multiple(() => {
            Assert.That(box.Contains(new Vector3(-1660, -4935, 1265)), Is.True);
            Assert.That(box.Contains(new Vector3(1360, 5085, 4275)), Is.True);
            Assert.That(box.Contains(new Vector3(-1661, 75, 2775)), Is.False);
            Assert.That(box.Contains(new Vector3(-150, 5086, 2775)), Is.False);
        });
    }

    // Maple2.Database's options for map entities (PropertyBuilderExtensions): a default value is not written.
    private static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new Vector3Converter() },
    };

    // Box 1003's row in the 1.2.2 data: data built before the map-entity re-import loads unturned and writes the same row.
    [Test]
    public void RowWithoutRotation_LoadsUnturned() {
        const string row = """{"!":1606545175,"Position":{"X":-150,"Y":75,"Z":2775},"Dimensions":{"X":3000,"Y":10000,"Z":3000},"TriggerId":1003}""";
        var box = (Ms2TriggerBox) JsonSerializer.Deserialize<MapBlock>(row, Options)!;
        Assert.Multiple(() => {
            Assert.That(box.Rotation, Is.EqualTo(Vector3.Zero));
            Assert.That(JsonSerializer.Serialize<MapBlock>(box, Options), Is.EqualTo(row));
            Assert.That(JsonSerializer.Serialize<MapBlock>(box with { Rotation = new Vector3(0, 0, Turn) }, Options),
                Is.EqualTo(row.Replace("\"TriggerId\"", "\"Rotation\":{\"Z\":45.000652},\"TriggerId\"")));
        });
    }
}
