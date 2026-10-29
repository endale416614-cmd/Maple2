using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Game.Model;

public class FieldFunctionInteract : FieldEntity<FunctionCubeMetadata> {
    public readonly InteractCube InteractCube;
    public readonly long CubeId;
    /// <summary>The assistant the cube spawned (HS-02); she leaves with the cube.</summary>
    public FieldNpc? Assistant { get; set; }

    private long NextUpdateTick { get; set; }

    public FieldFunctionInteract(FieldManager field, int objectId, InteractCube interactCube, long cubeId) : base(field, objectId, interactCube.Metadata) {
        InteractCube = interactCube;
        NextUpdateTick = Field.FieldTick + Value.AutoStateChangeTime;
        CubeId = cubeId;
    }

    public override void Update(long tickCount) {
        lock (InteractCube) {
            if (InteractCube.Metadata.ControlType is InteractCubeControlType.Notice) {
                return;
            }
            if (InteractCube.State is InteractCubeState.Available or InteractCubeState.None) {
                return;
            }

            if (tickCount < NextUpdateTick) {
                return;
            }

            InteractCube.State = AutoStateTarget(Value);
            Field.Broadcast(FunctionCubePacket.UpdateFunctionCube(InteractCube));
        }
    }

    /// <summary>Where the timer takes a used cube: a switch to its AutoStateChange target (the souvenirs' "1-0" back to 0, so every
    /// use switches them on again); a farm or pen ("1-C", which the ingest reads as [1, 0]) and any other cube to Available.</summary>
    public static InteractCubeState AutoStateTarget(FunctionCubeMetadata metadata) =>
        metadata is { ControlType: InteractCubeControlType.Switch, AutoStateChange: [_, int target] } ? (InteractCubeState) target : InteractCubeState.Available;

    public bool Use() {
        lock (InteractCube) {
            if (InteractCube.State is not InteractCubeState.Available) {
                return false;
            }

            InteractCube.State = InteractCubeState.InUse;
            NextUpdateTick = Field.FieldTick + Value.AutoStateChangeTime;
            return true;
        }
    }

    /// <summary>A switch use: on (held by the user) from the default state, else back to it. True when it switched on.</summary>
    public bool Switch(long characterId) {
        lock (InteractCube) {
            bool on = InteractCube.State == Value.DefaultState;
            InteractCube.State = on ? InteractCubeState.InUse : Value.DefaultState;
            InteractCube.InteractingCharacterId = on ? characterId : 0;
            NextUpdateTick = Field.FieldTick + Value.AutoStateChangeTime;
            return on;
        }
    }
}
