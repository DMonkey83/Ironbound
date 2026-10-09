using Ironbound.Rules.Abilities;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;

namespace Ironbound.Rules.Classes;

/// <summary>
/// The choices a level hands out: what is on offer, what a sensible default is, whether a choice
/// is allowed, and taking it.
/// </summary>
/// <remarks>
/// Offers are worked out against the creature as it stands before the level, the same way the
/// general feat already is. A feat whose prerequisite the level itself would meet is therefore a
/// level late; the alternative is levelling first and undoing it when the choice turns out to be
/// illegal, and the rules have no undo.
/// </remarks>
public static class ClassLevelling
{
    /// <summary>
    /// The combat feats worth having first, in order, when nobody is choosing. A fighter's
    /// staples before anything situational.
    /// </summary>
    private static readonly string[] CombatPreference =
    [
        "weapon-focus", "power-attack", "dodge", "improved-initiative", "weapon-finesse",
        "point-blank-shot", "shield-focus", "combat-reflexes", "cleave", "vital-strike",
        "precise-shot", "improved-critical", "rapid-shot",
    ];

    private static readonly string[] WizardPreference = ["empower-spell"];

    private static readonly TalentEffect[] RoguePreference =
    [
        TalentEffect.FinesseRogue, TalentEffect.WeaponTraining, TalentEffect.BleedingAttack,
        TalentEffect.SurpriseAttack, TalentEffect.Resiliency, TalentEffect.SlowReactions,
        TalentEffect.CripplingStrike, TalentEffect.ImprovedEvasion, TalentEffect.Opportunist,
        TalentEffect.DefensiveRoll, TalentEffect.CombatTrick,
    ];

    private static readonly TalentEffect[] RagePreference =
    [
        TalentEffect.PowerfulBlow, TalentEffect.SurpriseAccuracy, TalentEffect.StrengthSurge,
        TalentEffect.RenewedVigor, TalentEffect.Superstition, TalentEffect.SwiftFoot,
        TalentEffect.GuardedStance, TalentEffect.Knockback,
    ];

    /// <summary>The level the creature would be in this class after taking one more.</summary>
    public static int NextLevelIn(Creature creature, ClassDefinition taken) =>
        ClassFeatures.ClassLevel(creature, taken.Id) + 1;

    /// <summary>What the next level in a class asks to be chosen.</summary>
    public static LevelNeeds NeedsFor(Creature creature, ClassDefinition taken, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(library);

        var level = NextLevelIn(creature, taken);
        var rows = taken.FeaturesGainedAt(level).ToList();

        var feats = new List<FeatDefinition>();
        foreach (var row in rows.Where(row => row.Id == FeatureIds.BonusFeat))
        {
            feats.AddRange(BonusFeatsOf(creature, library, row.Parameter("kind", FeatureIds.CombatFeat)));
        }

        var groups = rows.Any(row => row.Id == FeatureIds.WeaponTraining)
            ? Martial.WeaponGroups.Where(group => !creature.Choices.WeaponGroups.Contains(group)).ToList()
            : [];

        var talents = new List<(string Id, string Name)>();
        foreach (var row in rows.Where(row => row.Id == FeatureIds.Talent))
        {
            talents.AddRange(TalentsOf(creature, taken, level, library, row.Parameter("list"))
                .Select(talent => (talent.Id, talent.Name)));
        }

        return new LevelNeeds(
            [.. feats.DistinctBy(feat => feat.Id).OrderBy(feat => feat.Name, StringComparer.Ordinal)],
            groups,
            [.. talents.Distinct()]);
    }

    /// <summary>The feats a bonus-feat row of this kind offers: combat feats, or a wizard's.</summary>
    public static IEnumerable<FeatDefinition> BonusFeatsOf(Creature creature, ContentLibrary library, string kind) =>
        library.FeatIds
            .Select(library.GetFeat)
            .OfType<FeatDefinition>()
            .Where(feat => kind == FeatureIds.WizardFeat
                ? feat.Metamagic || feat.ItemCreation || feat.Id == "spell-mastery"
                : feat.Combat)
            .Where(feat => feat.AvailableTo(creature));

