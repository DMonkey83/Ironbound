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
}

/// <summary>The rulebook entry for each condition, transcribed once.</summary>
public static class ConditionInfo
{
    private static readonly Dictionary<Condition, ConditionRules> Table = Build();

    public static IReadOnlyList<Condition> All { get; } = [.. Enum.GetValues<Condition>()];

    public static ConditionRules Of(Condition condition) => Table[condition];

    /// <summary>A fresh effect that imposes a condition for as long as it lasts.</summary>
    public static ModifierEffect Effect(Condition condition, Duration duration)
    {
        var effect = new ModifierEffect(condition.ToString(), duration) { Condition = condition };

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

        table[Condition.Shaken] = new ConditionRules
        {
            Condition = Condition.Shaken,
            Grants = [.. Penalty(-2, attack: true, saves: true)],
        };

        table[Condition.Frightened] = new ConditionRules
        {
            // Mechanically shaken. The part where it runs away is a decision, and decisions
            // belong to whatever is driving the creature rather than to the rules.
            Condition = Condition.Frightened,
            Grants = [.. Penalty(-2, attack: true, saves: true)],
        };

        table[Condition.Sickened] = new ConditionRules
        {
            Condition = Condition.Sickened,
            Grants = [.. Penalty(-2, attack: true, saves: true, damage: true)],
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
            Condition = Condition.Blinded,
            Grants = [new ModifierGrant(ModifierTarget.ArmorClass, -2, BonusType.Untyped)],
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

        return table;
    }

    private static IEnumerable<ModifierGrant> Penalty(
        int value, bool attack = false, bool saves = false, bool damage = false)
    {
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
