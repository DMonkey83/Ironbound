using Ironbound.Rules.Abilities;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Skills;

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
        "precise-shot", "improved-critical", "rapid-shot", "weapon-specialization", "deadly-aim",
        "greater-weapon-focus", "manyshot", "greater-trip", "greater-bull-rush", "critical-focus",
        "greater-shield-focus", "mobility", "great-cleave", "far-shot",
    ];

    private static readonly string[] WizardPreference = ["empower-spell", "extend-spell", "quicken-spell", "maximize-spell"];

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

        var reaching = creature.Level + 1;
        var favoured = IsFavoured(creature, taken);

        return new LevelNeeds(
            [.. feats.DistinctBy(feat => feat.Id).OrderBy(feat => feat.Name, StringComparer.Ordinal)],
            groups,
            [.. talents.Distinct()])
        {
            AbilityIncrease = Levelling.GrantsAbilityIncreaseAt(reaching),
            DefaultAbility = taken.KeyAbility,
            FavouredClass = favoured,
            FavouredSkills = favoured
                ? [.. SkillInfo.All.Where(skill => creature.Skills.Ranks(skill) < reaching)]
                : [],
        };
    }

    /// <summary>
    /// Whether a level in this class is a level in the creature's favoured class: the one its
    /// file names, or else the first class it took. A creature with no class yet favours the
    /// first one it takes.
    /// </summary>
    public static bool IsFavoured(Creature creature, ClassDefinition taken)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);

        return creature.FavouredClass is not { } favoured || string.Equals(favoured, taken.Id, StringComparison.Ordinal);
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

        // ---- the ability increase: the class's key ability unless somebody says ----
        Ability? raised = null;
        if (given.AbilityIncrease is { } ability)
        {
            if (!needs.AbilityIncrease)
            {
                return (null, $"level {creature.Level + 1} raises no ability score.");
            }

            if (!creature.Abilities[ability].HasScore)
            {
                return (null, $"{creature.Name} has no {AbilityInfo.Abbreviate(ability)} to raise.");
            }

            raised = ability;
        }
        else if (needs.AbilityIncrease)
        {
            raised = creature.Abilities[needs.DefaultAbility].HasScore ? needs.DefaultAbility : Ability.Strength;
        }

        // ---- the favoured class: a hit point unless somebody asks for the rank ----
        FavouredClassBonus? bonus = null;
        Skill? skill = null;
        if (given.Favoured is not null && !needs.FavouredClass)
        {
            return (null, $"{taken.Name} is not {creature.Name}'s favoured class.");
        }

        if (given.FavouredSkill is not null && given.Favoured != FavouredClassBonus.SkillRank)
        {
            return (null, "a skill was named for a favoured-class bonus that is not a skill rank.");
        }

        if (needs.FavouredClass)
        {
            bonus = given.Favoured ?? FavouredClassBonus.HitPoint;

            if (bonus == FavouredClassBonus.SkillRank)
            {
                skill = given.FavouredSkill ?? DefaultFavouredSkill(creature, taken, needs.FavouredSkills);

                if (skill is not { } room || !needs.FavouredSkills.Contains(room))
                {
                    return (null, given.FavouredSkill is { } refused
                        ? $"{SkillInfo.Name(refused)} already has as many ranks as {creature.Name}'s level allows."
                        : $"{creature.Name} has no skill left with room for another rank.");
                }
            }
        }

        return (new LevelChoices(feat, group, talent?.Id, raised, bonus, skill), null);
    }

    /// <summary>
    /// The skill a favoured-class rank goes into when nobody says: the class skill the creature
    /// has put most into already that still has room, else any skill that has room.
    /// </summary>
    public static Skill? DefaultFavouredSkill(Creature creature, ClassDefinition taken, IReadOnlyList<Skill> room)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(room);

        return room
            .OrderByDescending(skill => taken.ClassSkills.Contains(skill))
            .ThenByDescending(skill => creature.Skills.Ranks(skill))
            .Cast<Skill?>()
            .FirstOrDefault();
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

        if (choices.AbilityIncrease is { } ability && creature.Abilities[ability].HasScore)
        {
            creature.Abilities[ability].Base += 1;

            // A higher casting score can be worth a bonus spell. Raised rather than set, as the
            // level's own slots were, so nothing spent this morning comes back.
            if (ability == creature.Spells.CastingAbility && creature.Levels.Count > 0)
            {
                foreach (var (level, count) in Progression.SlotsFor(creature.Levels, creature.Abilities[ability].Modifier))
                {
                    creature.Spells.RaiseSlots(level, count);
                }
            }
        }

        switch (choices.Favoured)
        {
            case FavouredClassBonus.HitPoint:
                creature.HitPoints.Base += 1;
                break;

            case FavouredClassBonus.SkillRank when choices.FavouredSkill is { } skill:
                creature.Skills.SetRanks(skill, creature.Skills.Ranks(skill) + 1);
                break;
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

        if (feat.Takes != FeatChoice.Weapon)
        {
            return ChooseOther(creature, feat);
        }

        // A proficiency feat is taken for a weapon of its own category that the creature cannot
        // use yet; anything else for whatever it fights with first.
        var weapons = new[] { creature.MeleeAttack, creature.PrimaryAttack }
            .Concat(creature.Attacks)
            .OfType<Combat.WeaponAttack>()
            .Where(weapon => !Proficiency.IsProficiencyFeat(feat.Effect)
                || (Proficiency.FeatCovers(feat, weapon) && !Proficiency.IsProficient(creature, weapon)))
            .Select(weapon => weapon.Kind)
            .OfType<string>()
            .Distinct();

        // One it qualifies for first — Weapon Specialization in the weapon it has Weapon Focus
        // in — and failing that the first it does not hold yet, which is what a talent handing
        // the feat out regardless of its prerequisites wants.
        var fresh = weapons
            .Select(kind => feat with { Choice = kind })
            .Where(candidate => !creature.Feats.Any(held => held.Key == candidate.Key))
            .ToList();

        return fresh.FirstOrDefault(candidate => candidate.AvailableTo(creature)) ?? fresh.FirstOrDefault();
    }

    /// <summary>
    /// A feat taken for a skill, a school, an alignment or an element, with that chosen: Skill
    /// Focus in the skill with most ranks, Spell Focus in the school most of the prepared spells
    /// are of, and so on. Null when there is nothing left it could be taken for.
    /// </summary>
    private static FeatDefinition? ChooseOther(Creature creature, FeatDefinition feat)
    {
        IEnumerable<string> options = feat.Takes switch
        {
            FeatChoice.Skill => Skills.SkillInfo.All
                .OrderByDescending(skill => creature.Skills.Ranks(skill))
                .ThenBy(skill => skill)
                .Select(skill => skill.ToString()),

            // The school of most of what she prepares; any school after that.
            FeatChoice.School => creature.Spells.Prepared
                .GroupBy(spell => spell.School)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .Select(group => group.Key.ToString())
                .Concat(FeatChoices.Options(FeatChoice.School)),

            _ => FeatChoices.Options(feat.Takes),
        };

        foreach (var option in options.Distinct(StringComparer.Ordinal))
        {
            var candidate = feat with { Choice = option };
            if (!creature.Feats.Any(held => held.Key == candidate.Key) && candidate.AvailableTo(creature))
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
        FeatEffect.PointBlankShot or FeatEffect.PreciseShot or FeatEffect.RapidShot or FeatEffect.DeadlyAim
            or FeatEffect.Manyshot or FeatEffect.FarShot or FeatEffect.ImprovedPreciseShot or FeatEffect.ShotOnTheRun =>
            creature.PrimaryAttack is { IsRanged: true },
        FeatEffect.ShieldFocus or FeatEffect.GreaterShieldFocus => creature.Equipment.HasShield,

        // Nothing in the game yet that it would help with; taken only on purpose.
        FeatEffect.Endurance => false,
        _ when Proficiency.IsProficiencyFeat(feat.Effect) => Proficiency.WouldHelp(creature, feat),
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
