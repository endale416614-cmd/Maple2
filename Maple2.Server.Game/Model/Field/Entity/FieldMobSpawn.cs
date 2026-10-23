using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Maple2.Model.Game;
using Maple2.Model.Game.Field;
using Maple2.Model.Metadata;
using Maple2.Model.Metadata.FieldEntity;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using Maple2.Tools;
using Maple2.Tools.DotRecast;
using Maple2.Tools.Extensions;
using Serilog;

namespace Maple2.Server.Game.Model;

/// <summary>
/// SpawnPoint on field with random NPCs.
/// </summary>
public class FieldMobSpawn : FieldEntity<MapMetadataSpawn> {
    private const int FORCE_SPAWN_MULTIPLIER = 2;
    private const int SPAWN_DISTANCE = 250;
    private const int PET_SPAWN_RATE_TOTAL = 10000;

    private readonly WeightedSet<NpcMetadata> npcs;
    private readonly WeightedSet<ItemMetadata> pets;
    private readonly List<int> spawnedMobs;
    private readonly List<int> spawnedPets;
    private readonly List<Vector3> validSpawns;
    private long spawnTick;
    private int spawnId;

    public FieldMobSpawn(FieldManager field, int objectId, MapMetadataSpawn metadata, WeightedSet<NpcMetadata> npcs, WeightedSet<ItemMetadata> pets) : base(field, objectId, metadata) {
        this.npcs = npcs;
        this.pets = pets;
        spawnedMobs = new List<int>(metadata.Population);
        spawnedPets = new List<int>(metadata.PetPopulation);
        spawnId = metadata.Id;
        if (Value.Cooldown <= 0) {
            Log.Logger.Information("No respawn for mapId:{MapId} spawnId:{SpawnId}", Field.MapId, Value.Id);
        }

        validSpawns = new();
    }

    public void Despawn(int objectId) {
        // Pets do not impact spawn cycle.
        spawnedPets.Remove(objectId);

        if (!spawnedMobs.Remove(objectId)) {
            return;
        }

        // No respawn without a non-zero cooldown.
        if (Value.Cooldown <= 0) {
            return;
        }

#if DEBUG
        // 2x faster respawns.
        const int spawnRate = 500;
#else
        const int spawnRate = 1000;
#endif

        if (spawnedMobs.Count == 0) {
            spawnTick = Math.Min(spawnTick, Environment.TickCount64 + Value.Cooldown * spawnRate);
        } else if (spawnTick == long.MaxValue) {
            spawnTick = Environment.TickCount64 + Value.Cooldown * spawnRate * FORCE_SPAWN_MULTIPLIER;
        }
    }

    /// <summary><paramref name="count"/> positions from <paramref name="pool"/>: distinct while the pool lasts (a partial
    /// Fisher-Yates shuffle), then repeats.</summary>
    public static List<Vector3> PickSpawns(IReadOnlyList<Vector3> pool, int count, Random random) {
        var picked = new List<Vector3>(Math.Max(count, 0));
        if (pool.Count == 0) {
            return picked;
        }
        Vector3[] remaining = pool.ToArray();
        int distinct = Math.Min(count, remaining.Length);
        for (int i = 0; i < distinct; i++) {
            int index = random.Next(remaining.Length - i);
            picked.Add(remaining[index]);
            remaining[index] = remaining[remaining.Length - i - 1];
        }
        for (int i = distinct; i < count; i++) {
            picked.Add(pool[random.Next(pool.Count)]);
        }
        return picked;
    }

    private List<Vector3> GetRandomSpawns(int count) {
        if (count > validSpawns.Count) {
            Log.Logger.Debug("Spawn {SpawnId} in map {MapId} has {SpawnCount} positions for {Picking} monsters; some share one", spawnId, Field.MapId, validSpawns.Count, count);
        }
        return PickSpawns(validSpawns, count, Random.Shared);
    }

