namespace Ironbound.Rules.Abilities;

/// <summary>The six ability scores, in their canonical order.</summary>
public enum Ability
{
    Strength = 0,
    Dexterity,
    Constitution,
    Intelligence,
    Wisdom,
    Charisma,
}

public static class AbilityInfo
{
    /// <summary>All six, in canonical order. Index matches the enum value.</summary>
    public static IReadOnlyList<Ability> All { get; } =
    [
        Ability.Strength,
        Ability.Dexterity,
        Ability.Constitution,
        Ability.Intelligence,
        Ability.Wisdom,
        Ability.Charisma,
    ];

    public const int Count = 6;

    /// <summary>Three-letter form for the character sheet: "Str", "Dex", "Con".</summary>
    public static string Abbreviate(Ability ability) => ability switch
    {
        Ability.Strength => "Str",
        Ability.Dexterity => "Dex",
        Ability.Constitution => "Con",
        Ability.Intelligence => "Int",
        Ability.Wisdom => "Wis",
        Ability.Charisma => "Cha",
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };
}
