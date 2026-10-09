namespace Ironbound.Rules.Combat;

/// <summary>
/// How much training a weapon asks for, which is what proficiency is written against.
/// </summary>
/// <remarks>
/// A fighter is proficient with every simple and martial weapon and a wizard with five named
/// ones; nobody is proficient with an exotic weapon without a feat for that one weapon. Natural
/// attacks sit outside the scheme altogether: nobody has to learn to bite.
/// </remarks>
public enum WeaponCategory
{
    Simple,
    Martial,
    Exotic,
    Natural,
}

public static class WeaponCategories
{
    /// <summary>
    /// The category a weapon modification moves a weapon into: one step harder to use, simple
    /// to martial and martial to exotic. An exotic weapon stays exotic, and a natural one is
    /// not something a smith can modify.
    /// </summary>
    /// <remarks>
    /// The modifications themselves are catalogue data only; this is the one rule they carry
    /// that the proficiency rules would need, written down here so it is not lost.
    /// </remarks>
    public static WeaponCategory Raised(WeaponCategory category) => category switch
    {
        WeaponCategory.Simple => WeaponCategory.Martial,
        WeaponCategory.Martial => WeaponCategory.Exotic,
        _ => category,
    };

    public static string Name(WeaponCategory category) => category.ToString().ToLowerInvariant();
}
