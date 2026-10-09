using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Classes;

/// <summary>
/// What training does to a weapon in somebody's hands and to the armour on their back: the
/// fighter's weapon and armour training, and the feats written against one kind of weapon.
/// </summary>
/// <remarks>
/// All of it is per weapon or per attack, which is why none of it can live on the creature's
/// own modifier stacks: Weapon Focus (longsword) is worth nothing to the same fighter's bow.
/// <see cref="Strike"/> asks here with the weapon in hand, the same way it asks the stances.
/// </remarks>
public static class Martial
{
    /// <summary>
    /// Every fighter weapon group, by the ids weapon files use: the Core Rulebook's, then the
    /// tribal group the Advanced Player's Guide added and the firearms and siege engines groups
    /// of Ultimate Combat, which the catalogue's guns and engines need.
    /// </summary>
    public static IReadOnlyList<string> WeaponGroups { get; } =
    [
        "axes", "heavy-blades", "light-blades", "bows", "close", "crossbows", "double",
        "flails", "hammers", "monk", "natural", "polearms", "spears", "thrown",
        "tribal", "firearms", "siege-engines",
    ];

    /// <summary>"heavy-blades" as a sheet would say it: "heavy blades".</summary>
    public static string GroupName(string group) => group.Replace('-', ' ');

    /// <summary>Weapon Focus, and the Weapon Specialization that builds on it, are worth this.</summary>
    public const int FocusBonus = 1;

    /// <summary>Point-Blank Shot's range, and its bonus.</summary>
    public const int PointBlankFeet = 30;

    public const int PointBlankBonus = 1;

    /// <summary>Shield Focus's one point.</summary>
    public const int ShieldFocusBonus = 1;

    /// <summary>Mobility's dodge bonus against attacks of opportunity drawn by moving.</summary>
    public const int MobilityBonus = 4;

    /// <summary>What a blocking weapon adds while its wielder fights defensively.</summary>
    public const int BlockingBonus = 1;

    /// <summary>Bravery: one against fear at second level, one more every four levels after.</summary>
    public static int Bravery(Creature creature) => ClassFeatures.Rank(creature, FeatureIds.Bravery);

    /// <summary>How many steps of armour training: one at third, two at seventh.</summary>
    public static int ArmorTraining(Creature creature) => ClassFeatures.Rank(creature, FeatureIds.ArmorTraining);

    /// <summary>
    /// The weapon groups actually trained in, first pick first. A file can list more groups than
    /// the fighter has reached; only as many as the table has handed out count.
    /// </summary>
    public static IReadOnlyList<string> TrainedGroups(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return [.. creature.Choices.WeaponGroups.Take(ClassFeatures.Rank(creature, FeatureIds.WeaponTraining))];
    }

    /// <summary>
    /// Weapon training's bonus in one group: the first group picked is worth one for every pick
    /// made since, so at ninth level the first group is +2 and the second +1.
    /// </summary>
    public static int WeaponTraining(Creature creature, string group)
    {
        var trained = TrainedGroups(creature);
        var index = trained.ToList().IndexOf(group);
        return index < 0 ? 0 : trained.Count - index;
    }

