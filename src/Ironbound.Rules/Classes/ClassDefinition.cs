using Ironbound.Rules.Abilities;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

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

    /// <summary>
    /// What it trains its members to fight with: "simple", "martial", or a weapon by id — a
    /// wizard's five are written out one by one. A cleric adds her god's weapon on top.
    /// </summary>
    public IReadOnlyList<string> WeaponProficiencies { get; init; } = [];

    /// <summary>
    /// What armour it trains its members to wear: "light", "medium", "heavy", "shields" and
    /// "tower-shield". Each armour category is its own entry, as in the book's class tables.
    /// </summary>
    public IReadOnlyList<string> ArmourProficiencies { get; init; } = [];

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
    /// The skills this class counts as its own, worth three extra once a rank is spent.
    /// </summary>
    public IReadOnlyList<Skill> ClassSkills { get; init; } = [];

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

    /// <summary>
    /// What the class hands out and when, in the order the rulebook's table lists it.
    /// </summary>
    /// <remarks>
    /// One row per step rather than one row per feature with a formula beside it: sneak attack
    /// is written at first, third, fifth, seventh and ninth level, and "how many dice" is how
    /// many of those rows a rogue has reached. That is how the table in the book reads, and it
    /// keeps the numbers in the file rather than in code — the code only knows what a row of
    /// <c>sneak-attack</c> <em>does</em>.
    /// </remarks>
    public IReadOnlyList<ClassFeatureDefinition> Features { get; init; } = [];

    /// <summary>Every feature row this class has handed out by a given level.</summary>
    public IEnumerable<ClassFeatureDefinition> FeaturesAt(int level) =>
        Features.Where(feature => feature.Level <= level);

    /// <summary>The rows gained on reaching exactly this level.</summary>
    public IEnumerable<ClassFeatureDefinition> FeaturesGainedAt(int level) =>
        Features.Where(feature => feature.Level == level);

    public int CasterLevelAt(int level) => Casting switch
    {
        CasterProgression.Full => level,
        CasterProgression.Half => level / 2,
        _ => 0,
    };

    public override string ToString() => Name;
}

/// <summary>
/// One row of a class's feature table: at this level, this feature, with whatever it needs to
/// be told.
/// </summary>
/// <param name="Id">What the code knows the feature as: <c>sneak-attack</c>, <c>rage</c>. An id
/// the code has never heard of is reported when the content loads.</param>
/// <param name="Parameters">Anything else the row says, such as which list a <c>talent</c> row
/// picks from. Empty for most.</param>
public sealed record ClassFeatureDefinition(
    int Level, string Id, IReadOnlyDictionary<string, string> Parameters)
{
    public ClassFeatureDefinition(int level, string id)
        : this(level, id, new Dictionary<string, string>())
    {
    }

    /// <summary>A parameter, or the fallback when the row does not say.</summary>
    public string Parameter(string name, string fallback = "") =>
        Parameters.TryGetValue(name, out var value) ? value : fallback;

    public override string ToString() => $"{Id} at {Level}";
}

/// <summary>So many levels of one class. A character is a list of these.</summary>
public readonly record struct ClassLevel(ClassDefinition Class, int Level)
{
    public override string ToString() => $"{Class.Name} {Level}";
}
