namespace Ironbound.Rules.Modifiers;

/// <summary>
/// The category a modifier belongs to. Two bonuses of the same type usually do not
/// stack: only the largest applies. Penalties are never subject to that rule.
/// </summary>
public enum BonusType
{
    Untyped = 0,
    Alchemical,
    Armor,
    Circumstance,
    Competence,
    Deflection,
    Dodge,
    Enhancement,
    Inherent,
    Insight,
    Luck,
    Morale,
    NaturalArmor,
    Profane,
    Racial,
    Resistance,
    Sacred,
    Shield,
    Size,
    Trait,
}

/// <summary>How multiple bonuses of one <see cref="BonusType"/> combine.</summary>
public enum StackingRule
{
    /// <summary>Only the largest bonus of this type applies.</summary>
    Highest,

    /// <summary>Every bonus of this type applies, even from the same source.</summary>
    Stacks,

    /// <summary>Bonuses stack across distinct sources; the same source only counts once.</summary>
    StacksPerSource,
}

public static class BonusTypes
{
    public static StackingRule RuleFor(BonusType type) => type switch
    {
        BonusType.Untyped => StackingRule.Stacks,
        BonusType.Dodge => StackingRule.Stacks,
        BonusType.Circumstance => StackingRule.StacksPerSource,
        _ => StackingRule.Highest,
    };

    /// <summary>Lower-case label for the combat log, e.g. "natural armor".</summary>
    public static string Name(BonusType type) => type switch
    {
        BonusType.Untyped => "untyped",
        BonusType.NaturalArmor => "natural armor",
        _ => type.ToString().ToLowerInvariant(),
    };
}