    /// <summary>Weapon training with this particular weapon: its best group.</summary>
    public static int WeaponTraining(Creature creature, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        return weapon.Groups.Select(group => WeaponTraining(creature, group)).DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether a feat taken for one kind of weapon was taken for this one.</summary>
    public static bool HasFeatFor(Creature creature, FeatEffect effect, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(weapon);

        return weapon.Kind is { } kind
            && creature.Feats.Any(feat => feat.Effect == effect && feat.Choice == kind);
    }

    /// <summary>
    /// The ability that aims this weapon for this wielder. Weapon Finesse swaps Strength for
    /// Dexterity on a finesse weapon, and only when Dexterity is the better of the two — taking
    /// the feat never makes a strong character worse.
    /// </summary>
    public static Ability? AttackAbility(Creature creature, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(weapon);

        if (weapon.AttackAbility == Ability.Strength
            && weapon.Finesse
            && !weapon.IsRanged
            && creature.HasFeat(FeatEffect.WeaponFinesse)
            && creature.Abilities[Ability.Dexterity].Modifier > creature.Abilities[Ability.Strength].Modifier)
        {
            return Ability.Dexterity;
        }

        return weapon.AttackAbility;
    }

    /// <summary>
    /// Everything training adds to one attack roll with one weapon: weapon training, Weapon
    /// Focus, Point-Blank Shot at close range.
    /// </summary>
    /// <param name="feet">How far away the target is, when that is known.</param>
    public static ModifierStack AttackBonus(Creature creature, WeaponAttack weapon, int? feet)
    {
        var stack = new ModifierStack();

        if (WeaponTraining(creature, weapon) is > 0 and var trained)
        {
            stack.Add(trained, BonusType.Untyped, "Weapon training");
        }

        if (HasFeatFor(creature, FeatEffect.WeaponFocus, weapon))
        {
            stack.Add(FocusBonus, BonusType.Untyped, "Weapon Focus");
        }

        // Greater Weapon Focus is taken for the weapon Weapon Focus was, and adds to it.
        if (HasFeatFor(creature, FeatEffect.GreaterWeaponFocus, weapon))
        {
            stack.Add(FocusBonus, BonusType.Untyped, "Greater Weapon Focus");
        }

        if (IsPointBlank(creature, weapon, feet))
        {
            stack.Add(PointBlankBonus, BonusType.Untyped, "Point-Blank Shot");
        }

        return stack;
    }

    /// <summary>Weapon Specialization's two, and Greater Weapon Specialization's two more.</summary>
    public const int SpecializationBonus = 2;

    /// <summary>
    /// The same for damage: weapon training, Weapon Specialization in this weapon, Point-Blank
    /// Shot, and an Arcane Strike still running.
    /// </summary>
    public static ModifierStack DamageBonus(Creature creature, WeaponAttack weapon, int? feet)
    {
        var stack = new ModifierStack();

        if (WeaponTraining(creature, weapon) is > 0 and var trained)
        {
            stack.Add(trained, BonusType.Untyped, "Weapon training");
        }

        if (HasFeatFor(creature, FeatEffect.WeaponSpecialization, weapon))
        {
            stack.Add(SpecializationBonus, BonusType.Untyped, "Weapon Specialization");
        }

        if (HasFeatFor(creature, FeatEffect.GreaterWeaponSpecialization, weapon))
        {
            stack.Add(SpecializationBonus, BonusType.Untyped, "Greater Weapon Specialization");
        }

        if (IsPointBlank(creature, weapon, feet))
        {
            stack.Add(PointBlankBonus, BonusType.Untyped, "Point-Blank Shot");
        }

        if (Encounters.Actions.ArcaneStrikeAction.BonusOf(creature) is > 0 and var arcane)
        {
            stack.Add(arcane, BonusType.Untyped, Encounters.Actions.ArcaneStrikeAction.EffectName);
        }

        return stack;
    }

    /// <summary>What Critical Focus adds to the roll that confirms a critical.</summary>
    public const int CriticalFocusBonus = 4;

    /// <summary>What is added to a confirmation roll alone: Critical Focus, with any weapon.</summary>
    public static int ConfirmationBonus(Creature creature) =>
        creature.HasFeat(FeatEffect.CriticalFocus) ? CriticalFocusBonus : 0;

    /// <summary>
    /// The damage reduction a blow with this weapon ignores: five with Penetrating Strike and
    /// ten with the greater feat, either only with a weapon its wielder has Weapon Focus in.
    /// </summary>
    public static int Penetration(Creature creature, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(weapon);

        if (!HasFeatFor(creature, FeatEffect.WeaponFocus, weapon))
        {
            return 0;
        }

        return creature.HasFeat(FeatEffect.GreaterPenetratingStrike) ? 10
            : creature.HasFeat(FeatEffect.PenetratingStrike) ? 5
            : 0;
    }

    /// <summary>
    /// How many sets of the weapon's dice a vital strike adds: one for the feat, two with the
    /// improved one, three with the greater — twice, three and four times the dice in all.
    /// </summary>
    public static int VitalStrikeSets(Creature creature) =>
        creature.HasFeat(FeatEffect.GreaterVitalStrike) ? 3
            : creature.HasFeat(FeatEffect.ImprovedVitalStrike) ? 2
            : creature.HasFeat(FeatEffect.VitalStrike) ? 1
            : 0;

    private static bool IsPointBlank(Creature creature, WeaponAttack weapon, int? feet) =>
        weapon.IsRanged && feet is <= PointBlankFeet && creature.HasFeat(FeatEffect.PointBlankShot);

    /// <summary>The weapon's critical profile in this wielder's hands: doubled by Improved Critical.</summary>
    public static CriticalProfile Critical(Creature creature, WeaponAttack weapon) =>
        HasFeatFor(creature, FeatEffect.ImprovedCritical, weapon)
            ? weapon.Attack.Critical.Widened()
            : weapon.Attack.Critical;

    /// <summary>
    /// The check penalty of everything worn, with armour training taking a point off the body
    /// armour's share for each step. A shield's penalty is the shield's, trained or not.
    /// </summary>
    public static int CheckPenalty(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var (armour, shield) = creature.Equipment.CheckPenalties;
        var eased = Math.Min(0, armour + ArmorTraining(creature));

        return eased + shield;
    }

    /// <summary>
    /// What medium or heavy armour does to a creature's speed: thirty becomes twenty, twenty
    /// fifteen. Armour training lifts it for medium armour at the first step and heavy at the
    /// second.
    /// </summary>
    public static int ArmouredSpeed(Creature creature, int speed)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var worn = creature.Equipment.ArmourWorn;
        var training = ArmorTraining(creature);

        var slowed = worn switch
        {
            ArmourCategory.Medium => training < 1,
            ArmourCategory.Heavy => training < 2,
            _ => false,
        };

        return slowed ? Reduced(speed) : speed;
    }

