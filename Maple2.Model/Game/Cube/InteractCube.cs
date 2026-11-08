using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Maple2.PacketLib.Tools;
using Maple2.Tools;
using Maple2.Tools.Extensions;

namespace Maple2.Model.Game;

public class InteractCube : IByteSerializable {
    public string Id { get; set; }
    public int ObjectCode { get; init; }
    public readonly FunctionCubeMetadata Metadata;
    public InteractCubeState State { get; set; }

    public Nurturing? Nurturing { get; set; }
    public CubePortalSettings? PortalSettings { get; set; }
    public CubeNoticeSettings? NoticeSettings { get; set; }

    public long InteractingCharacterId { get; set; }

    public InteractCube(Vector3B position, FunctionCubeMetadata metadata) {
        Id = $"4_{position.ConvertToInt()}";
        Metadata = metadata;
        ObjectCode = metadata.Id;
        State = metadata.DefaultState;

        if (metadata.Nurturing is not null) {
            Nurturing = new Nurturing(metadata.Nurturing);
        }

        if (metadata.ConfigurableCubeType is ConfigurableCubeType.UGCPortal) {
            PortalSettings = new CubePortalSettings(position);
        } else if (metadata.ConfigurableCubeType is ConfigurableCubeType.UGCNotice) {
            NoticeSettings = new CubeNoticeSettings();
        }
    }

    public InteractCube(string id, FunctionCubeMetadata metadata, CubePortalSettings? portalSettings, CubeNoticeSettings? noticeSettings) {
        Id = id;
        Metadata = metadata;
        ObjectCode = metadata.Id;
        State = metadata.DefaultState;
        PortalSettings = portalSettings;
        NoticeSettings = noticeSettings;
    }

    public void WriteTo(IByteWriter writer) {
        writer.WriteUnicodeString(Id);
        writer.Write(State);
        if (Nurturing is not null) {
            writer.WriteClass<Nurturing>(Nurturing);
        } else if (Metadata.ControlType is InteractCubeControlType.Nurturing) {
            // The client reads this block for every Nurturing cube. A cube with no data sends what a new nurturing row holds (BT-142).
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            writer.WriteLong(now); // CreationTime
            writer.WriteLong(); // Exp
            writer.WriteShort(1); // Stage, never 0: the client keys its NPC by stage
            writer.WriteShort(1); // ClaimedGiftForStage
            writer.WriteShort();
            writer.WriteLong(now); // LastFeedTime
        }
    }
}
