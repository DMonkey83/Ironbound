using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>
/// Shoving, tripping and wrestling: the attacks that move a creature or change its posture
/// rather than reducing its hit points.
/// </summary>
/// <remarks>
/// One roll against one number — a combat maneuver bonus against a combat manoeuvre defence —
/// which is why the whole family costs so little to add once the first one exists.
/// <para>
/// These are what make position worth fighting over. A fighter who has spent the round standing
/// still to full attack is the ideal person to knock down, because the four points of armour
/// class matter far less than the round they lose getting up. That exchange only exists because
/// the full attack does.
/// </para>
/// </remarks>
public static class Maneuvers
{
    /// <summary>What everybody has before anything is added to it.</summary>
    public const int Base = 10;

    /// <summary>Failing by this much turns the maneuver back on whoever tried it.</summary>
    public const int BacklashMargin = 10;

    /// <summary>What practising one maneuver in particular is worth.</summary>
    public const int ImprovedBonus = 2;

    /// <summary>Which feat makes this maneuver safer and surer, if any does.</summary>
    public static FeatEffect ImprovedBy(ManeuverKind kind) => kind switch
    {
        ManeuverKind.Trip => FeatEffect.ImprovedTrip,
        ManeuverKind.BullRush => FeatEffect.ImprovedBullRush,
        _ => FeatEffect.None,
    };

    /// <summary>
    /// The bonus to a maneuver check: skill at arms, strength, and how much of you there is.
    /// </summary>
    /// <remarks>
    /// <see cref="Creature.AttackModifiers"/> is included, which is a deliberate reading rather
    /// than the letter of the rules. It means being shaken or sickened makes you worse at
    /// tripping people, and Bless makes you better — both of which are what anyone would expect,
    /// and the alternative is a second parallel stack that every buff has to remember to feed.
    /// </remarks>
    public static ModifierBreakdown Bonus(Creature creature) => Bonus(creature, null);

    /// <summary>
    /// The bonus for one particular maneuver, which is two better if the creature has practised
    /// this one specifically.
    /// </summary>
    public static ModifierBreakdown Bonus(Creature creature, ManeuverKind? kind)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var innate = new ModifierStack();

        if (kind is { } attempting && creature.HasFeat(ImprovedBy(attempting)) )
        {
            innate.Add(ImprovedBonus, BonusType.Untyped, $"Improved {attempting}");
        }

        if (creature.BaseAttackBonus != 0)
        {
            innate.Add(creature.BaseAttackBonus, BonusType.Untyped, "Base Attack Bonus");
        }

        var strength = creature.Abilities[Ability.Strength].Modifier;
        if (strength != 0)
        {
            innate.Add(strength, BonusType.Untyped, "Str");
        }

        var size = CreatureSizes.ManeuverModifier(creature.Size);
        if (size != 0)
        {
            innate.Add(size, BonusType.Size, "Size");
        }

        // A trip is made with the weapon in hand, so a fighter trained in its group is better at
        // it — and somebody never taught to use it is worse. A bull rush is made with the whole
        // body and gets nothing from the blade, either way.
        var tripping = kind == ManeuverKind.Trip ? creature.MeleeAttack : null;

        if (tripping is not null
            && Classes.Martial.WeaponTraining(creature, tripping) is > 0 and var trained)
        {
            innate.Add(trained, BonusType.Untyped, "Weapon training");
        }

        // Declared before the check: the barbarian's whole level on this one.
        if (kind is not null && creature.Stances.IsActive(Stance.StrengthSurge))
        {
            innate.Add(
                creature.Stances.RageBonus(Stance.StrengthSurge),
                BonusType.Untyped,
                Stances.Name(Stance.StrengthSurge));
        }

