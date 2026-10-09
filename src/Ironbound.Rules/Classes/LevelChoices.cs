using Ironbound.Rules.Abilities;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Classes;

/// <summary>What a level in a creature's favoured class is worth on top of everything else.</summary>
public enum FavouredClassBonus
{
    /// <summary>One more hit point. What is taken when nobody says.</summary>
    HitPoint,

    /// <summary>One more skill rank, in <see cref="LevelChoices.FavouredSkill"/>.</summary>
    SkillRank,
}

/// <summary>
/// What somebody chose for the class features a new level hands out, beyond the general feat
/// every odd level brings.
/// </summary>
/// <param name="BonusFeat">A fighter's or wizard's bonus feat — or the combat feat a rogue's
/// combat trick buys, when that is the talent chosen.</param>
/// <param name="WeaponGroup">The group a fighter's weapon training is in, by id.</param>
/// <param name="Talent">A rogue talent or a rage power, by id.</param>
/// <param name="AbilityIncrease">The ability raised by one at fourth, eighth, twelfth, sixteenth
/// and twentieth level.</param>
/// <param name="Favoured">What a level in the favoured class adds: a hit point or a skill rank.</param>
/// <param name="FavouredSkill">Where that skill rank goes, when it is a skill rank.</param>
/// <remarks>
/// Anything left null that the level needs is picked sensibly on the player's behalf — which is
/// how the autopilot and a headless run level at all — and anything given that the level does
/// not hand out is refused rather than quietly dropped.
/// </remarks>
public sealed record LevelChoices(
    FeatDefinition? BonusFeat = null,
    string? WeaponGroup = null,
    string? Talent = null,
    Ability? AbilityIncrease = null,
    FavouredClassBonus? Favoured = null,
    Skill? FavouredSkill = null);

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

    /// <summary>Whether the level raises an ability score: the character level it reaches is a multiple of four.</summary>
    public bool AbilityIncrease { get; init; }

    /// <summary>The ability raised when nobody says: the class's key ability.</summary>
    public Ability DefaultAbility { get; init; } = Ability.Strength;

    /// <summary>Whether the level is in the creature's favoured class, and so worth a hit point or a rank.</summary>
    public bool FavouredClass { get; init; }

    /// <summary>
    /// The skills a favoured skill rank could go into: any skill with fewer ranks than the new
    /// character level, which is as many as a skill may hold.
    /// </summary>
    public IReadOnlyList<Skill> FavouredSkills { get; init; } = [];

    /// <summary>Whether the class's own features ask for anything: a bonus feat, a weapon group, a talent.</summary>
    public bool Any => BonusFeats.Count > 0 || WeaponGroups.Count > 0 || Talents.Count > 0;

    /// <summary>
    /// Whether the level asks anything at all, the ability increase and the favoured-class
    /// bonus included — which, for a level in the favoured class, it always does.
    /// </summary>
    public bool AnyChoice => Any || AbilityIncrease || FavouredClass;
}
