using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Classes;

/// <summary>
/// A rogue's extra damage against somebody who cannot defend themselves properly, and the
/// talents that ride on it.
/// </summary>
/// <remarks>
/// Precision damage: extra dice of the weapon's own type that a critical does not multiply, which
/// is exactly what <see cref="DamageComponent.Extra(DiceExpression, DamageType)"/> already meant.
/// It reaches the target through the same packet as the weapon, so damage reduction is taken off
/// the whole hit once — the reason sneak attack survives reduction far better than a plain blow.
/// </remarks>
public static class SneakAttack
{
    /// <summary>A ranged sneak attack has to be this close.</summary>
    public const int RangedFeet = 30;

    /// <summary>The name the bleed it leaves is filed under. One bleed: a second replaces the first.</summary>
    public const string BleedLabel = "Bleed";

    /// <summary>The name slow reactions is filed under.</summary>
    public const string SlowReactionsLabel = "Slow Reactions";

    /// <summary>The name a crippling strike's Strength damage is filed under, so it can grow.</summary>
    public const string CripplingLabel = "Strength damage";

    public const int CripplingDamage = 2;

    /// <summary>How many d6: one at first level and another every odd level after.</summary>
    public static int Dice(Creature creature) => ClassFeatures.Rank(creature, FeatureIds.SneakAttack);

    /// <summary>
    /// Whether this blow qualifies: the target cannot use its Dexterity against it, or is
    /// flanked by the attacker — and, for a shot, is close enough to aim at a vital spot.
    /// </summary>
    /// <param name="dexterityDenied">Denied its Dexterity bonus, whether or not it has one.</param>
    /// <param name="feet">How far, when the ground says; unknown distances are assumed close.</param>
    public static bool Applies(
        Creature attacker, WeaponAttack weapon, bool dexterityDenied, bool flanking, int? feet)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        if (Dice(attacker) <= 0 || !(dexterityDenied || flanking))
        {
            return false;
        }

        return !weapon.IsRanged || feet is null or <= RangedFeet;
    }

    /// <summary>The extra component for a hit: so many d6 of the weapon's own damage type.</summary>
    public static DamageComponent Component(Creature attacker, WeaponAttack weapon)
    {
        var type = weapon.Damage.Components.Count > 0
            ? weapon.Damage.Components[0].Type
            : DamageType.Untyped;

        return DamageComponent.Extra(DiceExpression.Parse($"{Dice(attacker)}d6"), type);
    }

    /// <summary>
    /// What the talents leave behind once a sneak attack has landed, as lines for the log.
    /// </summary>
    public static IReadOnlyList<string> Riders(Creature attacker, Creature target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        var lines = new List<string>();

        if (!target.IsAlive)
        {
            return lines;
        }

        var talents = attacker.Choices;

        if (talents.HasTalent(TalentEffect.BleedingAttack))
        {
            // A point a round per die, until somebody heals it. Bleed from one source does not
            // stack with itself; a fresh one replaces the old, which is what the name does.
            var bleed = Dice(attacker);
            target.Effects.Apply(new DamageOverTimeEffect(
                BleedLabel, Duration.Permanent, DiceExpression.Constant(bleed), DamageType.Untyped));
            lines.Add($"{target.Name} bleeds {bleed} a round");
        }

        if (talents.HasTalent(TalentEffect.SlowReactions))
        {
            target.Effects.Apply(new ModifierEffect(SlowReactionsLabel, Duration.Rounds(1)));
            lines.Add($"{target.Name} cannot make attacks of opportunity for a round");
        }

        if (talents.HasTalent(TalentEffect.CripplingStrike)
            && target.Abilities[Ability.Strength].HasScore)
        {
            // Ability damage stacks, so the old effect is read and replaced by a bigger one
            // rather than being overwritten by one of the same size.
            var already = target.Effects.Find(CripplingLabel) is ModifierEffect old
                ? old.GrantedModifiers.Sum(grant => grant.Value)
                : 0;

            target.Effects.Apply(new ModifierEffect(CripplingLabel, Duration.Permanent)
                .GrantsToAbility(Ability.Strength, already - CripplingDamage, BonusType.Untyped));
            lines.Add($"{target.Name} takes {CripplingDamage} Strength damage");
        }

        return lines;
    }

    /// <summary>Whether something has been slowed past taking attacks of opportunity.</summary>
    public static bool IsSlowed(Creature creature) => creature.Effects.Has(SlowReactionsLabel);
}
