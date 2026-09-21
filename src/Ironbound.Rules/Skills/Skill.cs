using Ironbound.Rules.Abilities;

namespace Ironbound.Rules.Skills;

/// <summary>
/// The things a character can be good at that are not hitting people.
/// </summary>
/// <remarks>
/// A trimmed list rather than the full twenty-odd: every one here is either used by something
/// in the game or is the obvious home for something that will be. Adding more is a line in an
/// enum and a line in a table.
/// </remarks>
public enum Skill
{
    Acrobatics,
    Bluff,
    Climb,
    Diplomacy,
    DisableDevice,
    Heal,
    Intimidate,
    Knowledge,
    Perception,
    SenseMotive,
    Spellcraft,
    Stealth,
    Survival,
    Swim,
}

/// <summary>What each skill runs on, and whether you can try it untrained.</summary>
public static class SkillInfo
{
    /// <summary>
    /// Training in a class skill is worth three points on top of the rank itself.
    /// </summary>
    /// <remarks>
    /// The rule that makes a rogue's first rank in Stealth worth four and a wizard's worth one.
    /// It is a cliff rather than a slope on purpose: it makes "is this my kind of thing?" a
    /// bigger question than "how many points did I spend?".
    /// </remarks>
    public const int ClassSkillBonus = 3;

    public static IReadOnlyList<Skill> All { get; } = [.. Enum.GetValues<Skill>()];

    /// <summary>Which ability the check runs on.</summary>
    public static Ability AbilityFor(Skill skill) => skill switch
    {
        Skill.Acrobatics or Skill.DisableDevice or Skill.Stealth => Ability.Dexterity,
        Skill.Climb or Skill.Swim => Ability.Strength,
        Skill.Bluff or Skill.Diplomacy or Skill.Intimidate => Ability.Charisma,
        Skill.Knowledge or Skill.Spellcraft => Ability.Intelligence,
        _ => Ability.Wisdom,
    };

    /// <summary>
    /// Whether an untrained attempt is allowed at all.
    /// </summary>
    /// <remarks>
    /// This is where content gating actually lives. An attack roll always has its five percent;
    /// a skill check does not, and a lock nobody in the party has ranks to pick is a lock that
    /// stays shut however many times you try it.
    /// </remarks>
    public static bool TrainedOnly(Skill skill) =>
        skill is Skill.DisableDevice or Skill.Knowledge or Skill.Spellcraft;

    public static string Name(Skill skill) => skill switch
    {
        Skill.DisableDevice => "Disable Device",
        Skill.SenseMotive => "Sense Motive",
        _ => skill.ToString(),
    };
}
