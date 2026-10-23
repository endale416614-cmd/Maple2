using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Serilog;

namespace Maple2.Server.Game.Model;

public class FieldSpawnPointNpc : FieldEntity<SpawnPointNPC> {
    private int SpawnId => Value.SpawnPointId;

    private bool active;

    private long checkTick;

    private readonly HashSet<int> placed = []; // object ids of the NPCs this point spawned

    public FieldSpawnPointNpc(FieldManager field, int objectId, SpawnPointNPC metadata) : base(field, objectId, metadata) {
        if (metadata.RegenCheckTime > 0) {
            active = true;
        }
    }

    public void SpawnOnCreate() {
        if (Value.SpawnOnFieldCreate) {
            TriggerSpawn(forceFullSpawn: true);
        }
    }

    public override void Update(long tickCount) {
        if (!active || Value.RegenCheckTime <= 0) {
            return;
        }
        if (checkTick > tickCount) {
            return;
        }

        checkTick = tickCount + (long) TimeSpan.FromSeconds(Value.RegenCheckTime).TotalMilliseconds;
        TriggerSpawn();
    }

    public void TriggerSpawn(bool forceFullSpawn = false) {
        // Spawn points without an id all have id 0 (1,843 NPC slots in 61 maps): counted by spawn id they shared one count
        // and most never respawned, so such a point counts the NPCs it placed itself.
        FieldNpc[] npcs = forceFullSpawn ? [] : SpawnId != 0 ? Field.GetActorsBySpawnId(SpawnId).OfType<FieldNpc>().ToArray() : Placed();

        foreach (SpawnPointNPCListEntry spawn in Value.NpcList) {
            if (!Field.NpcMetadata.TryGet(spawn.NpcId, out NpcMetadata? npcMetadata)) {
                Log.Logger.Warning("Npc {NpcId} failed to load for map {MapId}", spawn.NpcId, Field.MapId);
                continue;
            }

            int spawnCountNeeded = forceFullSpawn ? spawn.Count : Missing(spawn.Count, spawn.NpcId, npcs.Select(x => x.Value.Id));

            for (int i = 0; i < spawnCountNeeded; i++) {
                FieldNpc? npc = Field.SpawnNpc(npcMetadata, Value);
                if (npc == null) {
                    continue;
                }
                npc.SpawnPointId = SpawnId;
                placed.Add(npc.ObjectId);

                Field.Broadcast(FieldPacket.AddNpc(npc));
                Field.Broadcast(ProxyObjectPacket.AddNpc(npc));
            }
        }
    }

    /// <summary>How many <paramref name="npcId"/> to add so that the point has <paramref name="count"/> of them again; never below 0.</summary>
    public static int Missing(int count, int npcId, IEnumerable<int> presentNpcIds) => Math.Max(0, count - presentNpcIds.Count(id => id == npcId));

    // The NPCs this point placed that are still in the field (dead ones too until they are removed, as GetActorsBySpawnId).
    private FieldNpc[] Placed() {
        placed.RemoveWhere(objectId => !Field.Mobs.ContainsKey(objectId) && !Field.Npcs.ContainsKey(objectId));
        return placed.Select(objectId => Field.Mobs.GetValueOrDefault(objectId) ?? Field.Npcs.GetValueOrDefault(objectId))
            .OfType<FieldNpc>().ToArray();
    }
}
