using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Classes;

/// <summary>
/// Who has been trained to use what: weapons by category or by name, armour by weight, shields.
/// </summary>
/// <remarks>
/// Asked live, the way everything else about a creature is: a class level, a feat or a new god
/// changes the answer on the spot, and nothing has to be kept in step. Training comes from five
/// places — the classes, a cleric's god, the creature's people, its feats, and for a creature with
/// no class at all, its own stat block — and any one of them is enough.
/// <para>
/// Two kinds of attack are never asked about. A natural attack needs no training. An attack built
/// by hand, with no catalogue kind, has nothing to look up: the tests and the odd scripted creature
/// that make them are describing a creature that knows how to use what it holds.
/// </para>
/// </remarks>
public static class Proficiency
{
    /// <summary>What swinging a weapon you were never taught costs on every attack roll.</summary>
    public const int NonProficientPenalty = -4;

    /// <summary>"simple" in a class's weapon list: every simple weapon.</summary>
    public const string SimpleWeapons = "simple";

    /// <summary>"martial": every martial weapon.</summary>
    public const string MartialWeapons = "martial";

    /// <summary>"deity": a cleric's god's favoured weapon, whichever god that turns out to be.</summary>
    public const string DeityWeapon = "deity";

    public const string LightArmour = "light";

    public const string MediumArmour = "medium";

    public const string HeavyArmour = "heavy";

    public const string Shields = "shields";

    public const string TowerShields = "tower-shield";

    /// <summary>Every word a class's armour list may use.</summary>
    public static IReadOnlyList<string> ArmourWords { get; } =
        [LightArmour, MediumArmour, HeavyArmour, Shields, TowerShields];

    /// <summary>The source name the penalty goes by, which is what the sheet's breakdown shows.</summary>
    public const string NotProficient = "not proficient";

