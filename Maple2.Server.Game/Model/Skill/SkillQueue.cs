using Maple2.Model.Enum;

namespace Maple2.Server.Game.Model.Skill;

// Used to keep track and lookup pending skills
// This is a circular array and will overwrite old pending skills
public class SkillQueue {
    // A held skill's cast must outlast what the player does meanwhile: up to 11 motionless casts in 30 s in the captures (BT-162).
    private const int MAX_PENDING = 16;

    private readonly SkillRecord?[] casts;
    public SkillRecord? StateSkill;
    public long? MotionCastUid; // The cast that started the running motion: only its Cancel stops it (BT-162).
    private int index;

    public SkillQueue() {
        casts = new SkillRecord[MAX_PENDING];
        index = 0;
    }

    public void Add(SkillRecord cast) {
        casts[index] = cast;
        AddState(cast);
        index = (index + 1) % MAX_PENDING;
    }

    /// <summary>A cast from the state skill packet takes no slot: the client sends nothing later for its uid, so a slot would only push out a cast it still reports on (BT-162).</summary>
    public void AddState(SkillRecord cast) {
        if (cast.Metadata.Property.State != ActorState.None) {
            StateSkill = cast;
        }
    }

    public SkillRecord? Get(long uid) {
        for (int i = 0; i < MAX_PENDING; i++) {
            if (casts[i]?.CastUid == uid) {
                return casts[i];
            }
        }

        return null;
    }

    public void Remove(long uid) {
        for (int i = 0; i < MAX_PENDING; i++) {
            if (casts[i]?.CastUid != uid) {
                continue;
            }

            casts[i] = null;
            return;
        }
    }

    public void Clear() {
        for (int i = 0; i < MAX_PENDING; i++) {
            casts[i] = null;
        }
    }

    /// <summary>
    /// Returns the most recently added non-null SkillRecord, or null if none exist.
    /// </summary>
    public SkillRecord? GetMostRecent() {
        // Walk backwards from the last written index
        for (int i = 0; i < MAX_PENDING; i++) {
            int idx = (index - 1 - i + MAX_PENDING) % MAX_PENDING;
            if (casts[idx] != null) {
                return casts[idx];
            }
        }
        return null;
    }
}
