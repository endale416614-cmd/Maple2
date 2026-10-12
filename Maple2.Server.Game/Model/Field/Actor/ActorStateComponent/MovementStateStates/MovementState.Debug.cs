using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using System.Numerics;
using Maple2.Model.Game;

namespace Maple2.Server.Game.Model.ActorStateComponent;

public partial class MovementState {
    private class DebugMarker {
        public bool Spawned;
        public long NextUpdate = 0;
        public int ObjectId = 0;
    }
    private DebugMarker debugNpc;
    private DebugMarker debugTarget;
    private DebugMarker debugAgent;

    // The marker items are only shown by the commented-out debug drawing below; creating them (three rolled
    // items per NPC spawn) was pure overhead.
    private static DebugMarker InitDebugMarker(int itemId, int rarity) => new();

    private void UpdateDebugMarker(Vector3 position, DebugMarker marker, long tickCount) {
        if (tickCount < marker.NextUpdate) {
            return;
        }

        marker.NextUpdate = tickCount + 50;

        if (marker.Spawned) {
            //actor.Field.Broadcast(FieldPacket.RemoveItem(marker.ObjectId));
        }

        marker.Spawned = true;
        //marker.ObjectId = actor.Field.RemoveMeNextLocalId();
        //
        //actor.Field.Broadcast(FieldPacket.DropDebugItem(marker.Item, marker.ObjectId, position, 1000000, 0, false));
    }

    private void RemoveDebugMarker(DebugMarker marker, long tickCount) {
        if (tickCount < marker.NextUpdate) {
            return;
        }

        if (!marker.Spawned) {
            return;
        }

        marker.NextUpdate = tickCount + 50;

        marker.Spawned = false;
        //actor.Field.Broadcast(FieldPacket.RemoveItem(marker.ObjectId));
        marker.ObjectId = 0;
    }
}