    private Vector3? RandomNavMeshPoint() {
        if (!Field.FindNearestPoly(Position, out long closest, out RcVec3f position) || closest == 0) {
            return null;
        }
        float distance = SPAWN_DISTANCE * DotRecastHelper.MapRotation.GetRightAxis().Length(); // navmesh space
        DtStatus status = Field.Navigation.Crowd.GetNavMeshQuery().FindRandomPointAroundCircle(closest, position, distance,
            Field.Navigation.Crowd.GetFilter(0), new RcRand(), out _, out RcVec3f randomPt);
        return status.Failed() ? null : DotRecastHelper.FromNavMeshSpace(randomPt);
    }

    private void InitializeSpawns() {
        if (validSpawns.Count > 0) {
            return;
        }

        if (!Field.MapMetadata.TryGet(Field.MapId, out MapMetadata? map)) {
            Log.Logger.Error("Failed to get map xblock name for map {MapId}", Field.MapId);

            validSpawns.Add(Position);

            return;
        }

        if (!Field.MapData.TryGet(map.XBlock, out FieldAccelerationStructure? mapData)) {
            Log.Logger.Error("Failed to get map xblock name for map {MapId}", Field.MapId);

            validSpawns.Add(Position);

            return;
        }

        foreach (FieldEntity entity in mapData.QuerySpawnsList(Position, SPAWN_DISTANCE)) {
            validSpawns.Add(entity.Position);
        }

        // The ingest counts only box colliders as ground, so a region on mesh or vibrate cubes has few or no spawn tiles
        // (2000083 spawn 15): random navmesh points around it make up the rest, once (DL-06).
        for (int i = validSpawns.Count; i < Value.Population + 1; i++) {
            if (RandomNavMeshPoint() is not { } point) {
                break;
            }
            validSpawns.Add(point);
        }

        if (validSpawns.Count == 0) {
            Log.Logger.Warning("Failed to find spawns for spawn {SpawnId} in map {MapId}", spawnId, Field.MapId);

            validSpawns.Add(Position);
        }
    }

    public override void Update(long tickCount) {
        if (tickCount < spawnTick) {
            return;
        }

        InitializeSpawns();

        spawnTick = long.MaxValue;

        int spawnMobCount = Value.Population - spawnedMobs.Count;
        int petSpawnRate = OfflinePets.SpawnRate(Value.PetSpawnRate);
        bool doSpawnPet = Random.Shared.Next(PET_SPAWN_RATE_TOTAL) < petSpawnRate;

        if (doSpawnPet) {
            ++spawnMobCount;
        }

        List<Vector3> pickedSpawns = GetRandomSpawns(spawnMobCount);
        int spawnIndex = 0;
        for (int i = spawnedMobs.Count; i < Value.Population; i++) {
            FieldNpc? fieldNpc = Field.SpawnNpc(npcs.Get(), pickedSpawns[spawnIndex++], Rotation, owner: this);
            if (fieldNpc == null) {
                continue;
            }

            spawnedMobs.Add(fieldNpc.ObjectId);

            Field.Broadcast(FieldPacket.AddNpc(fieldNpc));
            Field.Broadcast(ProxyObjectPacket.AddNpc(fieldNpc));
        }

        if (petSpawnRate <= 0 || pets.Count <= 0 || spawnedPets.Count >= Value.PetPopulation) {
            return;
        }

        if (doSpawnPet) {
            // Any stats are computed after pet is captured since that's when rarity is determined.
            var pet = new Item(pets.Get());
            FieldPet? fieldPet = Field.SpawnPet(pet, pickedSpawns.Last(), Rotation, owner: this);
            if (fieldPet == null) {
                return;
            }

            spawnedPets.Add(fieldPet.ObjectId);

            Field.Broadcast(FieldPacket.AddPet(fieldPet));
            Field.Broadcast(ProxyObjectPacket.AddPet(fieldPet));
        }
    }

    private Vector3 GetRandomSpawn() {
        int spawnX = Random.Shared.Next((int) Position.X - SPAWN_DISTANCE, (int) Position.X + SPAWN_DISTANCE);
        int spawnY = Random.Shared.Next((int) Position.Y - SPAWN_DISTANCE, (int) Position.Y + SPAWN_DISTANCE);
        return new Vector3(spawnX, spawnY, Position.Z);
    }
}
