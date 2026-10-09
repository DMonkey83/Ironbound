using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Combat;

/// <summary>
/// What the critical feats do once a critical hit is confirmed: bleed, sicken, stagger, stun,
/// tire, exhaust, blind or deafen.
/// </summary>
/// <remarks>
/// Only one of them lands on any one critical, and two with Critical Mastery. The book leaves the
/// pick to the attacker; nothing can be asked in the middle of a swing, so it is made here, the
/// strongest first — a stun before a stagger before anything else — skipping any that would do
/// nothing to this target: tiring somebody already fatigued, bleeding something with a mace.
/// </remarks>
public static class CriticalFeats
{
    public const string BleedLabel = "Bleeding Critical";

    /// <summary>A minute, which is how long the sickness lasts.</summary>
    public const int SickeningRounds = 10;

    /// <summary>Strongest first: the order a critical's one effect is picked in.</summary>
    private static readonly FeatEffect[] Preference =
    [
        FeatEffect.StunningCritical,
        FeatEffect.StaggeringCritical,
        FeatEffect.BlindingCritical,
        FeatEffect.ExhaustingCritical,
        FeatEffect.BleedingCritical,
        FeatEffect.SickeningCritical,
        FeatEffect.DeafeningCritical,
        FeatEffect.TiringCritical,
    ];

    /// <summary>Ten plus the attacker's base attack bonus: the save the critical feats allow.</summary>
    public static int DifficultyClass(Creature attacker) => 10 + attacker.BaseAttackBonus;

    /// <summary>
    /// Lands the attacker's critical feats on a target it has just critically hit, and says what
    /// each did. Nothing for a target that is dead, or a critical feat it has none of.
    /// </summary>
    public static IReadOnlyList<string> Apply(
        Creature attacker, Creature target, WeaponAttack weapon, IRandomSource random, RuleOptions rules)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(random);

        if (!target.IsAlive)
        {
            return [];
        }

        var allowed = attacker.HasFeat(FeatEffect.CriticalMastery) ? 2 : 1;
        var notes = new List<string>();

        foreach (var effect in Preference)
        {
            if (notes.Count >= allowed)
            {
                break;
            }

            if (attacker.HasFeat(effect) && Bites(effect, target, weapon))
            {
                notes.Add(Land(effect, attacker, target, random, rules));
            }
        }

        return notes;
    }

    /// <summary>Whether a critical feat would do anything to this target with this weapon.</summary>
    private static bool Bites(FeatEffect effect, Creature target, WeaponAttack weapon) => effect switch
    {
        // Slashing or piercing only: a mace bruises, it does not open a vein.
        FeatEffect.BleedingCritical => weapon.DamageTypes.Count == 0
            ? weapon.Damage.Components.Any(component => component.Type is DamageType.Piercing or DamageType.Slashing)
            : weapon.DamageTypes.Any(type => type is DamageType.Piercing or DamageType.Slashing),
        FeatEffect.TiringCritical => !target.Has(Condition.Fatigued) && !target.Has(Condition.Exhausted),
        FeatEffect.ExhaustingCritical => !target.Has(Condition.Exhausted),
        FeatEffect.BlindingCritical => !target.Has(Condition.Blinded),
        FeatEffect.DeafeningCritical => !target.Has(Condition.Deafened),
        _ => true,
    };

    private static string Land(FeatEffect effect, Creature attacker, Creature target, IRandomSource random, RuleOptions rules)
    {
        var dc = DifficultyClass(attacker);

        switch (effect)
        {
            case FeatEffect.BleedingCritical:
            {
                // It stacks: another bleeding critical opens another 2d6 a round.
                var dice = 2 + (target.Effects.Find(BleedLabel) is DamageOverTimeEffect open ? open.Amount.DiceRolled : 0);

                target.Effects.Apply(new DamageOverTimeEffect(
                    BleedLabel, Duration.Permanent, $"{dice}d6", DamageType.Untyped));

                return $"{target.Name} bleeds {dice}d6 a round";
            }

            case FeatEffect.SickeningCritical:
                Lengthen(target, Condition.Sickened, "Sickening Critical", Duration.Rounds(SickeningRounds));
                return $"{target.Name} is sickened";

            case FeatEffect.StaggeringCritical:
            {
                var save = SaveRerolls.Attempt(target, Save.Fortitude, dc, random, rules);
                var rounds = save.Succeeded ? 1 : random.NextDie(4) + 1;
                Lengthen(target, Condition.Staggered, "Staggering Critical", Duration.Rounds(rounds));
                return $"{target.Name} is staggered for {rounds} round(s) ({save})";
            }

            case FeatEffect.StunningCritical:
            {
                var save = SaveRerolls.Attempt(target, Save.Fortitude, dc, random, rules);
                var rounds = random.NextDie(4);

                if (save.Succeeded)
                {
                    Lengthen(target, Condition.Staggered, "Stunning Critical", Duration.Rounds(rounds));
                    return $"{target.Name} is staggered for {rounds} round(s) ({save})";
                }

                Lengthen(target, Condition.Stunned, "Stunning Critical", Duration.Rounds(rounds));
                return $"{target.Name} is stunned for {rounds} round(s) ({save})";
            }

            case FeatEffect.TiringCritical:
                target.Effects.Apply(ConditionInfo.Effect(Condition.Fatigued, Duration.Permanent, "Tiring Critical"));
                return $"{target.Name} is fatigued";

            case FeatEffect.ExhaustingCritical:
                target.Effects.Apply(ConditionInfo.Effect(Condition.Exhausted, Duration.Permanent, "Exhausting Critical"));
                return $"{target.Name} is exhausted";

            case FeatEffect.BlindingCritical:
            {
                var save = SaveRerolls.Attempt(target, Save.Fortitude, dc, random, rules);

                if (save.Succeeded)
                {
                    var rounds = random.NextDie(4);
                    Lengthen(target, Condition.Dazzled, "Blinding Critical", Duration.Rounds(rounds));
                    return $"{target.Name} is dazzled for {rounds} round(s) ({save})";
                }

                target.Effects.Apply(ConditionInfo.Effect(Condition.Blinded, Duration.Permanent, "Blinding Critical"));
                return $"{target.Name} is blinded ({save})";
            }

            default:
            {
                var save = SaveRerolls.Attempt(target, Save.Fortitude, dc, random, rules);

                if (save.Succeeded)
                {
                    Lengthen(target, Condition.Deafened, "Deafening Critical", Duration.Rounds(1));
                    return $"{target.Name} is deafened for a round ({save})";
                }

                target.Effects.Apply(ConditionInfo.Effect(Condition.Deafened, Duration.Permanent, "Deafening Critical"));
                return $"{target.Name} is deafened ({save})";
            }
        }
    }

    /// <summary>
    /// Puts a condition on for so long, or adds that much to it if one of the same name is
    /// already running: the book's "additional hits add to the duration".
    /// </summary>
    private static void Lengthen(Creature target, Condition condition, string name, Duration duration)
    {
        if (target.Effects.Find(name) is { } running && !running.Duration.IsPermanent)
        {
            // A save that had it permanent already — a blinding — is left as it is.
            if (running.Condition == condition)
            {
                duration = Duration.FromTicks(running.TicksRemaining + duration.Ticks);
            }
        }
        else if (target.Effects.Find(name) is { Duration.IsPermanent: true, Condition: { } held } && held == condition)
        {
            return;
        }

        target.Effects.Apply(ConditionInfo.Effect(condition, duration, name));
    }
}
