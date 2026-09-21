using Ironbound.Rules.Abilities;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>How fast a class learns to hit things.</summary>
public enum AttackProgression
{
    /// <summary>One for one. Fighters, barbarians, and anything whose job is the front rank.</summary>
    Full,

    /// <summary>Three for four. Clerics, rogues, bards — competent, not specialists.</summary>
    ThreeQuarters,

    /// <summary>One for two. Wizards and sorcerers, who are not meant to be doing this.</summary>
    Half,
}

/// <summary>How fast a class's magic grows, if it has any.</summary>
public enum CasterProgression
{
    None,

    /// <summary>Caster level equals class level. Wizards, clerics, druids, sorcerers.</summary>
    Full,

    /// <summary>Half, rounded down. Paladins and rangers, who start at fourth level.</summary>
    Half,
}

/// <summary>
/// A class as written down: what it does to a character per level.
/// </summary>
/// <remarks>
/// This is where the three magic numbers in every creature file used to live. Writing
/// <c>"baseAttack": 6</c> beside <c>"level": 6</c> says nothing about <em>why</em>, and nothing
/// stops the two drifting apart — a fighter with base attack 3 is not wrong in any way the
/// content loader could detect. Saying "fighter 6" leaves one fact on the page and derives the
/// rest.
/// </remarks>
public sealed record ClassDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Sides on the hit die: 6 for a wizard, 10 for a fighter, 12 for a barbarian.</summary>
    public int HitDie { get; init; } = 8;

    public AttackProgression Attack { get; init; } = AttackProgression.ThreeQuarters;

    /// <summary>Which saves rise quickly. Everything else rises slowly.</summary>
    public IReadOnlyList<Save> GoodSaves { get; init; } = [];

    public CasterProgression Casting { get; init; } = CasterProgression.None;

    /// <summary>Which ability powers the spells, when the class has any.</summary>
    public Ability CastingAbility { get; init; } = Ability.Intelligence;

    /// <summary>Base attack bonus this class alone contributes at a given level.</summary>
    public int BaseAttackAt(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);

        return Attack switch
        {
            AttackProgression.Full => level,
            AttackProgression.Half => level / 2,
            _ => level * 3 / 4,
        };
    }

    /// <summary>What this class alone contributes to one saving throw.</summary>
    public int SaveAt(Save save, int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);

        return GoodSaves.Contains(save) ? SaveProgression.Good(level) : SaveProgression.Poor(level);
    }

    /// <summary>
    /// Spells per day before ability bonuses, by class level then spell level.
    /// </summary>
    /// <remarks>
    /// A table rather than a formula because that is what it is in the rules — the shape of a
    /// caster's day is designed, not calculated, and a wizard reaching third level and gaining
    /// their first second-level slot is a moment the arithmetic would flatten.
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<int>> SpellSlots { get; init; } = [];

    /// <summary>What this class alone grants at a given level, before any ability bonus.</summary>
    public IReadOnlyList<int> SlotsAt(int level) =>
        level >= 1 && level <= SpellSlots.Count ? SpellSlots[level - 1] : [];

    public int CasterLevelAt(int level) => Casting switch
    {
        CasterProgression.Full => level,
        CasterProgression.Half => level / 2,
        _ => 0,
    };

    public override string ToString() => Name;
}

/// <summary>So many levels of one class. A character is a list of these.</summary>
public readonly record struct ClassLevel(ClassDefinition Class, int Level)
{
    public override string ToString() => $"{Class.Name} {Level}";
}
