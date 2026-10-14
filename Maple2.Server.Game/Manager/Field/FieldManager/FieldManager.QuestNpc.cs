using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Manager.Field;

// Some quest start/turn-in NPCs exist only as trigger event spawns gated on quest states. When the player's state falls
// between the trigger's conditions (a quest turned in, the next not taken yet) and the field is created fresh (relog,
// re-entry), nothing places them and the chain soft-locks. Noxious Grotto 52000037: Goho/Jordy after 60100125/60100130.
public partial class FieldManager {
    private const long QuestNpcCheckInterval = 1000;
    private const long QuestNpcGraceTime = 15000; // the map's own triggers and cutscenes get the first chance

    private static readonly object QuestsByNpcLock = new();
    private static Dictionary<int, (QuestMetadata[] Start, QuestMetadata[] Complete)>? questsByNpc;

    private Dictionary<int, EventSpawnPointNPC> questNpcSpawns = new(); // npcId -> event spawn point used as fallback
    private readonly Dictionary<int, long> questNpcMissingSince = new();
    private readonly HashSet<int> triggerSpawnedNpcIds = [];
    private readonly Dictionary<int, int> questFallbackNpcs = new(); // objectId -> npcId
    private long nextQuestNpcCheck;

    private void InitQuestNpcFallback() {
        if (Entities.EventNpcSpawns.Count == 0 || QuestMetadata == null) {
            return;
        }

        Dictionary<int, (QuestMetadata[] Start, QuestMetadata[] Complete)> index = QuestsByNpc(QuestMetadata);
        questNpcSpawns = TriggerOnlyNpcSpawns(Entities.NpcSpawns, Entities.EventNpcSpawns.Values, npcId =>
            index.ContainsKey(npcId) && NpcMetadata.TryGet(npcId, out NpcMetadata? npc) && npc.Basic.Friendly > 0);
    }

    /// <summary>
    /// NPCs that a map places only through event (trigger) spawn points, mapped to the lowest such spawn point.
    /// </summary>
    public static Dictionary<int, EventSpawnPointNPC> TriggerOnlyNpcSpawns(IEnumerable<SpawnPointNPC> npcSpawns, IEnumerable<EventSpawnPointNPC> eventSpawns, Func<int, bool> include) {
        var placed = npcSpawns.SelectMany(spawn => spawn.NpcList).Select(entry => entry.NpcId).ToHashSet();
        var result = new Dictionary<int, EventSpawnPointNPC>();
        foreach (EventSpawnPointNPC spawn in eventSpawns.OrderBy(spawn => spawn.SpawnPointId)) {
            foreach (SpawnPointNPCListEntry entry in spawn.NpcList) {
                if (placed.Contains(entry.NpcId) || result.ContainsKey(entry.NpcId) || !include(entry.NpcId)) {
                    continue;
                }
                result[entry.NpcId] = spawn;
            }
        }
        return result;
    }

    private static Dictionary<int, (QuestMetadata[] Start, QuestMetadata[] Complete)> QuestsByNpc(QuestMetadataStorage storage) {
        lock (QuestsByNpcLock) {
            if (questsByNpc != null) {
                return questsByNpc;
            }

            List<QuestMetadata> quests = storage.GetQuests().ToList();
            ILookup<int, QuestMetadata> start = quests.ToLookup(quest => quest.Basic.StartNpc);
            ILookup<int, QuestMetadata> complete = quests.ToLookup(quest => quest.Basic.CompleteNpc);
            questsByNpc = start.Select(group => group.Key)
                .Union(complete.Select(group => group.Key))
                .Where(npcId => npcId != 0)
                .ToDictionary(npcId => npcId, npcId => (start[npcId].ToArray(), complete[npcId].ToArray()));
            return questsByNpc;
        }
    }

    private void UpdateQuestNpcFallback() {
        if (questNpcSpawns.Count == 0 || FieldTick < nextQuestNpcCheck) {
            return;
        }
        nextQuestNpcCheck = FieldTick + QuestNpcCheckInterval;

        foreach ((int npcId, EventSpawnPointNPC spawn) in questNpcSpawns) {
            if (triggerSpawnedNpcIds.Contains(npcId)
                || Npcs.Values.Any(npc => npc.Value.Id == npcId)
                || !Players.Values.Any(player => NeedsQuestNpc(player, npcId))) {
                questNpcMissingSince.Remove(npcId);
                continue;
            }

            if (!questNpcMissingSince.TryGetValue(npcId, out long since)) {
                questNpcMissingSince[npcId] = FieldTick;
                continue;
            }
            if (FieldTick - since < QuestNpcGraceTime) {
                continue;
            }

            questNpcMissingSince.Remove(npcId);
            SpawnQuestFallbackNpc(npcId, spawn);
        }
    }

    private static bool NeedsQuestNpc(FieldPlayer player, int npcId) {
        if (questsByNpc == null || !questsByNpc.TryGetValue(npcId, out (QuestMetadata[] Start, QuestMetadata[] Complete) quests)) {
            return false;
        }

        QuestManager manager = player.Session.Quest;
        foreach (QuestMetadata metadata in quests.Complete) {
            if (manager.TryGetQuest(metadata.Id, out Quest? quest) && quest.State == QuestState.Started && manager.CanComplete(quest)) {
                return true;
            }
        }
        foreach (QuestMetadata metadata in quests.Start) {
            if (metadata.EventMissionType == QuestEventMissionType.none && !manager.TryGetQuest(metadata.Id, out _) && manager.CanStart(metadata)) {
                return true;
            }
        }
        return false;
    }

    private void SpawnQuestFallbackNpc(int npcId, EventSpawnPointNPC spawn) {
        if (!NpcMetadata.TryGet(npcId, out NpcMetadata? metadata)) {
            return;
        }

        FieldNpc? npc = SpawnNpc(metadata, spawn);
        if (npc == null) {
            return;
        }
        npc.SpawnPointId = spawn.SpawnPointId; // destroy_monster on this spawn id still removes it

        questFallbackNpcs[npc.ObjectId] = npcId;
        Broadcast(FieldPacket.AddNpc(npc));
        Broadcast(ProxyObjectPacket.AddNpc(npc));
        logger.Information("Quest NPC {NpcId} was not placed by any trigger for the player's quest state; spawned it at spawn point {SpawnId} in map {MapId}",
            npcId, spawn.SpawnPointId, MapId);
    }

    /// <summary>
    /// Called when a trigger spawns <paramref name="npcId"/>: from then on the trigger owns that NPC in this field,
    /// and a fallback copy already standing in for it is removed.
    /// </summary>
    public void OnTriggerSpawnNpc(int npcId) {
        triggerSpawnedNpcIds.Add(npcId);
        foreach ((int objectId, int fallbackNpcId) in questFallbackNpcs.ToArray()) {
            if (fallbackNpcId != npcId) {
                continue;
            }
            questFallbackNpcs.Remove(objectId);
            RemoveNpc(objectId);
        }
    }
}
