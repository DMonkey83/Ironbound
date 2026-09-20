using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>
/// Resolves one creature attacking another, from the d20 to the target losing hit points.
/// </summary>
/// <remarks>
/// A static resolver rather than a method on <see cref="Creature"/>, so it stays a pure function
/// of its arguments with nowhere to hide state. It is also the only place that knows both the
/// wielder and the weapon, which is why the two sets of modifiers are combined here.
/// </remarks>
public static class Strike
{
    /// <param name="defenderState">What is true of the target — flat-footed, surprised,
    /// immobilised. Position-dependent conditions will be derived here once a map exists.</param>
    /// <param name="rules">Defaults to the attacker's own options.</param>
    public static StrikeResult Resolve(
        Creature attacker,
        WeaponAttack weapon,
        Creature target,
        IRandomSource random,
        DefenseOptions defenderState = DefenseOptions.None,
        RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);
        rules ??= attacker.Rules;

        var before = target.HitPoints.State;
        var attack = weapon.Attack.Resolve(
            target.ArmorClass, random, AttackBonus(attacker, weapon), defenderState, rules);

        DamageRoll? damage = null;
        DamageTaken? taken = null;
        DamageApplication? applied = null;
        var nonlethal = 0;

        if (attack.IsHit)
        {
            // Damage dice are only rolled on a hit, so a miss leaves the random stream
            // exactly where a replay expects to find it.
            damage = DamageFor(attacker, weapon).Roll(random, attack.CriticalMultiplier);
            taken = target.Defenses.Apply(damage, weapon.Qualities, rules);
            (applied, nonlethal) = Apply(target, taken);
        }

        return new StrikeResult
        {
            Attacker = attacker,
            Target = target,
            Weapon = weapon,
            Attack = attack,
            Damage = damage,
            Taken = taken,
            Applied = applied,
            NonlethalDealt = nonlethal,
            StateBefore = before,
            StateAfter = target.HitPoints.State,
        };
    }

    /// <summary>
    /// The wielder's modifiers and the weapon's, resolved together in one pass. Adding two
    /// separate totals would let a Magic Weapon spell and a +1 sword stack their enhancement
    /// bonuses, which the rules forbid.
    /// </summary>
    public static ModifierBreakdown AttackBonus(Creature attacker, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        return ModifierStack.Combine(
            attacker.AttackModifiers,
            weapon.Attack.Modifiers,
            SizeOf(attacker),
            Derived(attacker, weapon.AttackAbility, modifier => modifier));
    }

    /// <summary>
    /// Size cuts both ways: the same number that makes a small creature harder to hit makes it
    /// better at hitting. Derived live, so growing or shrinking takes effect at once.
    /// </summary>
    private static ModifierStack SizeOf(Creature attacker)
    {
        var stack = new ModifierStack();
        var modifier = CreatureSizes.Modifier(attacker.Size);

        if (modifier != 0)
        {
            stack.Add(modifier, BonusType.Size, "Size");
        }

        return stack;
    }

    /// <summary>The flat bonus added to the weapon's damage, from the same two sources.</summary>
    public static ModifierBreakdown DamageBonus(Creature attacker, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        return ModifierStack.Combine(
            attacker.DamageModifiers,
            weapon.DamageModifiers,
            Derived(attacker, weapon.DamageAbility, weapon.ScaleDamage));
    }

    /// <summary>
    /// Read live from the ability score, so a Strength buff reaches the very next swing instead
    /// of needing anything to be recomputed.
    /// </summary>
    private static ModifierStack Derived(Creature attacker, Abilities.Ability? ability, Func<int, int> scale)
    {
        var stack = new ModifierStack();
        if (ability is not { } from)
        {
            return stack;
        }

        var value = scale(attacker.Abilities[from].Modifier);
        if (value != 0)
        {
            stack.Add(value, BonusType.Untyped, Abilities.AbilityInfo.Abbreviate(from));
        }

        return stack;
    }

    /// <summary>
    /// The weapon's damage with the flat bonus folded into its own component, so the bonus is
    /// multiplied on a critical and the log still reads as one expression.
    /// </summary>
    private static DamagePacket DamageFor(Creature attacker, WeaponAttack weapon)
    {
        var bonus = DamageBonus(attacker, weapon).Total;
        if (bonus == 0)
        {
            return weapon.Damage;
        }

        var packet = new DamagePacket();
        var folded = false;

        foreach (var component in weapon.Damage.Components)
        {
            if (!folded && component.MultipliedOnCritical)
            {
                packet.Add(new DamageComponent(component.Amount.Plus(bonus), component.Type, true));
                folded = true;
            }
            else
            {
                packet.Add(component);
            }
        }

        if (!folded)
        {
            var type = weapon.Damage.Components.Count > 0
                ? weapon.Damage.Components[0].Type
                : DamageType.Untyped;
            packet.Add(DiceExpression.Constant(bonus), type);
        }

        return packet;
    }

    /// <summary>
    /// Hands the damage to the target, letting regeneration turn all but a few damage types into
    /// nonlethal. Damage that regeneration cannot absorb also stops it for a round.
    /// </summary>
    private static (DamageApplication Applied, int Nonlethal) Apply(Creature target, DamageTaken taken)
    {
        if (target.Effects.Regeneration is not { IsSuspended: false } regeneration)
        {
            return (target.HitPoints.Take(taken.Total), 0);
        }

        var lethal = taken.Entries
            .Where(entry => regeneration.IsSuspendedBy(entry.Type))
            .Sum(entry => entry.Taken);

        if (lethal > 0)
        {
            regeneration.Suspend();
        }

        var applied = target.HitPoints.Take(lethal);
        var nonlethal = target.HitPoints.TakeNonlethal(taken.Total - lethal);

        return (applied, nonlethal);
    }
}
