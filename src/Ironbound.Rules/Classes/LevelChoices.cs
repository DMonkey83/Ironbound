using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Classes;

/// <summary>
/// What somebody chose for the class features a new level hands out, beyond the general feat
/// every odd level brings.
/// </summary>
/// <param name="BonusFeat">A fighter's or wizard's bonus feat — or the combat feat a rogue's
/// combat trick buys, when that is the talent chosen.</param>
/// <param name="WeaponGroup">The group a fighter's weapon training is in, by id.</param>
/// <param name="Talent">A rogue talent or a rage power, by id.</param>
/// <remarks>
/// Anything left null that the level needs is picked sensibly on the player's behalf — which is
/// how the autopilot and a headless run level at all — and anything given that the level does
/// not hand out is refused rather than quietly dropped.
/// </remarks>
public sealed record LevelChoices(
    FeatDefinition? BonusFeat = null,
    string? WeaponGroup = null,
    string? Talent = null);

/// <summary>
/// What a level in a class asks to be chosen, with everything that may be. An empty list means
/// this level asks for nothing of that kind.
/// </summary>
public sealed record LevelNeeds(
    IReadOnlyList<FeatDefinition> BonusFeats,
    IReadOnlyList<string> WeaponGroups,
    IReadOnlyList<(string Id, string Name)> Talents)
{
    public static LevelNeeds None { get; } = new([], [], []);

    /// <summary>Whether there is anything to choose at all.</summary>
    public bool Any => BonusFeats.Count > 0 || WeaponGroups.Count > 0 || Talents.Count > 0;
}
