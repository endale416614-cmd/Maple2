using Maple2.Model.Metadata;

namespace Maple2.Server.Game.Util;

/// <summary>
/// The original side rules from the NPC data: AttackGroup / DefenseGroup are bit masks, and A can attack B when
/// A.AttackGroup &amp; B.DefenseGroup != 0. Monsters are 1/2; allies, guards, barriers and gates are 2/1 (Friendly 1),
/// so monsters attack them; special objects such as cannons and falling grates defend as 4 and are left alone.
/// Story NPCs (Friendly 2) never take part in fights.
/// </summary>
public static class CombatGroups {
    public static bool CanAttack(NpcMetadataBasic attacker, NpcMetadataBasic target) => (attacker.AttackGroup & target.DefenseGroup) != 0;

    /// <summary>Whether a monster may target and hit this friendly NPC (e.g. Cusp of Life's Portal Barrier, gates, Royal Spearmen).</summary>
    public static bool MonsterCanAttack(NpcMetadataBasic monster, NpcMetadataBasic npc) =>
        monster.Friendly == 0 && npc.Friendly == 1 && CanAttack(monster, npc);
}
