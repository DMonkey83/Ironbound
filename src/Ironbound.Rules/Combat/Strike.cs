using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Maps;
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
    /// <summary>What having an ally on the far side is worth.</summary>
    public const int FlankingBonus = 2;

    /// <summary>What standing behind something is worth to the target.</summary>
    public const int CoverBonus = 4;

    /// <summary>
    /// What being on the floor is worth: four easier to stab, four harder to shoot. The same
    /// posture cuts both ways, which is why knocking somebody down is a tactic rather than
    /// simply a good thing.
    /// </summary>
    public const int ProneAgainstMelee = -4;

    public const int ProneAgainstRanged = 4;

    /// <summary>
    /// The cost of shooting into a melee your own side is part of. Steep on purpose: it is the
    /// rule that stops an archer treating a scrum as a free target, and the reason a bowman wants
    /// an angle rather than a straight line down the middle of the fight.
    /// </summary>
    public const int IntoMeleePenalty = -4;

    /// <param name="defenderState">What is true of the target — flat-footed, surprised,
    /// immobilised. Position-dependent conditions will be derived here once a map exists.</param>
    /// <param name="rules">Defaults to the attacker's own options.</param>
    /// <param name="vital">A Vital Strike: the weapon's dice rolled twice, the second set not
    /// multiplied on a critical. Only an attack action may ask for it.</param>
    public static StrikeResult Resolve(
        Creature attacker,
        WeaponAttack weapon,
        Creature target,
        IRandomSource random,
        DefenseOptions defenderState = DefenseOptions.None,
        RuleOptions? rules = null,
        Battlefield? field = null,
        int iterativePenalty = 0,
        bool flatFooted = false,
        bool vital = false)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);
        rules ??= attacker.Rules;

        var before = target.HitPoints.State;
        var notes = new List<string>();

        // Stunned or blinded denies Dexterity just as being flat-footed does, and the caller
        // should not have to remember which conditions do that.
        if (target.DeniesDexterity)
        {
            defenderState |= DefenseOptions.DexterityDenied;
        }

        // Uncanny dodge is exactly this: never caught flat-footed. Conditions still bite.
        if (flatFooted && !UncannyDodge.Has(target))
        {
            defenderState |= DefenseOptions.DexterityDenied;
        }

        // Only the armour class asks whether it was a swing, for a guarded stance's sake.
        var asked = weapon.IsRanged ? defenderState : defenderState | DefenseOptions.Melee;
        var feet = field?.DistanceInFeet(attacker, target);
        var flanking = FlankingPartner(attacker, target, field) is not null;

        // A bullet up close goes through armour as if it were not there.
        if (Firearms.TargetsTouch(weapon, feet))
        {
            asked |= DefenseOptions.TouchAttack;
        }

        var broken = attacker.Equipment.IsBroken(weapon);

        var attack = weapon.Attack.Resolve(
            target.ArmorClass,
            random,
            AttackBonus(attacker, weapon, target, field, iterativePenalty),
            asked,
            rules,
            CoverFor(attacker, target, field),
            ProneFor(weapon, target),
            CriticalFor(attacker, weapon));

        // A misfire is a miss whatever the total came to, and it costs the gun.
        var misfired = Firearms.Misfires(weapon, attack.NaturalRoll, broken);
        if (misfired)
        {
            attack = attack with { Outcome = AttackOutcome.Miss, Threatened = false, CriticalMultiplier = 1 };
            notes.Add(Misfire(attacker, weapon, broken, random, rules));
        }
        else if (weapon.Has(WeaponSpecial.Fragile) && attack.NaturalRoll == 1 && Crack(attacker, weapon, broken) is { } cracked)
        {
            notes.Add(cracked);
        }

        // Thrown, hit or miss, it is somewhere on the floor now.
        if (weapon.IsThrownUse && attacker.Equipment.LetGo(weapon))
        {
            notes.Add($"{attacker.Name}'s {ItemName(attacker, weapon)} is out of hand until the fight is over");
        }

        // Declared before the roll, so the roll spends it whether it hit or not.
        var surprised = attacker.Stances.Spend(Combat.Stance.SurpriseAccuracy);
        var powerful = attacker.Stances.IsActive(Combat.Stance.PowerfulBlow)
            ? attacker.Stances.RageBonus(Combat.Stance.PowerfulBlow)
            : 0;
        attacker.Stances.Spend(Combat.Stance.PowerfulBlow);

        if (surprised)
        {
            notes.Insert(0, "surprise accuracy");
        }

        DamageRoll? damage = null;
        DamageTaken? taken = null;
        DamageApplication? applied = null;
        var nonlethal = 0;
        var sneak = 0;

        if (attack.IsHit)
        {
            var denied = (attack.Options & DefenseOptions.DexterityDenied) != 0;
            var type = DamageTypeAgainst(weapon, target);
            var packet = DamageFor(attacker, weapon, feet, powerful, vital, type);

            if (SneakAttack.Applies(attacker, weapon, denied, flanking, feet))
            {
                sneak = SneakAttack.Dice(attacker);
                packet.Add(SneakAttack.Component(attacker, weapon));
            }

            // Damage dice are only rolled on a hit, so a miss leaves the random stream
            // exactly where a replay expects to find it.
            damage = packet.Roll(random, attack.CriticalMultiplier);
            taken = target.Defenses.Apply(damage, QualitiesOf(weapon), rules);

            var (dealt, rolled) = RogueDefences.DefensiveRoll(target, taken.Total, denied, random, rules);
            if (rolled is not null)
            {
                notes.Add($"{target.Name} rolls with it: {rolled}");
            }

            // A sap's blow is nonlethal all through, sneak attack and all, as the book has it.
            (applied, nonlethal) = packet.Components.Any(component => component.Nonlethal)
                ? (default(DamageApplication), target.HitPoints.TakeNonlethal(dealt))
                : Apply(target, taken, dealt);

            if (sneak > 0)
            {
                notes.AddRange(SneakAttack.Riders(attacker, target));
            }
        }

        if (powerful > 0)
        {
            notes.Insert(0, attack.IsHit ? $"powerful blow +{powerful}" : "powerful blow wasted");
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
            TargetAfter = target.HitPoints.ToString(),
            SneakAttackDice = sneak,
            Vital = vital,
            Misfired = misfired,
            Notes = notes,
        };
    }

    /// <summary>
    /// The critical this wielder threatens with this weapon: Improved Critical's, unless the
    /// weapon is broken, when it threatens only on a twenty and only doubles.
    /// </summary>
    public static CriticalProfile CriticalFor(Creature attacker, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        return attacker.Equipment.IsBroken(weapon) ? CriticalProfile.Standard : Martial.Critical(attacker, weapon);
    }

    /// <summary>What the weapon's damage types let it past: every one of them, for "B and P".</summary>
    private static DamageBypass QualitiesOf(WeaponAttack weapon)
    {
        var qualities = weapon.Qualities;

        if (weapon.DamageRule == DamageRule.Both)
        {
            foreach (var type in weapon.DamageTypes)
            {
                qualities |= DamageBypasses.Of(type);
            }
        }

        return qualities;
    }

    /// <summary>
    /// The type a "P or S" weapon is swung as against this target: whichever its damage
    /// reduction stops least, then whichever its resistances and immunities spare, then the
    /// first the weapon lists. Anything else deals the one type it deals.
    /// </summary>
    public static DamageType? DamageTypeAgainst(WeaponAttack weapon, Creature target)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);

        if (weapon.DamageRule != DamageRule.Either || weapon.DamageTypes.Count < 2)
        {
            return null;
        }

        var defenses = target.Defenses;

        return weapon.DamageTypes
            .Select((type, order) => (Type: type, Order: order))
            .OrderBy(choice => defenses.IsImmuneTo(choice.Type) ? 1 : 0)
            .ThenBy(choice => defenses.ReductionAgainst(weapon.Qualities | DamageBypasses.Of(choice.Type)))
            .ThenBy(choice => defenses.ResistanceTo(choice.Type))
            .ThenBy(choice => defenses.IsVulnerableTo(choice.Type) ? 0 : 1)
            .ThenBy(choice => choice.Order)
            .First()
            .Type;
    }

    /// <summary>
    /// A gun misfiring: broken if it was sound, and if it was already broken, gone — burst in
    /// the wielder's hands.
    /// </summary>
    /// <remarks>
    /// The burst is <see cref="Firearms"/>' simplification: the gun's own dice as fire damage to
    /// the wielder alone, a Reflex save for half.
    /// </remarks>
    private static string Misfire(Creature attacker, WeaponAttack weapon, bool broken, IRandomSource random, RuleOptions rules)
    {
        var name = ItemName(attacker, weapon);

        if (!broken)
        {
            attacker.Equipment.Break(weapon);
            return $"misfire — the {name} is broken";
        }

        attacker.Equipment.Destroy(weapon);

        var save = attacker.Saves.Attempt(Saves.Save.Reflex, Firearms.ExplosionSaveDc, random, rules);
        var rolled = weapon.Damage.Components.Count > 0
            ? weapon.Damage.Components[0].Amount.DiceOnly().Roll(random).Total
            : 0;
        var amount = save.Succeeded ? rolled / 2 : rolled;
        var taken = amount > 0
            ? attacker.Defenses.Apply(DamagePacket.Weapon(amount.ToString(), DamageType.Fire).Roll(random), DamageBypass.None, rules).Total
            : 0;

        attacker.HitPoints.Take(taken);
        return $"misfire — the {name} bursts: {save}, {taken} fire damage to {attacker.Name}";
    }

    /// <summary>
    /// A fragile weapon on a natural 1: cracked if it was whole, and gone if it was not. Null for
    /// one that came from no item, which has nothing to crack.
    /// </summary>
    private static string? Crack(Creature attacker, WeaponAttack weapon, bool broken)
    {
        var name = ItemName(attacker, weapon);

        if (broken)
        {
            return attacker.Equipment.Destroy(weapon) ? $"the {name} shatters" : null;
        }

        return attacker.Equipment.Break(weapon) ? $"the {name} cracks" : null;
    }

    private static string ItemName(Creature attacker, WeaponAttack weapon) =>
        attacker.Equipment.ItemFor(weapon)?.Name ?? weapon.Name;

    /// <summary>What a broken weapon costs on both rolls.</summary>
    public const int BrokenPenalty = -2;

    private static ModifierStack Broken(Creature attacker, WeaponAttack weapon)
    {
        var stack = new ModifierStack();

        if (attacker.Equipment.IsBroken(weapon))
        {
            stack.Add(BrokenPenalty, BonusType.Untyped, "broken");
        }

        return stack;
    }

    /// <summary>
    /// The wielder's modifiers and the weapon's, resolved together in one pass, plus flanking
    /// when the ground says so. Adding two separate totals would let a Magic Weapon spell and a
    /// +1 sword stack their enhancement bonuses, which the rules forbid.
    /// </summary>
    public static ModifierBreakdown AttackBonus(
        Creature attacker,
        WeaponAttack weapon,
        Creature? target = null,
        Battlefield? field = null,
        int iterativePenalty = 0)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        var feet = target is null ? null : field?.DistanceInFeet(attacker, target);

        return ModifierStack.Combine(
            attacker.AttackModifiers,
            weapon.Attack.Modifiers,
            BaseAttack(attacker),
            SizeOf(attacker),
            Flanking(attacker, target, field),
            AtRange(attacker, weapon, target, field),
            Iterative(iterativePenalty),
            Stance(attacker, !weapon.IsRanged),
            Martial.AttackBonus(attacker, weapon, feet),
            Proficiency.AttackPenalties(attacker, weapon),
            Broken(attacker, weapon),
            Derived(attacker, Martial.AttackAbility(attacker, weapon), modifier => modifier));
    }

    /// <summary>The creature's own skill at arms, read live so a class level lands immediately.</summary>
    private static ModifierStack BaseAttack(Creature attacker)
    {
        var stack = new ModifierStack();

        if (attacker.BaseAttackBonus != 0)
        {
            stack.Add(attacker.BaseAttackBonus, BonusType.Untyped, "Base Attack Bonus");
        }

        return stack;
    }

    /// <summary>
    /// What the creature has chosen to give up. Power Attack is melee only, which is why this
    /// has to happen where the weapon is known rather than on the creature's own stack.
    /// </summary>
    private static ModifierStack Stance(Creature attacker, bool melee)
    {
        var stack = new ModifierStack();
        var penalty = attacker.Stances.AttackPenalty(melee);

        if (penalty != 0)
        {
            stack.Add(penalty, BonusType.Untyped, attacker.Stances.ToString());
        }

        if (attacker.Stances.IsActive(Combat.Stance.SurpriseAccuracy))
        {
            stack.Add(
                attacker.Stances.RageBonus(Combat.Stance.SurpriseAccuracy),
                BonusType.Morale,
                Combat.Stances.Name(Combat.Stance.SurpriseAccuracy));
        }

        return stack;
    }

    /// <summary>What the second and later swings of a full attack give up.</summary>
    private static ModifierStack Iterative(int penalty)
    {
        var stack = new ModifierStack();

        if (penalty != 0)
        {
            stack.Add(penalty, BonusType.Untyped, "Iterative attack");
        }

        return stack;
    }

    /// <summary>What lying down is worth against this particular weapon.</summary>
    public static int ProneFor(WeaponAttack weapon, Creature target)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        ArgumentNullException.ThrowIfNull(target);

        return target.IsProne
            ? weapon.IsRanged ? ProneAgainstRanged : ProneAgainstMelee
            : 0;
    }

    /// <summary>What the ground gives the defender against this particular attacker.</summary>
    public static int CoverFor(Creature attacker, Creature target, Battlefield? field) =>
        field?.HasCover(attacker, target) == true ? CoverBonus : 0;

    /// <summary>
    /// The two things that make a shot harder than a swing: distance, and your own side being in
    /// the way. Both are penalties, so both always apply — there is no "highest penalty wins".
    /// </summary>
    private static ModifierStack AtRange(
        Creature attacker, WeaponAttack weapon, Creature? target, Battlefield? field)
    {
        var stack = new ModifierStack();

        if (!weapon.IsRanged || target is null || field is null)
        {
            return stack;
        }

        if (field.DistanceInFeet(attacker, target) is { } feet
            && weapon.RangePenalty(feet) is var penalty and < 0)
        {
            stack.Add(penalty, BonusType.Untyped, $"Range ({feet} ft)");
        }

        // Precise Shot is the feat that buys the angle a bowman would otherwise walk for.
        if (!attacker.HasFeat(Feats.FeatEffect.PreciseShot)
            && field.SquareOf(target) is { } square
            && field.Creatures.Any(ally =>
                !ReferenceEquals(ally, attacker)
                && ally.IsAllyOf(attacker)
                && field.Threatens(ally, square)))
        {
            stack.Add(IntoMeleePenalty, BonusType.Untyped, "Firing into melee");
        }

        return stack;
    }

    /// <summary>
    /// Two allies on opposite sides are worth +2 each. Worked out at the moment of the swing,
    /// because it depends on where everyone is standing right now.
    /// </summary>
    private static ModifierStack Flanking(Creature attacker, Creature? target, Battlefield? field)
    {
        var stack = new ModifierStack();

        if (target is not null && FlankingPartner(attacker, target, field) is { } partner)
        {
            stack.Add(FlankingBonus, BonusType.Untyped, $"Flanking with {partner.Name}");
        }

        return stack;
    }

    /// <summary>
    /// Whoever the attacker is flanking this target with, if anybody — and nobody at all against
    /// improved uncanny dodge, unless the attacker is enough of a rogue to get past it.
    /// </summary>
    public static Creature? FlankingPartner(Creature attacker, Creature target, Battlefield? field)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        if (field is null || !UncannyDodge.CanBeFlankedBy(target, attacker))
        {
            return null;
        }

        return field.FindFlankingPartner(attacker, target);
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
    /// <param name="feet">How far the target is, for what only counts up close.</param>
    public static ModifierBreakdown DamageBonus(Creature attacker, WeaponAttack weapon, int? feet = null)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(weapon);

        var stance = new ModifierStack();
        var bonus = attacker.Stances.DamageBonus(
            !weapon.IsRanged, twoHanded: weapon.DamageScale == AbilityDamageScale.OneAndAHalf);

        if (bonus != 0)
        {
            stance.Add(bonus, BonusType.Untyped, Combat.Stances.Name(Combat.Stance.PowerAttack));
        }

        if (attacker.Stances.IsActive(Combat.Stance.PowerfulBlow))
        {
            stance.Add(
                attacker.Stances.RageBonus(Combat.Stance.PowerfulBlow),
                BonusType.Untyped,
                Combat.Stances.Name(Combat.Stance.PowerfulBlow));
        }

        return ModifierStack.Combine(
            attacker.DamageModifiers,
            weapon.DamageModifiers,
            stance,
            Martial.DamageBonus(attacker, weapon, feet),
            Broken(attacker, weapon),
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
    /// <remarks>
    /// Always a fresh packet, never the weapon's own: sneak attack and vital strike add
    /// components to what this returns, and the weapon's packet is shared by every swing.
    /// The powerful blow is passed in rather than read from the stance, because the roll that
    /// spent it has already taken it away.
    /// </remarks>
    /// <param name="type">What a "P or S" weapon is being swung as, or null to keep its own.</param>
    private static DamagePacket DamageFor(
        Creature attacker, WeaponAttack weapon, int? feet, int powerful, bool vital, DamageType? type = null)
    {
        var bonus = DamageBonus(attacker, weapon, feet).Total + powerful;
        var packet = Fold(weapon, bonus, type);

        // Vital Strike rolls the weapon's dice again — dice only, and not multiplied on a
        // critical, which is the same shape as precision damage.
        if (vital)
        {
            foreach (var component in weapon.Damage.Components.Where(c => c.MultipliedOnCritical))
            {
                packet.Add(DamageComponent.Extra(component.Amount.DiceOnly(), type ?? component.Type) with
                {
                    Nonlethal = component.Nonlethal,
                });
            }
        }

        return packet;
    }

    private static DamagePacket Fold(WeaponAttack weapon, int bonus, DamageType? type)
    {
        if (bonus == 0 && type is null)
        {
            return new DamagePacket(weapon.Damage.Components);
        }

        var packet = new DamagePacket();
        var folded = false;

        foreach (var component in weapon.Damage.Components)
        {
            if (!folded && component.MultipliedOnCritical)
            {
                packet.Add(new DamageComponent(component.Amount.Plus(bonus), type ?? component.Type, true)
                {
                    Nonlethal = component.Nonlethal,
                });
                folded = true;
            }
            else
            {
                packet.Add(component);
            }
        }

        if (!folded && bonus != 0)
        {
            var first = weapon.Damage.Components.Count > 0
                ? weapon.Damage.Components[0].Type
                : DamageType.Untyped;
            packet.Add(DiceExpression.Constant(bonus), type ?? first);
        }

        return packet;
    }

    /// <summary>
    /// Hands the damage to the target, letting regeneration turn all but a few damage types into
    /// nonlethal. Damage that regeneration cannot absorb also stops it for a round.
    /// </summary>
    /// <param name="total">What actually lands, which a defensive roll may have halved from
    /// <see cref="DamageTaken.Total"/>.</param>
    private static (DamageApplication Applied, int Nonlethal) Apply(Creature target, DamageTaken taken, int total)
    {
        if (target.Effects.Regeneration is not { IsSuspended: false } regeneration)
        {
            return (target.HitPoints.Take(total), 0);
        }

        var lethal = Math.Min(total, taken.Entries
            .Where(entry => regeneration.IsSuspendedBy(entry.Type))
            .Sum(entry => entry.Taken));

        if (lethal > 0)
        {
            regeneration.Suspend();
        }

        var applied = target.HitPoints.Take(lethal);
        var nonlethal = target.HitPoints.TakeNonlethal(total - lethal);

        return (applied, nonlethal);
    }
}
