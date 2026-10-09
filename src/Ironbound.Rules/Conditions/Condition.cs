using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Conditions;

/// <summary>
/// The states a creature can be put into that the rules give a name to.
/// </summary>
/// <remarks>
/// Worth having as a closed list rather than as loose modifier bundles, because half the rules
/// in the game ask questions about them: a spell that only affects a frightened target, a feat
/// that triggers on a prone one, an ability that cannot be used while sickened. "A -2 penalty
/// from somewhere" cannot answer any of those.
/// </remarks>
public enum Condition
{
    /// <summary>The first rung of the fear ladder: -2 on almost everything.</summary>
    Shaken,

    /// <summary>Shaken, and would rather be elsewhere.</summary>
    Frightened,

    /// <summary>Queasy. A broad -2, including to the damage you deal.</summary>
    Sickened,

    /// <summary>On the floor. Easier to stab and harder to shoot.</summary>
    Prone,

    /// <summary>Caught in something. Slow and clumsy.</summary>
    Entangled,

    /// <summary>Not doing anything this turn, but still upright and defending.</summary>
    Dazed,

    /// <summary>Not doing anything, and wide open while not doing it.</summary>
    Stunned,

    /// <summary>Cannot see it coming.</summary>
    Blinded,

    /// <summary>
    /// Sound asleep. Does nothing, sees nothing coming, and wakes the moment it is hurt.
    /// </summary>
    /// <remarks>
    /// Last in the list on purpose: saves name conditions as words, but anything that ever
    /// counted them by position would see every later one shift.
    /// </remarks>
    Asleep,

    /// <summary>
    /// Worn out: -2 Strength and Dexterity, and no charging or running. What a barbarian is left
    /// with once her rage is spent, and the reason she cannot simply start another.
    /// </summary>
    /// <remarks>
    /// Appended after <see cref="Asleep"/> for the same reason it was: saves name conditions as
    /// words, but nothing should ever have to wonder whether a position moved.
    /// </remarks>
    Fatigued,

    /// <summary>
    /// A move or a standard action a round and never both, nor anything that takes the whole
    /// round. What a staggering critical does, and what Diehard leaves somebody below nought.
    /// </summary>
    Staggered,

    /// <summary>Half blinded by light: -1 on attacks and on looking for things.</summary>
    Dazzled,

    /// <summary>
    /// Cannot hear: -4 on initiative and on Perception, and one spell in five fumbled, since
    /// nearly every spell is spoken.
    /// </summary>
    Deafened,

    /// <summary>
    /// Worn out past fatigue: -6 Strength and Dexterity, and half speed. Fatigue on top of
    /// fatigue is this.
    /// </summary>
    Exhausted,
}

/// <summary>
/// What a condition actually does, beyond having a name.
/// </summary>
/// <remarks>
/// The modifier side is expressed with <see cref="ModifierGrant"/>, the same shape a spell's
/// buff uses, so conditions cost the stacking engine nothing new. The rest are flags, because
/// "takes no actions" is not a number and pretending otherwise would be worse.
/// </remarks>
public sealed record ConditionRules
{
    public required Condition Condition { get; init; }

    /// <summary>The penalties it hands out, in the ordinary stacking currency.</summary>
    public IReadOnlyList<ModifierGrant> Grants { get; init; } = [];

    /// <summary>Takes no actions at all while it lasts.</summary>
    public bool DeniesActions { get; init; }

    /// <summary>Loses its Dexterity bonus to armour class, as being flat-footed does.</summary>
    public bool DeniesDexterity { get; init; }

    /// <summary>What is left of its speed, as a percentage. A hundred is untouched.</summary>
    public int SpeedPercent { get; init; } = 100;

    /// <summary>Down to one move or standard action a round, as staggered is.</summary>
    public bool SingleAction { get; init; }
}

/// <summary>The rulebook entry for each condition, transcribed once.</summary>
public static class ConditionInfo
{
    private static readonly Dictionary<Condition, ConditionRules> Table = Build();

    public static IReadOnlyList<Condition> All { get; } = [.. Enum.GetValues<Condition>()];

    public static ConditionRules Of(Condition condition) => Table[condition];

    /// <summary>A fresh effect that imposes a condition for as long as it lasts.</summary>
    public static ModifierEffect Effect(Condition condition, Duration duration) =>
        Effect(condition, duration, condition.ToString());

    /// <summary>
    /// The same, filed under a name of its own: a stun from a critical is "Stunning Critical",
    /// so that it lengthens itself rather than replacing a stun that came from somewhere else.
    /// </summary>
    public static ModifierEffect Effect(Condition condition, Duration duration, string name)
    {
        var effect = new ModifierEffect(name, duration) { Condition = condition };

        foreach (var grant in Of(condition).Grants)
        {
            effect.Grants(grant.Value, grant.Type, grant.Target);
        }

        return effect;
    }

