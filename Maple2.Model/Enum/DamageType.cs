namespace Maple2.Model.Enum;

public enum DamageType : byte {
    Normal = 0,
    Critical = 1,
    Miss = 2,
    Block = 3,
    Invincible = 4, // a hit on a target with an enabled buff of DefensiveProperty invincible 1, for 0 (NB-21, as captured)
    // 8 is mapped to 3
    Invincible2 = 8, // the same with invincible 2 (NB-21, as captured)
}
