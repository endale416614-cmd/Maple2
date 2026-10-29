using System.Collections.Concurrent;
using Maple2.Server.Game.Model.Skill;

namespace Maple2.Server.Game.Model;

/// <summary>The vibrate cubes of one field that are broken: the attack that broke each, its packet tick, and until when it stays broken.</summary>
public class BrokenVibrates {
    private record Break(DamageRecord Record, int Tick, long Until);

    private readonly ConcurrentDictionary<string, Break> breaks = new();

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
}