    private static Dictionary<Condition, ConditionRules> Build()
    {
        // Penalties are untyped because penalties always stack: being shaken and sickened at
        // once is -4 to hit, not -2 twice over with the larger winning.
        var table = new Dictionary<Condition, ConditionRules>();

        // The fear conditions and sickness reach skill checks and ability checks as well as
        // attacks and saves: a shaken rogue is worse at the lock as well as at the stab.
        table[Condition.Shaken] = new ConditionRules
        {
            Condition = Condition.Shaken,
            Grants = [.. Penalty(-2, attack: true, saves: true, checks: true)],
        };

        table[Condition.Frightened] = new ConditionRules
        {
            // Mechanically shaken. The part where it runs away is a decision, and decisions
            // belong to whatever is driving the creature rather than to the rules.
            Condition = Condition.Frightened,
            Grants = [.. Penalty(-2, attack: true, saves: true, checks: true)],
        };

        table[Condition.Sickened] = new ConditionRules
        {
            Condition = Condition.Sickened,
            Grants = [.. Penalty(-2, attack: true, saves: true, damage: true, checks: true)],
        };

        table[Condition.Prone] = new ConditionRules
        {
            // The -4 to melee attacks is here; what being prone does to your armour class
            // depends on who is shooting at you, so it lives with the attack resolution.
            Condition = Condition.Prone,
            Grants = [new ModifierGrant(ModifierTarget.Attack, -4, BonusType.Untyped)],
        };

        table[Condition.Entangled] = new ConditionRules
        {
            Condition = Condition.Entangled,
            Grants =
            [
                new ModifierGrant(ModifierTarget.Attack, -2, BonusType.Untyped),
                new ModifierGrant(ModifierTarget.Ability(Abilities.Ability.Dexterity), -4, BonusType.Untyped),
            ],
            SpeedPercent = 50,
        };

        table[Condition.Dazed] = new ConditionRules
        {
            Condition = Condition.Dazed,
            DeniesActions = true,
        };

        table[Condition.Stunned] = new ConditionRules
        {
            Condition = Condition.Stunned,
            Grants = [new ModifierGrant(ModifierTarget.ArmorClass, -2, BonusType.Untyped)],
            DeniesActions = true,
            DeniesDexterity = true,
        };

        table[Condition.Blinded] = new ConditionRules
        {
            // The -4 on the skills that run on Strength and Dexterity is the book's. Its other
            // half — total concealment against everybody, a miss chance on every swing it makes —
            // waits for concealment.
            Condition = Condition.Blinded,
            Grants =
            [
                new ModifierGrant(ModifierTarget.ArmorClass, -2, BonusType.Untyped),
                .. Skills.SkillInfo.All
                    .Where(skill => Skills.SkillInfo.AbilityFor(skill) is Abilities.Ability.Strength or Abilities.Ability.Dexterity)
                    .Select(skill => new ModifierGrant(ModifierTarget.Skill(skill), -4, BonusType.Untyped)),
            ],
            DeniesDexterity = true,
            SpeedPercent = 50,
        };

        table[Condition.Asleep] = new ConditionRules
        {
            // Strictly a sleeper is helpless, which is worse than this: Dexterity counted as
            // nought rather than merely lost, and a throat to cut. The rules have no notion of
            // helpless yet, and stunned-without-the-penalty is the nearest honest thing they can
            // say — no turn, no Dexterity, no swings at anybody walking past.
            Condition = Condition.Asleep,
            DeniesActions = true,
            DeniesDexterity = true,
        };

        table[Condition.Fatigued] = new ConditionRules
        {
            // Charging and running are the other half of the rule; the engine has neither yet,
            // so the two ability penalties are the whole of it.
            Condition = Condition.Fatigued,
            Grants =
            [
                new ModifierGrant(ModifierTarget.Ability(Abilities.Ability.Strength), -2, BonusType.Untyped),
                new ModifierGrant(ModifierTarget.Ability(Abilities.Ability.Dexterity), -2, BonusType.Untyped),
            ],
        };

        table[Condition.Staggered] = new ConditionRules
        {
            Condition = Condition.Staggered,
            SingleAction = true,
        };

        table[Condition.Dazzled] = new ConditionRules
        {
            // The book's -1 is on sight-based Perception. Nearly all of it is, and the game has
            // no other kind, so it is all of Perception here.
            Condition = Condition.Dazzled,
            Grants =
            [
                new ModifierGrant(ModifierTarget.Attack, -1, BonusType.Untyped),
                new ModifierGrant(ModifierTarget.Skill(Skills.Skill.Perception), -1, BonusType.Untyped),
            ],
        };

        table[Condition.Deafened] = new ConditionRules
        {
            // The book fails outright any Perception check made by sound and takes four off the
            // opposed ones. Nothing here tells one from the other, so it is four off them all.
            // The fumbled spells are Casting's business.
            Condition = Condition.Deafened,
            Grants =
            [
                new ModifierGrant(ModifierTarget.Initiative, -4, BonusType.Untyped),
                new ModifierGrant(ModifierTarget.Skill(Skills.Skill.Perception), -4, BonusType.Untyped),
            ],
        };

        table[Condition.Exhausted] = new ConditionRules
        {
            // Running and charging are barred as fatigue bars them; neither exists to bar yet,
            // except the run, which asks.
            Condition = Condition.Exhausted,
            Grants =
            [
                new ModifierGrant(ModifierTarget.Ability(Abilities.Ability.Strength), -6, BonusType.Untyped),
                new ModifierGrant(ModifierTarget.Ability(Abilities.Ability.Dexterity), -6, BonusType.Untyped),
            ],
            SpeedPercent = 50,
        };

        return table;
    }

    private static IEnumerable<ModifierGrant> Penalty(
        int value, bool attack = false, bool saves = false, bool damage = false, bool checks = false)
    {
        if (checks)
        {
            yield return new ModifierGrant(ModifierTarget.AllSkills, value, BonusType.Untyped);
            yield return new ModifierGrant(ModifierTarget.AbilityChecks, value, BonusType.Untyped);
        }

        if (attack)
        {
            yield return new ModifierGrant(ModifierTarget.Attack, value, BonusType.Untyped);
        }

        if (damage)
        {
            yield return new ModifierGrant(ModifierTarget.Damage, value, BonusType.Untyped);
        }

        if (!saves)
        {
            yield break;
        }

        foreach (var save in SaveInfo.All)
        {
            yield return new ModifierGrant(ModifierTarget.Save(save), value, BonusType.Untyped);
        }
    }
}
