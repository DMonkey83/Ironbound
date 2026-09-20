using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>
/// The attacker's side of a single attack roll: a bonus built from a
/// <see cref="ModifierStack"/> and the weapon's critical behaviour.
/// </summary>
public sealed class Attack
{
    public const int DieSides = 20;

    /// <summary>Base attack bonus, Strength, weapon enhancement, size, morale, and the rest.</summary>
    public ModifierStack Modifiers { get; } = new();

    public CriticalProfile Critical { get; set; } = CriticalProfile.Standard;

    /// <summary>
    /// Rays, grapples and the like resolve against touch AC. This belongs to the attack,
    /// whereas being flat-footed belongs to the defender — <see cref="Resolve"/> combines them
    /// so no caller has to remember to.
    /// </summary>
    public bool TargetsTouchArmorClass { get; set; }

    /// <param name="defenderState">What is true of the target: <see cref="DefenseOptions.DexterityDenied"/>
    /// when flat-footed, surprised or immobilised.</param>
    /// <param name="rules">Defaults to <see cref="RuleOptions.Pathfinder"/>.</param>
    public AttackResult Resolve(
        ArmorClass defense,
        IRandomSource random,
        DefenseOptions defenderState = DefenseOptions.None,
        RuleOptions? rules = null) =>
        Resolve(defense, random, Modifiers.Explain(), defenderState, rules);

    /// <summary>
    /// Resolves using a bonus worked out elsewhere. <see cref="Strike"/> uses this because only
    /// it knows both the wielder and the weapon, and their modifiers have to meet the stacking
    /// rules together rather than as two totals added up.
    /// </summary>
    /// <param name="cover">What the ground is worth to the defender, added to their armour
    /// class. Positional, so it cannot live in the defender's own modifier stack: the same
    /// pillar that shields them from the archer shields them from nobody standing beside it.</param>
    public AttackResult Resolve(
        ArmorClass defense,
        IRandomSource random,
        ModifierBreakdown bonus,
        DefenseOptions defenderState = DefenseOptions.None,
        RuleOptions? rules = null,
        int cover = 0,
        int prone = 0)
    {
        ArgumentNullException.ThrowIfNull(defense);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(bonus);
        rules ??= RuleOptions.Pathfinder;

        var options = defenderState;
        if (TargetsTouchArmorClass)
        {
            options |= DefenseOptions.TouchAttack;
        }

        var armorClass = defense.Value(options) + cover + prone;

        var natural = random.NextDie(DieSides);
        var total = natural + bonus.Total;
        var hit = Lands(natural, total, armorClass, rules);

        // A roll in the threat range only threatens if it actually hit; otherwise no
        // confirmation is rolled at all, and the random stream stays where a replay expects it.
        var threatened = hit && Critical.Threatens(natural);

        int? confirmationNatural = null;
        int? confirmationTotal = null;
        var confirmed = false;

        if (threatened && !rules.ConfirmCriticals)
        {
            confirmed = true;
        }
        else if (threatened)
        {
            var roll = random.NextDie(DieSides);
            confirmationNatural = roll;
            confirmationTotal = roll + bonus.Total;
            confirmed = Lands(roll, confirmationTotal.Value, armorClass, rules);
        }

        var outcome = confirmed ? AttackOutcome.CriticalHit
            : hit ? AttackOutcome.Hit
            : AttackOutcome.Miss;

        return new AttackResult
        {
            NaturalRoll = natural,
            Bonus = bonus,
            Total = total,
            TargetArmorClass = armorClass,
            Options = options,
            Cover = cover,
            Prone = prone,
            Outcome = outcome,
            Threatened = threatened,
            ConfirmationNatural = confirmationNatural,
            ConfirmationTotal = confirmationTotal,
            CriticalMultiplier = confirmed ? Critical.Multiplier : 1,
        };
    }

    /// <summary>
    /// Whether an attack roll connects. By default a natural 20 always does and a natural 1
    /// never does, without consulting the total; the rule applies to the confirmation roll as
    /// well, because a confirmation is itself an attack roll. The natural-20 floor is what keeps
    /// a high-armour opponent reachable at 5% a swing, and
    /// <see cref="RuleOptions.NaturalTwentyAlwaysHits"/> is what removes that mercy.
    /// </summary>
    private static bool Lands(int natural, int total, int armorClass, RuleOptions rules)
    {
        if (natural == DieSides && rules.NaturalTwentyAlwaysHits)
        {
            return true;
        }

        if (natural == 1 && rules.NaturalOneAlwaysMisses)
        {
            return false;
        }

        return total >= armorClass;
    }
}