        // Armour worn untrained hampers a manoeuvre as it does a swing.
        return ModifierStack.Combine(innate, creature.AttackModifiers, Classes.Proficiency.AttackPenalties(creature, tripping));
    }

    /// <summary>
    /// The number to beat: ten, plus everything that makes a creature hard to shift.
    /// </summary>
    /// <remarks>
    /// Note that Dexterity counts here, and is lost for the same reasons it is lost from armour
    /// class — someone stunned or caught flat-footed is far easier to put on the floor.
    /// </remarks>
    public static ModifierBreakdown DefenseBonus(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var innate = new ModifierStack();

        // The base ten belongs in the breakdown, exactly as it does for armour class. Left out,
        // a sheet shows a total of 23 above a sum that reads 13, and the reader is right to
        // distrust both.
        innate.Add(Base, BonusType.Untyped, "Base");

        if (creature.BaseAttackBonus != 0)
        {
            innate.Add(creature.BaseAttackBonus, BonusType.Untyped, "Base Attack Bonus");
        }

        Contribute(innate, creature, Ability.Strength);

        // A denied Dexterity loses only its *bonus*. A penalty still counts, exactly as it does
        // for armour class: being clumsy does not stop hurting because you were caught off guard.
        var dexterity = creature.Abilities[Ability.Dexterity].Modifier;
        if (!creature.DeniesDexterity || dexterity < 0)
        {
            Contribute(innate, creature, Ability.Dexterity);
        }

        var size = CreatureSizes.ManeuverModifier(creature.Size);
        if (size != 0)
        {
            innate.Add(size, BonusType.Size, "Size");
        }

        // Deflection and dodge are the armour-class bonuses that also resist a shove; armour
        // and natural armour are not, since neither helps you keep your feet. TotalWhere filters
        // before stacking, so a bypassed bonus cannot suppress a smaller one that does count.
        var warding = creature.ArmorClass.Modifiers.TotalWhere(Deflection);
        if (warding != 0)
        {
            innate.Add(warding, BonusType.Untyped, "Deflection and dodge");
        }

        return ModifierStack.Combine(innate);
    }

    /// <summary>Base ten plus the bonuses. The single number a maneuver is rolled against.</summary>
    public static int Defense(Creature creature) => DefenseBonus(creature).Total;

    /// <summary>
    /// Rolls one maneuver. No natural-twenty floor: a maneuver check is not an attack roll, so
    /// the mercy that keeps a high-armour boss reachable does not extend to shoving it over.
    /// </summary>
    public static ManeuverResult Attempt(
        Creature attacker, Creature target, IRandomSource random, ManeuverKind kind)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var bonus = Bonus(attacker, kind);
        var defense = Defense(target);
        var natural = random.NextDie(Attack.DieSides);

        // Whatever was declared for this check is used by it, success or not.
        attacker.Stances.Spend(Stance.StrengthSurge);

        return new ManeuverResult
        {
            Kind = kind,
            Attacker = attacker,
            Target = target,
            NaturalRoll = natural,
            Bonus = bonus,
            Defense = defense,
        };
    }

    private static void Contribute(ModifierStack stack, Creature creature, Ability ability)
    {
        var modifier = creature.Abilities[ability].Modifier;
        if (modifier != 0)
        {
            stack.Add(modifier, BonusType.Untyped, AbilityInfo.Abbreviate(ability));
        }
    }

    /// <summary>Deflection and dodge are the armour-class bonuses that also resist a shove.</summary>
    private static bool Deflection(Modifier modifier) =>
        modifier.Type is BonusType.Deflection or BonusType.Dodge;
}

/// <summary>Which maneuver was attempted. Only what the log and the rules need to tell apart.</summary>
public enum ManeuverKind
{
    Trip,
    BullRush,
}

/// <summary>One maneuver check, with everything needed to explain it.</summary>
public sealed record ManeuverResult
{
    public required ManeuverKind Kind { get; init; }

    public required Creature Attacker { get; init; }

    public required Creature Target { get; init; }

    public required int NaturalRoll { get; init; }

    public required ModifierBreakdown Bonus { get; init; }

    public required int Defense { get; init; }

    public int Total => NaturalRoll + Bonus.Total;

    /// <summary>How far over — or under — the defence the check landed.</summary>
    public int Margin => Total - Defense;

    public bool Succeeded => Margin >= 0;

    /// <summary>Missed by ten or more, which turns a trip back on the one who tried it.</summary>
    public bool Backfired => Margin <= -Maneuvers.BacklashMargin;

    /// <summary>How many extra five-foot increments a bull rush pushed, beyond the first.</summary>
    public int ExtraIncrements => Succeeded ? Margin / 5 : 0;

    public override string ToString()
    {
        var bonus = Bonus.Total.ToString("+0;-0;+0");
        var verdict = Succeeded ? "success" : Backfired ? "failure, badly" : "failure";

        return $"{Attacker.Name} attempts to {Describe(Kind)} {Target.Name}: "
            + $"d20 [{NaturalRoll}] {bonus} = {Total} vs CMD {Defense} — {verdict}";
    }

    private static string Describe(ManeuverKind kind) => kind switch
    {
        ManeuverKind.BullRush => "bull rush",
        _ => "trip",
    };
}
