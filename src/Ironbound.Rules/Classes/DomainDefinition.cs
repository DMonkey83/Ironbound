using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Classes;

/// <summary>
/// The granted powers of domains and arcane schools that need code: one entry per rule.
/// </summary>
/// <remarks>
/// One enum for both because both are the same thing seen from two classes — a short list of
/// abilities that arrive at fixed levels — and because several of them become
/// <see cref="Power"/>s in exactly the same way.
/// </remarks>
public enum GrantedPowerEffect
{
    // ---- Healing domain ----

    /// <summary>A touch that pulls somebody back from below nought.</summary>
    RebukeDeath,

    /// <summary>Cure spells are cast as though empowered.</summary>
    HealersBlessing,

    // ---- War domain ----

    /// <summary>A touch worth half the cleric's level on melee damage for a round.</summary>
    BattleRage,

    /// <summary>A combat feat borrowed for a round at a time.</summary>
    WeaponMaster,

    // ---- Evocation ----

    /// <summary>Evocation damage gains half the wizard's level, once a spell.</summary>
    IntenseSpells,

    /// <summary>A magic missile of her own, a few times a day.</summary>
    ForceMissile,

    /// <summary>A wall of energy. Not implemented: there are no lasting areas to put one in.</summary>
    ElementalWall,

    // ---- Conjuration ----

    /// <summary>Summoned things stay longer. Recorded only: there is no summoning.</summary>
    SummonersCharm,

    /// <summary>A ranged touch of acid, a few times a day.</summary>
    AcidDart,

    /// <summary>Short teleports, so many feet a day.</summary>
    DimensionalSteps,

    // ---- Universalist ----

    /// <summary>A thrown melee weapon that comes back. Recorded only.</summary>
    HandOfTheApprentice,

    /// <summary>Metamagic without the slot. Recorded only.</summary>
    MetamagicMastery,
}

/// <summary>A granted power and the class level it arrives at.</summary>
public readonly record struct GrantedPower(int Level, GrantedPowerEffect Effect);

/// <summary>
/// A cleric's domain: a spell for each level and a couple of granted powers.
/// </summary>
/// <remarks>
/// Content rather than code, so a second god's domains are files rather than switch cases. A
/// spell the engine cannot express yet — Spiritual Weapon needs a weapon that fights by itself —
/// is left as an empty entry rather than a fake, and the domain slot at that level simply has
/// nothing of its own to hold.
/// </remarks>
public sealed record DomainDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Spell ids by spell level, first level first. An empty entry has none yet.</summary>
    public IReadOnlyList<string> Spells { get; init; } = [];

    public IReadOnlyList<GrantedPower> Powers { get; init; } = [];

    /// <summary>The domain's spell of a given level, or null.</summary>
    public string? SpellAt(int level) =>
        level >= 1 && level <= Spells.Count && Spells[level - 1].Length > 0 ? Spells[level - 1] : null;

    public override string ToString() => Name;
}

/// <summary>
/// A wizard's arcane school: what it grants, and which school of magic it is.
/// </summary>
public sealed record SchoolDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>The school of magic it specialises in, or null for the universalist, who does not.</summary>
    public SpellSchool? School { get; init; }

    public IReadOnlyList<GrantedPower> Powers { get; init; } = [];

    public bool IsUniversalist => School is null;

    public override string ToString() => Name;
}
