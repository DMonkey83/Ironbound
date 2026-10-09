using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Creatures;

/// <summary>One racial trait as a sheet shows it: a name and a line saying what it is worth.</summary>
public sealed record RaceTrait(string Name, string Description)
{
    public override string ToString() => $"{Name} — {Description}";
}

/// <summary>A racial bonus to one skill: an elf's keen senses are two more on Perception.</summary>
public readonly record struct RaceSkillBonus(Skill Skill, int Bonus, string Trait);

/// <summary>A racial bonus on saves against one school of magic: an elf's against enchantment.</summary>
public readonly record struct RaceSaveBonus(SpellSchool School, int Bonus, string Trait);

/// <summary>
/// A people, as far as the rules care: the traits every member of it shares.
/// </summary>
/// <remarks>
/// The ability adjustments are written down and deliberately <em>not</em> applied. A creature
/// file holds its final scores — Sylwen's Dexterity 16 already has an elf's two in it — so adding
/// them again would count them twice. They are kept so the file says what the book says, and so
/// a character-building screen has the numbers when there is one.
/// <para>
/// Size, speed, senses and the bonus to beat spell resistance are data for now as well: a
/// creature's own file says how big and how fast it is, there is no lighting for low-light vision
/// to matter in, and no spell resistance to overcome. What the rules do act on is the weapon
/// familiarity, the skill bonuses, the immunities and the save bonuses.
/// </para>
/// </remarks>
public sealed record RaceDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Data only: a creature's own file says how big it is.</summary>
    public CreatureSize Size { get; init; } = CreatureSize.Medium;

    /// <summary>Data only: a creature's own file says how fast it is.</summary>
    public int Speed { get; init; } = 30;

    /// <summary>Data only — see the remarks.</summary>
    public IReadOnlyDictionary<Ability, int> AbilityAdjustments { get; init; } = new Dictionary<Ability, int>();

    /// <summary>Weapons every member is proficient with, by id, whatever their class.</summary>
    public IReadOnlyList<string> FamiliarWeapons { get; init; } = [];

    /// <summary>
    /// A word that makes a weapon of this people martial rather than exotic for them: an elf
    /// treats anything with "elven" in its name as a martial weapon.
    /// </summary>
    public string FamiliarKeyword { get; init; } = string.Empty;

    public IReadOnlyList<RaceSkillBonus> Skills { get; init; } = [];

    /// <summary>Spell descriptors that do nothing to them: "sleep" for an elf.</summary>
    public IReadOnlyList<string> ImmuneTo { get; init; } = [];

    public IReadOnlyList<RaceSaveBonus> Saves { get; init; } = [];

    /// <summary>Data only: there is no spell resistance yet.</summary>
    public int SpellResistanceChecks { get; init; }

    /// <summary>Data only: identifying magic items is not in the game.</summary>
    public int IdentifyItems { get; init; }

    /// <summary>Data only: "low-light vision". There is no lighting yet.</summary>
    public IReadOnlyList<string> Senses { get; init; } = [];

    /// <summary>Every trait in words, for the sheet, in the order the book lists them.</summary>
    public IReadOnlyList<RaceTrait> Traits { get; init; } = [];

    /// <summary>Whether this people's familiarity covers a weapon outright.</summary>
    public bool IsFamiliarWith(string? kind) =>
        kind is not null && FamiliarWeapons.Contains(kind, StringComparer.Ordinal);

    /// <summary>
    /// The category a weapon counts as for one of this people: an elven weapon is martial to an
    /// elf however exotic it is to everybody else.
    /// </summary>
    public WeaponCategory CategoryOf(WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return weapon.Category == WeaponCategory.Exotic
            && FamiliarKeyword.Length > 0
            && weapon.Kind is { } kind
            && kind.Split('-').Contains(FamiliarKeyword, StringComparer.Ordinal)
                ? WeaponCategory.Martial
                : weapon.Category;
    }

    /// <summary>Whether a spell's descriptors include one this people shrugs off.</summary>
    public bool IsImmuneTo(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return ImmuneTo.Any(spell.Has);
    }

    /// <summary>What a member of this people adds to a save against a spell of this school.</summary>
    public IEnumerable<Modifier> SaveBonusesAgainst(Spell spell)
    {
        ArgumentNullException.ThrowIfNull(spell);

        return Saves
            .Where(bonus => bonus.School == spell.School)
            .Select(bonus => new Modifier(bonus.Bonus, BonusType.Racial, bonus.Trait));
    }

    /// <summary>
    /// Hands over the standing bonuses — the skills — under each trait's name. Called once, when a
    /// creature of this people is built, exactly as a feat's are; a save carries them from there.
    /// </summary>
    public void ApplyTo(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var bonus in Skills)
        {
            creature.Skills.Modifiers(bonus.Skill).Add(bonus.Bonus, BonusType.Racial, bonus.Trait);
        }
    }

    public override string ToString() => Name;
}
