namespace Ironbound.Rules.Defense;

/// <summary>
/// How an incoming attack changes which defences apply. There is no single "your AC" in a
/// d20 game — only your AC against a particular attack — and this is the narrow, fixed set
/// of variations the core rules define.
/// </summary>
[Flags]
public enum DefenseOptions
{
    None = 0,

    /// <summary>
    /// A touch attack: rays, most spells, grapples. Armour, shields and natural armour are
    /// bypassed; deflection, dodge, size and Dexterity still apply.
    /// </summary>
    TouchAttack = 1,

    /// <summary>
    /// The defender cannot react — flat-footed, surprised, immobilised. Costs the Dexterity
    /// *bonus* and all dodge bonuses. A Dexterity penalty still applies: being clumsy does
    /// not stop hurting because you were caught off guard.
    /// </summary>
    DexterityDenied = 2,
}
