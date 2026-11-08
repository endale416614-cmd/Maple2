namespace Maple2.Model.Metadata;

/// <summary>table/Server/room_random.xml: groups of rooms, each a pocket realm map drawn by its weight. A Hidden Realm portal (timeEventData type 2) names a group.</summary>
public record RandomRoomTable(IReadOnlyDictionary<int, RandomRoomTable.Group> Entries) : ServerTable {
    // Probability is of 10000: 10000 in the three groups the portals name.
    public record Group(int Probability, Room[] Rooms);

    // DurationTick: the room's clock in ms. Model: the portal's nif.
    public record Room(int Id, int Weight, int MapId, int DurationTick, string Model);
}