    /// <summary>
    /// The armour table's speeds. The common ones are the book's own; anything else is two
    /// thirds, rounded down to a square, which is what the table is the shape of.
    /// </summary>
    public static int Reduced(int speed) => speed switch
    {
        <= 5 => speed,
        15 => 10,
        20 => 15,
        30 => 20,
        40 => 30,
        50 => 35,
        60 => 40,
        _ => Math.Max(5, speed * 2 / 3 / 5 * 5),
    };

    /// <summary>
    /// What a creature's armour class gains from what it is defending against: Shield Focus
    /// against anything a shield stops, a barbarian's guarded stance against blades alone.
    /// </summary>
    public static IEnumerable<Modifier> Situational(Creature creature, DefenseOptions options)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var touch = (options & DefenseOptions.TouchAttack) != 0;
        var denied = (options & DefenseOptions.DexterityDenied) != 0;

        // Only a shield that is being used: one hanging behind a greataxe is focused on in vain.
        if (!touch && creature.Equipment.ShieldInUse && creature.HasFeat(FeatEffect.ShieldFocus))
        {
            yield return new Modifier(ShieldFocusBonus, BonusType.Shield, "Shield Focus");

            if (creature.HasFeat(FeatEffect.GreaterShieldFocus))
            {
                yield return new Modifier(ShieldFocusBonus, BonusType.Shield, "Greater Shield Focus");
            }
        }

        // Mobility: four of dodge against the swing that walking past somebody draws — lost,
        // as dodge is, with Dexterity.
        if ((options & DefenseOptions.Moving) != 0 && !denied && creature.HasFeat(FeatEffect.Mobility))
        {
            yield return new Modifier(MobilityBonus, BonusType.Dodge, "Mobility");
        }

        // A blocking weapon earns its name only while its wielder fights defensively with it:
        // total defence is not fighting with it at all. The book makes it a shield bonus, so it
        // is no help against a touch and does not add to a real shield in the other hand.
        if (!touch
            && creature.Stances.IsActive(Stance.FightingDefensively)
            && creature.MeleeAttack is { } weapon
            && weapon.Has(WeaponSpecial.Blocking))
        {
            yield return new Modifier(BlockingBonus, BonusType.Shield, "Blocking");
        }

        // A dodge bonus, so it is lost with Dexterity like any other.
        if ((options & DefenseOptions.Melee) != 0
            && !denied
            && Rage.IsRaging(creature)
            && creature.Effects.Has(ClassPowers.GuardedStanceLabel))
        {
            yield return new Modifier(ClassPowers.GuardedStanceBonus(creature), BonusType.Dodge, "Guarded Stance");
        }
    }
}