    /// <summary>
    /// The talents of one list that could be taken at a given level in the class: not already
    /// held, prerequisites met, and advanced ones only once advanced talents have arrived.
    /// </summary>
    public static IEnumerable<TalentDefinition> TalentsOf(
        Creature creature, ClassDefinition taken, int level, ContentLibrary library, string list)
    {
        var advanced = taken.FeaturesAt(level).Any(row => row.Id == FeatureIds.AdvancedTalents)
            || ClassFeatures.Has(creature, FeatureIds.AdvancedTalents);

        return library.Talents
            .Where(talent => talent.List == list)
            .Where(talent => !creature.Choices.HasTalent(talent.Id))
            .Where(talent => advanced || !talent.Advanced)
            .Where(talent => talent.MinimumLevel <= level)
            .Where(talent => talent.Requires.All(creature.Choices.HasTalent))
            .OrderBy(talent => talent.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Fills in whatever the level needs and the caller left empty, and checks what the caller
    /// did choose. Null, with a reason, when a choice is not allowed.
    /// </summary>
    public static (LevelChoices? Choices, string? Refusal) Resolve(
        Creature creature, ClassDefinition taken, LevelChoices given, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(library);

        var needs = NeedsFor(creature, taken, library);
        var level = NextLevelIn(creature, taken);
        var lists = taken.FeaturesGainedAt(level)
            .Where(row => row.Id == FeatureIds.Talent)
            .Select(row => row.Parameter("list"))
            .ToList();

        // ---- talent first, because a combat trick decides whether a bonus feat is owed ----
        TalentDefinition? talent = null;
        if (given.Talent is { } wanted)
        {
            if (!needs.Talents.Any(offered => offered.Id == wanted) || library.GetTalent(wanted) is not { } found)
            {
                return (null, $"'{wanted}' is not a talent this level offers.");
            }

            talent = found;
        }
        else if (lists.Count > 0)
        {
            talent = lists.Select(list => DefaultTalent(creature, library, list, level, taken)).FirstOrDefault(found => found is not null);
        }

        // ---- the bonus feat: owed by the class, or bought by a combat trick ----
        var trick = talent?.Effect == TalentEffect.CombatTrick;
        var owed = needs.BonusFeats.Count > 0 || trick;
        FeatDefinition? feat = null;

        if (given.BonusFeat is { } chosen)
        {
            if (!owed)
            {
                return (null, $"this level hands out no bonus feat for {chosen.Title}.");
            }

            var offered = trick
                ? BonusFeatsOf(creature, library, FeatureIds.CombatFeat).ToList()
                : needs.BonusFeats;

            if (!offered.Any(feat => feat.Id == chosen.Id))
            {
                return (null, $"{chosen.Title} is not a bonus feat this level offers.");
            }

            feat = chosen.Takes != FeatChoice.None && chosen.Choice is null
                ? ChooseFor(creature, chosen)
                : chosen;

            if (feat is null || !feat.AvailableTo(creature))
            {
                return (null, $"{chosen.Title} cannot be taken again.");
            }
        }
        else if (owed)
        {
            feat = DefaultFeat(creature, library, trick ? FeatureIds.CombatFeat : KindOwed(taken, level));
        }

        // ---- the weapon group ----
        string? group = null;
        if (given.WeaponGroup is { } named)
        {
            if (!needs.WeaponGroups.Contains(named))
            {
                return (null, $"'{named}' is not a weapon group this level can train.");
            }

            group = named;
        }
        else if (needs.WeaponGroups.Count > 0)
        {
            group = DefaultGroup(creature);
        }

        return (new LevelChoices(feat, group, talent?.Id), null);
    }

    private static string KindOwed(ClassDefinition taken, int level) =>
        taken.FeaturesGainedAt(level)
            .Where(row => row.Id == FeatureIds.BonusFeat)
            .Select(row => row.Parameter("kind", FeatureIds.CombatFeat))
            .FirstOrDefault() ?? FeatureIds.CombatFeat;

    /// <summary>
    /// Takes what was chosen, once the level itself has been gained: the bonus feat, the weapon
    /// group, the talent and any feat it brings, then whatever the new level grows by itself.
    /// </summary>
    public static void Apply(Creature creature, LevelChoices choices, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(library);

        if (choices.BonusFeat is { } feat)
        {
            ClassFeatures.Take(creature, feat);
        }

        if (choices.WeaponGroup is { } group && !creature.Choices.WeaponGroups.Contains(group))
        {
            creature.Choices.WeaponGroups.Add(group);
        }

        if (choices.Talent is { } id && library.GetTalent(id) is { } talent && !creature.Choices.HasTalent(id))
        {
            creature.Choices.Talents.Add(talent);
        }

        ClassFeatures.Establish(creature, library, refill: false);
    }

    /// <summary>
    /// A feat with its choice made: Weapon Focus in whatever the creature fights with, or the
    /// next weapon it carries that it is not already focused on. Null when there is nothing
    /// left to choose.
    /// </summary>
    public static FeatDefinition? ChooseFor(Creature creature, FeatDefinition feat)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(feat);

        if (feat.Takes == FeatChoice.None || feat.Choice is not null)
        {
            return feat;
        }

        var weapons = new[] { creature.MeleeAttack, creature.PrimaryAttack }
            .Concat(creature.Attacks)
            .OfType<Combat.WeaponAttack>()
            .Select(weapon => weapon.Kind)
            .OfType<string>()
            .Distinct();

        foreach (var kind in weapons)
        {
            var candidate = feat with { Choice = kind };
            if (!creature.Feats.Any(held => held.Key == candidate.Key))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The combat or wizard feat to take when nobody says.</summary>
    public static FeatDefinition? DefaultFeat(Creature creature, ContentLibrary library, string kind)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(library);

        var offered = BonusFeatsOf(creature, library, kind).ToList();
        var preference = kind == FeatureIds.WizardFeat ? WizardPreference : CombatPreference;

        foreach (var id in preference.Concat(offered.OrderBy(feat => feat.Name, StringComparer.Ordinal).Select(feat => feat.Id)))
        {
            if (offered.FirstOrDefault(feat => feat.Id == id) is not { } feat || !Suits(creature, feat))
            {
                continue;
            }

            if (ChooseFor(creature, feat) is { } chosen && chosen.AvailableTo(creature))
            {
                return chosen;
            }
        }

        return null;
    }

    /// <summary>Whether a feat would do anything for this particular creature.</summary>
    private static bool Suits(Creature creature, FeatDefinition feat) => feat.Effect switch
    {
        FeatEffect.WeaponFinesse => creature.Abilities[Ability.Dexterity].Modifier
            > creature.Abilities[Ability.Strength].Modifier,
        FeatEffect.PointBlankShot or FeatEffect.PreciseShot or FeatEffect.RapidShot =>
            creature.PrimaryAttack is { IsRanged: true },
        FeatEffect.ShieldFocus => creature.Equipment.HasShield,
        _ => true,
    };

    /// <summary>
    /// The weapon group to train in when nobody says: whatever the creature swings, else
    /// whatever it shoots, else the first group not yet taken.
    /// </summary>
    public static string? DefaultGroup(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var candidates = new[] { creature.MeleeAttack, creature.PrimaryAttack }
            .OfType<Combat.WeaponAttack>()
            .SelectMany(weapon => weapon.Groups)
            .Concat(Martial.WeaponGroups);

        return candidates.FirstOrDefault(group =>
            Martial.WeaponGroups.Contains(group) && !creature.Choices.WeaponGroups.Contains(group));
    }

    /// <summary>The talent of one list to take when nobody says, at the level about to be reached.</summary>
    public static TalentDefinition? DefaultTalent(
        Creature creature, ContentLibrary library, string list, int? level = null, ClassDefinition? taken = null)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(library);

        var reached = level ?? Math.Max(1, ClassFeatures.LevelOf(creature, FeatureIds.Talent));
        var advanced = (taken?.FeaturesAt(reached).Any(row => row.Id == FeatureIds.AdvancedTalents) ?? false)
            || ClassFeatures.Has(creature, FeatureIds.AdvancedTalents);

        var offered = library.Talents
            .Where(talent => talent.List == list
                && !creature.Choices.HasTalent(talent.Id)
                && (advanced || !talent.Advanced)
                && talent.MinimumLevel <= reached
                && talent.Requires.All(creature.Choices.HasTalent))
            .ToList();

        var preference = list == FeatureIds.RagePowers ? RagePreference : RoguePreference;

        foreach (var effect in preference)
        {
            if (offered.FirstOrDefault(talent => talent.Effect == effect) is { } found
                && (effect != TalentEffect.FinesseRogue || Suits(creature, new FeatDefinition
                {
                    Id = "weapon-finesse", Name = "Weapon Finesse", Effect = FeatEffect.WeaponFinesse,
                })))
            {
                return found;
            }
        }

        return offered.OrderBy(talent => talent.Name, StringComparer.Ordinal).FirstOrDefault();
    }
}
