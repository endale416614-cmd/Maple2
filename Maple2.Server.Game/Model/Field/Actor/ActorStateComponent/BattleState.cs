using System.Numerics;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Util;
using static Maple2.Model.Metadata.AiMetadata;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public class BattleState {
    private readonly FieldNpc actor;

    public NodeTargetType TargetType {
        get {
            if (targetNode is null) {
                return NodeTargetType.Near;
            }

            return targetNode.Type;
        }
    }
    public TargetNode? TargetNode {
        get => targetNode;
        set {
            targetNode = value;
            changedTargetType = false;
        }
    }
    public bool KeepBattle = false;
    public bool CanBattle = true;
    public bool InBattle { get => TargetId != 0 || KeepBattle; }
    public int TargetId => Target?.ObjectId ?? 0;
    public IActor? Target { get; private set; }
    public IActor? GrabbedUser { get; set; }
    private long nextTargetSearchTick = 0;
    private bool changedTargetType = false;
    private TargetNode? targetNode = null;

    public BattleState(FieldNpc actor) {
        this.actor = actor;
    }

    private string GetTargetName() {
        if (Target is null) {
            return "null";
        }

        if (Target is FieldPlayer player) {
            return player.Value.Character.Name;
        }

        if (Target is FieldNpc npc) {
            return npc.Value.Metadata.Name ?? "npc";
        }

        return "unknown";
    }

    public void Update(long tickCount) {
        if (actor.IsDead) {
            Target = null;

            return;
        }

        if (TargetId == 0 && TargetNode is not null && TargetType == NodeTargetType.HasAdditional && TargetNode.NoChangeWhenNoTarget) {
            return;
        }

        if (TargetId != 0 && (!ShouldKeepTarget() || !CanBattle) && !KeepBattle) {
            actor.AppendDebugMessage($"Lost target '{GetTargetName()}'\n");

            Target = null;
            // A target node is an instruction for the battle that just ended. Kept around, its from/to range would
            // also limit how far the idle NPC looks for its next target (e.g. 1200 instead of a 6000 sight).
            if (TargetNode is not { Type: NodeTargetType.HasAdditional, NoChangeWhenNoTarget: true }) {
                TargetNode = null;
            }
            if (actor is FieldPet { OwnerId: 0 } pet) {
                actor.Field.Broadcast(PetPacket.SyncTaming(0, pet));
            }
        }

        if (CanBattle && TargetId == 0 && nextTargetSearchTick <= tickCount) {
            if (actor is FieldPet { OwnerId: 0, TamingPoint: <= 0 }) {
                return;
            }
            Target = FindNewTarget();

            nextTargetSearchTick = tickCount + 500;

            if (TargetId != 0) {
                actor.AppendDebugMessage($"New target '{GetTargetName()}'\n");
            }
        }
    }

    /// <summary>
    /// Applies an AI target node: picks the best target under the node's rules (nearest, farthest, random, ...).
    /// When nothing matches, the NPC keeps the target it has.
    /// </summary>
    public void ApplyTargetNode(TargetNode node) {
        TargetNode = node;
        if (!CanBattle) {
            return;
        }

        IActor? next = FindNewTarget();
        if (next is not null && next != Target) {
            Target = next;
            actor.AppendDebugMessage($"Retarget '{GetTargetName()}'\n");
        }
    }

    /// <summary>
    /// A monster busy with an ally, barrier or gate turns on the player who hits it, so players can defend them.
    /// </summary>
    public void Provoke(IActor attacker) {
        if (!CanBattle || KeepBattle || Target is not FieldNpc || attacker.IsDead) {
            return;
        }
        Target = attacker;
        actor.AppendDebugMessage($"Provoked by '{GetTargetName()}'\n");
    }

    /// <summary>Whether a target is still close enough to keep chasing (the NPC's "last sight").</summary>
    public static bool WithinLastSight(Vector3 position, Vector3 targetPosition, NpcMetadataDistance distance) {
        float distanceSquared = (targetPosition - position).LengthSquared();
        float verticalOffset = targetPosition.Z - position.Z;
        float radiusSquared = distance.LastSightRadius * distance.LastSightRadius;
        return distanceSquared < radiusSquared && verticalOffset <= distance.LastSightHeightUp && verticalOffset >= -distance.LastSightHeightDown - 10;
    }

    public bool IsInRange(float distanceSquared, float verticalOffset, float radiusSquared, float heightUp, float heightDown) {
        return distanceSquared < radiusSquared && verticalOffset <= heightUp && verticalOffset >= heightDown - 10;
    }

    public bool ShouldTargetActor(IActor target, float radiusSquared, float heightUp, float heightDown) {
        float dummyMatch = -1;

        return ShouldTargetActor(target, radiusSquared, heightUp, heightDown, ref dummyMatch, null);
    }

    public bool ShouldTargetActor(IActor target, float radiusSquared, float heightUp, float heightDown, ref float currentBestMatch, List<(float, IActor)>? targetCandidates) {
        Vector3 position = actor.Position;

        if (TargetNode is not null && TargetType == NodeTargetType.Near) {
            if (TargetNode.From == 0 && TargetNode.To == 0) {
                position = TargetNode.Center;
            }
        }

        float distanceSquared = (target.Position - position).LengthSquared();
        float verticalOffset = target.Position.Z - position.Z;

        if (!IsInRange(distanceSquared, verticalOffset, radiusSquared, heightUp, -heightDown)) {
            return false;
        }

        if (TargetNode is null) {
            return true;
        }

        bool canMatch;
        float from = TargetNode.From * TargetNode.From;
        float to = TargetNode.To * TargetNode.To;

        switch (TargetType) {
            case NodeTargetType.Rand:
            case NodeTargetType.RandAssociated:
                canMatch = false;
                if (TargetNode.From != 0 || TargetNode.To != 0) {
                    canMatch = distanceSquared >= from && distanceSquared <= to;
                }
                if (canMatch && targetCandidates is not null) {
                    targetCandidates.Add((distanceSquared, target));
                }
                return canMatch;

            case NodeTargetType.Near:
            case NodeTargetType.NearAssociated:
                if (currentBestMatch != -1 && distanceSquared >= currentBestMatch) {
                    return false;
                }
                canMatch = distanceSquared >= from && distanceSquared <= to;
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            case NodeTargetType.Far:
                if (currentBestMatch != -1 && distanceSquared <= currentBestMatch) {
                    return false;
                }
                canMatch = distanceSquared >= from && distanceSquared <= to;
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            case NodeTargetType.Mid:
                canMatch = distanceSquared >= from && distanceSquared <= to;
                if (canMatch && targetCandidates is not null) {
                    targetCandidates.Add((distanceSquared, target));
                }
                return canMatch;

            case NodeTargetType.HasAdditional:
                int friendlyType = GetTargetType();
                if (currentBestMatch != -1 && distanceSquared >= currentBestMatch) {
                    return false;
                }
                canMatch = false;
                if (TargetNode.Target != NodeAiTarget.DefaultTarget) {
                    canMatch = friendlyType switch {
                        0 => target is FieldPlayer,
                        1 => target is FieldNpc,
                        _ => false,
                    };
                }
                canMatch |= target.Buffs.HasBuff(TargetNode.AdditionalId, TargetNode.AdditionalLevel, 0);
                if (canMatch && currentBestMatch != -1) {
                    currentBestMatch = distanceSquared;
                }
                return canMatch;

            case NodeTargetType.GrabbedUser:
                return target == GrabbedUser && distanceSquared >= from && distanceSquared <= to;

            default:
                break;
        }

        return true;
    }

    private bool ShouldKeepTarget() {
        if (changedTargetType || TargetId == 0) {
            return true;
        }

        IActor? target = null;

        if (actor.Field.Players.TryGetValue(TargetId, out FieldPlayer? targetPlayer)) {
            target = targetPlayer;
        } else if (actor.Field.Npcs.TryGetValue(TargetId, out FieldNpc? targetNpc)) {
            target = targetNpc;
        } else if (actor.Field.Mobs.TryGetValue(TargetId, out FieldNpc? targetMob)) {
            target = targetMob;
        }


        if (target is null || target is FieldNpc { IsDead: true }) {
            return false;
        }

        // Keeping a target only depends on sight. The target node's from/to range decides which target to pick
        // (ApplyTargetNode); applying it here made NPCs drop a target they were still chasing as soon as it was
        // farther than e.g. 1200, after which they idled for good.
        return WithinLastSight(actor.Position, target.Position, actor.Value.Metadata.Distance);
    }

    private int GetTargetType() {
        int friendlyType = actor.Value.Metadata.Basic.Friendly;

        if (friendlyType != 2 && TargetNode is not null && TargetType == NodeTargetType.HasAdditional) {
            friendlyType = TargetNode.Target switch {
                NodeAiTarget.Hostile => friendlyType == 0 ? 0 : 1, // enemies target players (0), friendlies target enemies (1)
                NodeAiTarget.Friendly => friendlyType == 1 ? 0 : 1, // enemies target enemies (1), friendlies target players (0)
                _ => friendlyType,
            };
        }

        return friendlyType;
    }

    private IActor? FindNewTarget() {
        int friendlyType = GetTargetType();
        float sightSquared = actor.Value.Metadata.Distance.Sight;
        float sightHeightUp = actor.Value.Metadata.Distance.SightHeightUp;
        float sightHeightDown = actor.Value.Metadata.Distance.SightHeightDown;

        sightSquared *= sightSquared;

        IActor? nextTarget = null;
        float nextTargetDistance = float.MaxValue;
        List<(float, IActor)>? candidates = null;

        if (TargetType == NodeTargetType.RandAssociated || TargetType == NodeTargetType.Mid) {
            candidates = new List<(float, IActor)>();
        }

        // Without a target node every actor in sight matches; take the nearest (players and NPCs are both candidates now).
        float nearestSquared = float.MaxValue;
        void Consider(IActor candidate) {
            if (!ShouldTargetActor(candidate, sightSquared, sightHeightUp, sightHeightDown, ref nextTargetDistance, candidates)) {
                return;
            }
            if (TargetNode is null) {
                float distanceSquared = (candidate.Position - actor.Position).LengthSquared();
                if (distanceSquared >= nearestSquared) {
                    return;
                }
                nearestSquared = distanceSquared;
            }
            nextTarget = candidate;
        }

        if (friendlyType == 0) {
            foreach (FieldPlayer player in actor.Field.Players.Values) {
                Consider(player);
            }

            // Monsters also go for the allies, guards, barriers and gates their attack group may hit (original
            // AttackGroup/DefenseGroup rule): defence maps such as Cusp of Life are lost when monsters break the barrier.
            NpcMetadataBasic self = actor.Value.Metadata.Basic;
            if (self.Friendly == 0) {
                foreach (FieldNpc npc in actor.Field.Npcs.Values) {
                    if (!npc.IsDead && CombatGroups.MonsterCanAttack(self, npc.Value.Metadata.Basic)) {
                        Consider(npc);
                    }
                }
            }
        }

        if (friendlyType == 1) {
            // Hostile NPCs live in Field.Mobs; Field.Npcs only holds friendly ones, so searching it found nothing and
            // friendly NPCs never fought (e.g. the Bastei Peak guardians never started the battle AI that jumps).
            foreach (FieldNpc npc in actor.Field.Mobs.Values) {
                if (npc.Value.Metadata.Basic.Friendly != 0) {
                    continue;
                }

                Consider(npc);
            }
        }

        if (TargetType == NodeTargetType.RandAssociated) {
            if (candidates!.Count == 0) {
                nextTarget = null;
            } else {
                nextTarget = candidates[Random.Shared.Next(0, candidates.Count)].Item2;
            }
        }

        if (TargetType == NodeTargetType.Mid) {
            if (candidates!.Count == 0) {
                nextTarget = null;
            } else {
                candidates.Sort((entry1, entry2) => entry1.Item1.CompareTo(entry2.Item1));

                nextTarget = candidates[candidates.Count / 2].Item2;
            }
        }

        return nextTarget;
    }
}
