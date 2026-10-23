using System.Numerics;

namespace Maple2.Model.Metadata;

// A scripted platform (MS2BreakableNIF); Enabled is the xblock's starting value of set_breakable's flag (it moves while on).
public record Breakable(
    bool Visible,
    int Id,
    int HideTime,
    int ResetTime,
    Vector3 Position,
    Vector3 Rotation,
    bool Enabled = true)
: MapBlock;

public record BreakableActor(
    bool Visible,
    int Id,
    int HideTime,
    int ResetTime,
    int GlobalDropBoxId,
    Vector3 Position,
    Vector3 Rotation)
: MapBlock;
