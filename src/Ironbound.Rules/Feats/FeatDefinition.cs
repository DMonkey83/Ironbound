using Ironbound.Rules.Abilities;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Feats;

/// <summary>
/// The handful of feats whose effect the engine has to know about by name.
/// </summary>
/// <remarks>
/// This is the "small effect identifier" the project was designed around, and the line it draws
/// is worth stating: a feat that only hands out numbers needs no entry here at all, because
/// <see cref="FeatDefinition.Grants"/> already says everything about it. An entry is earned only
/// by a feat that changes what the rules <em>do</em> — how many opportunities you get, whether
/// reaching in provokes — which cannot be a modifier however hard one squints.
/// <para>
/// Keeping it an enum rather than a string means a typo in a content file is a load-time problem
/// with the file's name attached, not a feat that silently does nothing for the rest of the game.
/// </para>
/// </remarks>
public enum FeatEffect
{
    /// <summary>No behaviour of its own: the numbers are the whole feat.</summary>
    None,

    /// <summary>An extra attack of opportunity per point of Dexterity bonus.</summary>
    CombatReflexes,

    /// <summary>Tripping no longer provokes, and the check is two better.</summary>
    ImprovedTrip,

    /// <summary>The same, for shoving people about.</summary>
    ImprovedBullRush,

    /// <summary>Lets a creature trade accuracy for damage.</summary>
    PowerAttack,

    /// <summary>Lets a creature trade accuracy for armour class.</summary>
    CombatExpertise,
}

/// <summary>
/// What a feat asks of you before it will have you.
/// </summary>
/// <remarks>
/// Prerequisites are most of what makes feat selection a build rather than a shopping list.
/// Power Attack wanting Strength 13 is why the wiry duellist cannot have it, and Improved Trip
/// wanting Combat Expertise is why the trip specialist spends two slots to get there.
/// </remarks>
public sealed record FeatRequirements
{
    /// <summary>Minimum ability scores, by ability.</summary>
    public IReadOnlyDictionary<Ability, int> Abilities { get; init; } =
        new Dictionary<Ability, int>();

    /// <summary>Feats that must already be held, by id.</summary>
    public IReadOnlyList<string> Feats { get; init; } = [];

    public int BaseAttack { get; init; }

    public int Level { get; init; }

    /// <summary>Whether a creature qualifies, and if not, what it is short of.</summary>
    public IReadOnlyList<string> Unmet(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var missing = new List<string>();

        foreach (var (ability, minimum) in Abilities.OrderBy(entry => entry.Key))
        {
            if ((creature.Abilities[ability].Score ?? 0) < minimum)
            {
                missing.Add($"{AbilityInfo.Abbreviate(ability)} {minimum}");
            }
        }

        foreach (var required in Feats.Where(id => !creature.HasFeat(id)))
        {
            missing.Add(required);
        }

        if (creature.BaseAttackBonus < BaseAttack)
        {
            missing.Add($"base attack +{BaseAttack}");
        }

        if (creature.Level < Level)
        {
            missing.Add($"level {Level}");
        }

        return missing;
    }

    public override string ToString()
    {
        var parts = Abilities
            .OrderBy(entry => entry.Key)
            .Select(entry => $"{AbilityInfo.Abbreviate(entry.Key)} {entry.Value}")
            .Concat(Feats);

        if (BaseAttack > 0)
        {
            parts = parts.Append($"base attack +{BaseAttack}");
        }

        if (Level > 0)
        {
            parts = parts.Append($"level {Level}");
        }

        var written = string.Join(", ", parts);
        return written.Length == 0 ? "none" : written;
    }
}

/// <summary>
/// A feat as written down.
/// </summary>
/// <remarks>
/// Most of them are nothing but a name and a couple of <see cref="ModifierGrant"/>s, which is
/// the point: Dodge, Iron Will and Improved Initiative between them need no C# at all. They are
/// three content files and the stacking engine that already existed.
/// </remarks>
public sealed record FeatDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>One line, for whatever eventually shows a character sheet.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The static bonuses it confers, in the ordinary stacking currency.</summary>
    public IReadOnlyList<ModifierGrant> Grants { get; init; } = [];

    /// <summary>What the rules must know about it beyond the numbers, if anything.</summary>
    public FeatEffect Effect { get; init; } = FeatEffect.None;

    /// <summary>What it asks of you first.</summary>
    public FeatRequirements Requires { get; init; } = new();

    /// <summary>Whether a creature could take it: qualified, and does not already have it.</summary>
    public bool AvailableTo(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return !creature.HasFeat(Id) && Requires.Unmet(creature).Count == 0;
    }

    /// <summary>Hands the feat's static bonuses to a creature. Called once, when it is built.</summary>
    public void ApplyTo(Creatures.Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var grant in Grants)
        {
            grant.Target.On(creature).Add(grant.Value, grant.Type, Name);
        }
    }

    public override string ToString() => Name;
}