    /// <summary>Whether a creature has been trained to fight with this weapon.</summary>
    public static bool IsProficient(Creature creature, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(weapon);

        if (weapon.Kind is not { } kind || weapon.Category == WeaponCategory.Natural)
        {
            return true;
        }

        var race = creature.Race;
        var category = race?.CategoryOf(weapon) ?? weapon.Category;

        if (race?.IsFamiliarWith(kind) == true || ByFeat(creature, category, kind))
        {
            return true;
        }

        // The Bestiary: simple weapons, and whatever the stat block hands it. A creature built by
        // hand rather than from a file has no stat block to hold it to.
        if (creature.Levels.Count == 0)
        {
            return creature.DefinitionId is null
                || category == WeaponCategory.Simple
                || creature.NativeGear.Contains(kind);
        }

        foreach (var taken in creature.Levels)
        {
            foreach (var word in taken.Class.WeaponProficiencies)
            {
                var covers = word switch
                {
                    SimpleWeapons => category == WeaponCategory.Simple,
                    MartialWeapons => category is WeaponCategory.Simple or WeaponCategory.Martial,
                    DeityWeapon => creature.Choices.Deity is { } god
                        && string.Equals(god.FavoredWeapon, kind, StringComparison.Ordinal),
                    _ => string.Equals(word, kind, StringComparison.Ordinal),
                };

                if (covers)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a creature has been trained to wear this armour or carry this shield. Anything
    /// that is neither needs no training.
    /// </summary>
    /// <summary>
    /// Whether a creature is proficient with a kind of weapon it carries, by the weapon's id —
    /// what Weapon Focus and Improved Critical ask of the weapon they are taken for.
    /// </summary>
    /// <remarks>
    /// Answered from the weapons in its hands, because a kind is only a word until there is a
    /// weapon to look up its category. A kind it carries nothing of is let through: feats are
    /// taken for what the creature carries, and the one who picks a weapon off the floor later
    /// is asked again when she swings it.
    /// </remarks>
    public static bool IsProficientWithKind(Creature creature, string kind)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(kind);

        var carried = creature.Attacks.FirstOrDefault(weapon => string.Equals(weapon.Kind, kind, StringComparison.Ordinal));
        return carried is null || IsProficient(creature, carried);
    }

    public static bool IsProficient(Creature creature, ItemDefinition armourOrShield)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(armourOrShield);

        if (WordFor(armourOrShield) is not { } word)
        {
            return true;
        }

        if (creature.HasFeat(FeatFor(armourOrShield)))
        {
            return true;
        }

        if (creature.Levels.Count == 0)
        {
            return creature.DefinitionId is null || creature.NativeGear.Contains(armourOrShield.Id);
        }

        return creature.Levels.Any(taken => taken.Class.ArmourProficiencies.Contains(word, StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether a creature is trained in a kind of armour, named as a class's list names it —
    /// "light", "shields" — from its classes or the feat for it. What armour feats ask for.
    /// </summary>
    public static bool IsTrainedIn(Creature creature, string word)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var feat = word switch
        {
            LightArmour => FeatEffect.ArmorProficiencyLight,
            MediumArmour => FeatEffect.ArmorProficiencyMedium,
            HeavyArmour => FeatEffect.ArmorProficiencyHeavy,
            Shields => FeatEffect.ShieldProficiency,
            TowerShields => FeatEffect.TowerShieldProficiency,
            _ => FeatEffect.None,
        };

        return creature.HasFeat(feat)
            || creature.Levels.Any(taken => taken.Class.ArmourProficiencies.Contains(word, StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether taking a proficiency feat would make the creature able to use something it
    /// carries and cannot use now. The automatic pick and the level-up screen's defaults ask this
    /// before reaching for one, so a fighter is never handed Light Armor Proficiency for nothing.
    /// </summary>
    public static bool WouldHelp(Creature creature, FeatDefinition feat)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(feat);

        switch (feat.Effect)
        {
            case FeatEffect.SimpleWeaponProficiency:
            case FeatEffect.MartialWeaponProficiency:
            case FeatEffect.ExoticWeaponProficiency:
                return creature.Attacks.Any(weapon =>
                    FeatCovers(feat, weapon)
                    && (feat.Choice is null || string.Equals(feat.Choice, weapon.Kind, StringComparison.Ordinal))
                    && !IsProficient(creature, weapon));

            case FeatEffect.ArmorProficiencyLight:
            case FeatEffect.ArmorProficiencyMedium:
            case FeatEffect.ArmorProficiencyHeavy:
            case FeatEffect.ShieldProficiency:
            case FeatEffect.TowerShieldProficiency:
                return creature.Equipment.Items.Any(item =>
                    FeatFor(item) == feat.Effect && !IsProficient(creature, item));

            default:
                return false;
        }
    }

    /// <summary>
    /// What a creature is trained with, in a line for its sheet: "Trained with simple and martial
    /// weapons; light and medium armour, shields". Empty for a creature with no class, whose stat
    /// block is its training.
    /// </summary>
    public static string Describe(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (creature.Levels.Count == 0)
        {
            return string.Empty;
        }

        var weapons = creature.Levels
            .SelectMany(taken => taken.Class.WeaponProficiencies)
            .Select(word => word switch
            {
                DeityWeapon => creature.Choices.Deity is { } god ? god.FavoredWeapon.Replace('-', ' ') : null,
                _ => word.Replace('-', ' '),
            })
            .OfType<string>()
            .Concat(creature.Race?.FamiliarWeapons.Select(id => id.Replace('-', ' ')) ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var armour = ArmourWords
            .Where(word => IsTrainedIn(creature, word))
            .Select(word => word switch
            {
                Shields => "shields",
                TowerShields => "tower shields",
                _ => $"{word} armour",
            })
            .ToList();

        var categories = weapons.Where(word => word is SimpleWeapons or MartialWeapons).ToList();
        var named = weapons.Where(word => word is not (SimpleWeapons or MartialWeapons)).ToList();
        if (categories.Count > 0)
        {
            named.Insert(0, $"{Join(categories)} weapons");
        }

        var weaponText = named.Count == 0 ? "no weapons" : Join(named);
        var armourText = armour.Count == 0 ? "no armour" : Join(armour);

        return $"Trained with {weaponText}; {armourText}";
    }

    private static string Join(IReadOnlyList<string> words) =>
        words.Count == 1 ? words[0] : $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}";

    /// <summary>The word a class's armour list would have to use to cover this item, or null for no armour.</summary>
    public static string? WordFor(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Armour switch
        {
            ArmourCategory.Light => LightArmour,
            ArmourCategory.Medium => MediumArmour,
            ArmourCategory.Heavy => HeavyArmour,
            ArmourCategory.Shield => item.TowerShield ? TowerShields : Shields,
            _ => null,
        };
    }

    /// <summary>The feat that teaches this armour or shield.</summary>
    public static FeatEffect FeatFor(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.Armour switch
        {
            ArmourCategory.Light => FeatEffect.ArmorProficiencyLight,
            ArmourCategory.Medium => FeatEffect.ArmorProficiencyMedium,
            ArmourCategory.Heavy => FeatEffect.ArmorProficiencyHeavy,
            ArmourCategory.Shield => item.TowerShield ? FeatEffect.TowerShieldProficiency : FeatEffect.ShieldProficiency,
            _ => FeatEffect.None,
        };
    }

    /// <summary>
    /// Everything training costs one attack roll with one weapon: four for a weapon nobody taught
    /// the creature to use, and the check penalty of any armour or shield it was never taught to
    /// wear — the price of moving in plates one does not know how to move in.
    /// </summary>
    public static ModifierStack AttackPenalties(Creature creature, WeaponAttack? weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stack = new ModifierStack();

        if (weapon is not null && !IsProficient(creature, weapon))
        {
            stack.Add(NonProficientPenalty, BonusType.Untyped, NotProficient);
        }

        foreach (var (item, penalty) in UntrainedArmour(creature))
        {
            stack.Add(penalty, BonusType.Untyped, $"{NotProficient} with {item.Name}");
        }

        return stack;
    }

    /// <summary>
    /// Each worn piece of armour or shield the creature is not proficient with, and what its check
    /// penalty comes to on her — armour training eases the body armour's share, as it does
    /// everywhere else.
    /// </summary>
    public static IEnumerable<(ItemDefinition Item, int Penalty)> UntrainedArmour(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var entry in creature.Equipment.Worn.Where(entry => entry.IsWorn && entry.Item.Armour != ArmourCategory.None))
        {
            var item = entry.Item;
            if (IsProficient(creature, item))
            {
                continue;
            }

            var penalty = item.IsBodyArmour
                ? Math.Min(0, item.CheckPenalty + Martial.ArmorTraining(creature))
                : item.CheckPenalty;

            if (penalty < 0)
            {
                yield return (item, penalty);
            }
        }
    }

    /// <summary>
    /// Whether a proficiency feat taken for this weapon would make the creature proficient — what
    /// the level-up screen and the automatic pick ask before offering one.
    /// </summary>
    public static bool FeatCovers(FeatDefinition feat, WeaponAttack weapon)
    {
        ArgumentNullException.ThrowIfNull(feat);
        ArgumentNullException.ThrowIfNull(weapon);

        return feat.Effect switch
        {
            FeatEffect.SimpleWeaponProficiency => weapon.Category == WeaponCategory.Simple,
            FeatEffect.MartialWeaponProficiency => weapon.Category == WeaponCategory.Martial,
            FeatEffect.ExoticWeaponProficiency => weapon.Category == WeaponCategory.Exotic,
            _ => false,
        };
    }

    /// <summary>Whether a feat is one of the proficiency feats, weapon or armour.</summary>
    public static bool IsProficiencyFeat(FeatEffect effect) => effect is
        FeatEffect.SimpleWeaponProficiency or FeatEffect.MartialWeaponProficiency
        or FeatEffect.ExoticWeaponProficiency or FeatEffect.ArmorProficiencyLight
        or FeatEffect.ArmorProficiencyMedium or FeatEffect.ArmorProficiencyHeavy
        or FeatEffect.ShieldProficiency or FeatEffect.TowerShieldProficiency;

    private static bool ByFeat(Creature creature, WeaponCategory category, string kind) =>
        creature.Feats.Any(feat => feat.Effect switch
        {
            FeatEffect.SimpleWeaponProficiency => category == WeaponCategory.Simple,
            FeatEffect.MartialWeaponProficiency or FeatEffect.ExoticWeaponProficiency =>
                string.Equals(feat.Choice, kind, StringComparison.Ordinal),
            _ => false,
        });
}
