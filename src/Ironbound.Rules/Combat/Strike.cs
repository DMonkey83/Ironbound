using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Combat;

/// <summary>
/// Resolves one creature attacking another, from the d20 to the target losing hit points.
/// </summary>
/// <remarks>
/// A static resolver rather than a method on <see cref="Creature"/>, so it stays a pure function
/// of its arguments with nowhere to hide state. Taking both creatures is also the seam that
/// flanking, sneak attack and favoured enemy will hang off once there is a map to ask about
/// position — none of which <see cref="Combat.Attack"/> alone could ever answer.
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
        var attack = weapon.Attack.Resolve(target.ArmorClass, random, defenderState, rules);

        DamageRoll? damage = null;
        DamageApplication? applied = null;

        if (attack.IsHit)
        {
            // Damage dice are only rolled on a hit, so a miss leaves the random stream
            // exactly where a replay expects to find it.
            damage = weapon.Damage.Roll(random, attack.CriticalMultiplier);
            applied = target.HitPoints.Take(damage.Total);
        }

        return new StrikeResult
        {
            Attacker = attacker,
            Target = target,
            Weapon = weapon,
            Attack = attack,
            Damage = damage,
            Applied = applied,
            StateBefore = before,
            StateAfter = target.HitPoints.State,
        };
    }
}
