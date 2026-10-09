using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Effects;

public enum ModifierTargetKind
{
    ArmorClass,
    Attack,
    Damage,
    Speed,
    Initiative,

    /// <summary>One of the six ability scores, named by <see cref="ModifierTarget.Which"/>.</summary>
    Ability,

    /// <summary>One of the three saving throws.</summary>
    Save,

    /// <summary>One skill, named by <see cref="ModifierTarget.Which"/>: an elf's Perception.</summary>
    Skill,

    /// <summary>Every skill check at once: what being shaken does to all of them.</summary>
    AllSkills,

    /// <summary>
    /// Every ability check — the Strength check to force a door, the Constitution check to
    /// stop bleeding — which the fear and sickness conditions reach as well.
    /// </summary>
    AbilityChecks,
}

/// <summary>
/// Which of a creature's numbers an effect is pushing on.
/// </summary>
/// <remarks>
/// This used to be a delegate picking a stack out of a creature, which read beautifully and could
/// not be written to a save file — a buff would simply vanish on load. Describing the target
/// instead means an effect can be written down and rebuilt, which is the whole reason saving
/// mid-fight works at all.
/// </remarks>
public readonly record struct ModifierTarget(ModifierTargetKind Kind, int Which)
{
    public static ModifierTarget ArmorClass { get; } = new(ModifierTargetKind.ArmorClass, 0);

    public static ModifierTarget Attack { get; } = new(ModifierTargetKind.Attack, 0);

    public static ModifierTarget Damage { get; } = new(ModifierTargetKind.Damage, 0);

    public static ModifierTarget Speed { get; } = new(ModifierTargetKind.Speed, 0);

    public static ModifierTarget Initiative { get; } = new(ModifierTargetKind.Initiative, 0);

    public static ModifierTarget Ability(Ability ability) =>
        new(ModifierTargetKind.Ability, (int)ability);

    public static ModifierTarget Save(Save save) => new(ModifierTargetKind.Save, (int)save);

    public static ModifierTarget Skill(Skills.Skill skill) => new(ModifierTargetKind.Skill, (int)skill);

    public static ModifierTarget AllSkills { get; } = new(ModifierTargetKind.AllSkills, 0);

    public static ModifierTarget AbilityChecks { get; } = new(ModifierTargetKind.AbilityChecks, 0);

    /// <summary>The actual stack on a particular creature.</summary>
    public ModifierStack On(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return Kind switch
        {
            ModifierTargetKind.ArmorClass => creature.ArmorClass.Modifiers,
            ModifierTargetKind.Attack => creature.AttackModifiers,
            ModifierTargetKind.Damage => creature.DamageModifiers,
            ModifierTargetKind.Speed => creature.SpeedModifiers,
            ModifierTargetKind.Initiative => creature.InitiativeModifiers,
            ModifierTargetKind.Ability => creature.Abilities[(Ability)Which].Modifiers,
            ModifierTargetKind.Save => creature.Saves[(Save)Which].Modifiers,
            ModifierTargetKind.Skill => creature.Skills.Modifiers((Skills.Skill)Which),
            ModifierTargetKind.AllSkills => creature.Skills.Checks,
            ModifierTargetKind.AbilityChecks => creature.AbilityCheckModifiers,
            _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
        };
    }

    public override string ToString() => Kind switch
    {
        ModifierTargetKind.Ability => $"ability.{AbilityInfo.Abbreviate((Ability)Which)}",
        ModifierTargetKind.Save => $"save.{SaveInfo.Abbreviate((Save)Which)}",
        ModifierTargetKind.Skill => $"skill.{(Skills.Skill)Which}",
        ModifierTargetKind.AllSkills => "skills",
        ModifierTargetKind.AbilityChecks => "abilityChecks",
        _ => Kind.ToString().ToLowerInvariant(),
    };
}

/// <summary>One modifier an effect hands out, in a form that can be written down.</summary>
public readonly record struct ModifierGrant(ModifierTarget Target, int Value, BonusType Type);
