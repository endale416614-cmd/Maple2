using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Trigger.Helpers;

namespace Maple2.Server.Game.Manager.Field;

public partial class FieldManager {
    public readonly TriggerCollection TriggerObjects;

    private readonly ConcurrentDictionary<int, FieldBreakable> triggerBreakable = new();
    private readonly ConcurrentDictionary<string, FieldTrigger> fieldTriggers = new();
    public readonly Dictionary<string, TickTimer> Timers = new();
    public readonly Dictionary<string, int> UserValues = new();
    public readonly Dictionary<string, Widget> Widgets = new();
    public readonly Dictionary<int, List<TriggerState>> States = new();
    public readonly FieldScore Score = new(); // the gauge or score board the map's scripts open (SE-01)

    /// <summary>Adds a kill's or a score item's points to the open gauge or score board; a gauge shows the new total.</summary>
    public void AddScore(int points) {
        if (Score.Add(points) && Score.IsGauge) {
            Broadcast(ShadowExpeditionPacket.Update(Score.Points));
        }
    }

    public FieldTrigger? AddTrigger(TriggerModel trigger) {
        try {
            var fieldTrigger = new FieldTrigger(this, NextLocalId(), trigger) {
                Position = trigger.Position,
                Rotation = trigger.Rotation,
            };
            fieldTriggers[trigger.Name] = fieldTrigger;

            return fieldTrigger;
        } catch (ArgumentException ex) {
            logger.Warning(ex, "Invalid Trigger: {Exception}", ex.Message);
            return null;
        }
    }

    public ICollection<FieldTrigger> EnumerateTrigger() => fieldTriggers.Values;

    /// <summary>Every script enters its new state before any script checks a condition: monster_dead is true while the monster does not exist,
    /// so a script ordered before the one that spawns it would otherwise move on at once (BT-164).</summary>
    public void UpdateTriggers() {
        foreach (FieldTrigger trigger in fieldTriggers.Values) trigger.Update(FieldTick);
        foreach (FieldTrigger trigger in fieldTriggers.Values) trigger.CheckConditions();
    }

    public bool TryGetTrigger(string name, [NotNullWhen(true)] out FieldTrigger? fieldTrigger) {
        return fieldTriggers.TryGetValue(name, out fieldTrigger);
    }

    // Used for debugging triggers
    public bool ReplaceTrigger(FieldTrigger oldFieldTrigger, FieldTrigger newFieldTrigger) {
        return fieldTriggers.TryUpdate(oldFieldTrigger.Value.Name, newFieldTrigger, oldFieldTrigger);
    }
}
