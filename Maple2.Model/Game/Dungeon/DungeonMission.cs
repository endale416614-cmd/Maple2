using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;

namespace Maple2.Model.Game.Dungeon;

public class DungeonMission : IByteSerializable {
    public readonly DungeonMissionMetadata Metadata;
    public int Id => Metadata.Id;
    public short Score { get; private set; }
    public short Counter { get; private set; }

    public DungeonMission(DungeonMissionMetadata metadata) {
        Metadata = metadata;
        // Penalty missions start full, as captured (Ishura's 23037003 at 300 in the Load).
        Score = metadata.IsPenaltyType ? metadata.MaxScore : (short) 0;
    }

    public bool Update(int counter = 1) {
        if (Counter >= Metadata.ApplyCount) {
            return false;
        }

        Counter += (short) counter;
        float percentage = (float) Counter / Metadata.ApplyCount;
        Score = (short) (percentage * Metadata.MaxScore);
        return true;
    }

    public void Complete() {
        // The original sends counter 1 for a mission with applyCount 0.
        Counter = Math.Max(Metadata.ApplyCount, (short) 1);
        Score = Metadata.MaxScore;
    }

    /// <summary>Whether this much of the player's damage to one NPC completes a DamageToNpc mission. Value2 0 means any one NPC.</summary>
    public bool CompletesOnDamage(int npcId, long dealt) =>
        Metadata.Type == DungeonMissionType.DamageToNpc && Score < Metadata.MaxScore && Metadata.Value1.Length > 0
        && (Metadata.Value2 == 0 || Metadata.Value2 == npcId) && dealt >= Metadata.Value1[0];

    public void WriteTo(IByteWriter writer) {
        writer.WriteInt(Id);
        writer.WriteShort(Score);
        writer.WriteShort(Counter);
    }
}
