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
