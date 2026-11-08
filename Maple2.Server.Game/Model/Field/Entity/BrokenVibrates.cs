using System.Collections.Concurrent;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Game.Model;

/// <summary>The vibrate cubes of one field that are broken: the attack that broke each, its packet tick, and until when it stays broken;
/// and the cubes of a hide group that the field's AI showed or hid (ShowVibrate, HideVibrateAll).</summary>
public class BrokenVibrates {
    private record Break(DamageRecord Record, int Tick, long Until);

    private readonly ConcurrentDictionary<string, Break> breaks = new();
    private readonly ConcurrentDictionary<string, bool> shown = new();

    public bool InForce(string id, long now) {
        return breaks.TryGetValue(id, out Break? broken) && now < broken.Until;
    }

    public void Add(string id, DamageRecord record, int tick, long until) {
        // A copy of what VibratePacket.Attack writes, without the targets, so a stored break holds no actor.
        var written = new DamageRecord(record.SkillMetadata, record.AttackMetadata) {
            SkillUid = record.SkillUid,
            SkillId = record.SkillId,
            Level = record.Level,
            MotionPoint = record.MotionPoint,
            AttackPoint = record.AttackPoint,
            Position = record.Position,
        };
        breaks[id] = new Break(written, tick, until);
    }

    /// <summary>The breaks in force at <paramref name="now"/>, for a player entering the field; ended ones are dropped.</summary>
    public List<(string Id, DamageRecord Record, int Tick)> InForceAt(long now) {
        var inForce = new List<(string Id, DamageRecord Record, int Tick)>();
        foreach ((string id, Break broken) in breaks) {
            if (now < broken.Until) {
                inForce.Add((id, broken.Record, broken.Tick));
            } else {
                breaks.TryRemove(new KeyValuePair<string, Break>(id, broken));
            }
        }

        return inForce;
    }

    /// <summary>ShowVibrate (<paramref name="isShown"/>) or HideVibrateAll: the cube comes back whole, or goes; either way its break ends.</summary>
    public void SetShown(string id, bool isShown) {
        breaks.TryRemove(id, out _);
        shown[id] = isShown;
    }

    /// <summary>True for a cube the AI hid; a cube it never showed or hid is as the map has it.</summary>
    public bool IsHidden(string id) {
        return shown.TryGetValue(id, out bool isShown) && !isShown;
    }

    /// <summary>The cubes the AI showed or hid, for a player entering the field.</summary>
    public List<(string Id, bool Shown)> Visibility() {
        return shown.Select(pair => (pair.Key, pair.Value)).ToList();
    }
}
