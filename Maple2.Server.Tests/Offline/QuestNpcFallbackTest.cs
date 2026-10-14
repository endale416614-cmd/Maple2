using System.Collections.Generic;
using System.Numerics;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;

namespace Maple2.Server.Tests.Offline;

/// <summary>Quest NPCs that a map only places through trigger event spawns (Noxious Grotto 52000037: Goho, Jordy).</summary>
public class QuestNpcFallbackTest {
    private const int Goho = 11003204;
    private const int Jordy = 11003198;
    private const int Placed = 11003476;

    private static EventSpawnPointNPC Event(int spawnId, int npcId) =>
        new($"e{spawnId}", spawnId, Vector3.Zero, Vector3.Zero, true, true, 0, 1, [new SpawnPointNPCListEntry(npcId, 1)], 10, 0, "Regen_A");

    private static SpawnPointNPC Regular(int npcId) =>
        new("r", 0, Vector3.Zero, Vector3.Zero, true, true, 0, [new SpawnPointNPCListEntry(npcId, 1)], 10, "");

    [Test]
    public void TriggerOnlyNpcSpawns_PicksLowestEventSpawn_AndSkipsPlacedNpcs() {
        // 52000037: Goho has the cutscene copy 604 and the resting spot 601.
        var events = new List<EventSpawnPointNPC> { Event(604, Goho), Event(601, Goho), Event(603, Jordy), Event(700, Placed) };

        Dictionary<int, EventSpawnPointNPC> spawns = FieldManager.TriggerOnlyNpcSpawns([Regular(Placed)], events, _ => true);

        Assert.That(spawns.Keys, Is.EquivalentTo(new[] { Goho, Jordy }));
        Assert.That(spawns[Goho].SpawnPointId, Is.EqualTo(601));
        Assert.That(spawns[Jordy].SpawnPointId, Is.EqualTo(603));
    }

    [Test]
    public void TriggerOnlyNpcSpawns_RespectsFilter() {
        var events = new List<EventSpawnPointNPC> { Event(601, Goho), Event(603, Jordy) };

        Dictionary<int, EventSpawnPointNPC> spawns = FieldManager.TriggerOnlyNpcSpawns([], events, npcId => npcId == Jordy);

        Assert.That(spawns.Keys, Is.EquivalentTo(new[] { Jordy }));
    }
}
