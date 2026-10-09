using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Classes;

/// <summary>
/// The moments a creature's class features have to be acted on rather than merely asked about:
/// being built, gaining a level, resting, and walking away from a fight.
/// </summary>
public static partial class ClassFeatures
{
    /// <summary>Fast movement's ten feet.</summary>
    public const int FastMovementFeet = 10;

    /// <summary>Swift foot's five.</summary>
    public const int SwiftFootFeet = 5;

    /// <summary>
    /// Settles everything a freshly built creature's features need from the content library:
    /// domain and school slots, spontaneous spells, the spellbook, damage reduction, the feats
    /// talents hand out, and a sensible pick for any choice its file left unmade.
    /// </summary>
    /// <remarks>
    /// Called once, after the class levels, feats and items are in place. Every step is safe to
    /// repeat, which is what lets a save that predates class features be brought up to date by
    /// calling it again.
    /// </remarks>
    /// <param name="refill">True at build, where the domain and school slots start full; false
    /// after a level, where only a newly gained one arrives.</param>
    public static void Establish(Creature creature, ContentLibrary library, bool refill = true)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(library);

        FillMissingChoices(creature, library);
        GrantTalentFeats(creature, library);

        if (creature.Spells.Spellbook.Count == 0)
        {
            foreach (var spell in creature.Spells.Prepared)
            {
                creature.Spells.Inscribe(spell);
            }
        }

        SetSpontaneous(creature, library);
        Grow(creature, refill);
    }

    /// <summary>
    /// Brings the parts of a creature that follow its levels without any choice up to date:
    /// the domain or school slot at every spell level it can cast, and barbarian damage
    /// reduction. Called by <see cref="Levelling.Gain"/> as well as at build.
    /// </summary>
    /// <param name="refill">True at build, where slots start full; false on levelling, where
    /// only the newly gained slot arrives and the spent ones stay spent.</param>
    public static void Grow(Creature creature, bool refill = false)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var level in SpecialtyLevels(creature))
        {
            if (refill)
            {
                creature.Spells.SetSpecialtySlots(level, 1);
            }
            else
            {
                creature.Spells.RaiseSpecialtySlots(level, 1);
            }
        }

        // Damage reduction from different sources does not stack, and the best one applies,
        // so a larger amount is simply added beside the old one.
        var reduction = Rank(creature, FeatureIds.DamageReduction);
        if (reduction > 0 && creature.Defenses.Reductions.All(
            existing => existing.BypassedBy != DamageBypass.None || existing.Amount < reduction))
        {
            creature.Defenses.Reduce(reduction);
        }
    }

    /// <summary>
    /// Every spell level that has a domain or school slot: one for each level the class that
    /// grants them can cast at all, and none for a universalist.
    /// </summary>
    public static IEnumerable<int> SpecialtyLevels(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var specialist = creature.Choices.School is { IsUniversalist: false };
        var levels = new SortedSet<int>();

        foreach (var taken in creature.Levels)
        {
            var features = taken.Class.FeaturesAt(taken.Level).Select(feature => feature.Id).ToHashSet();
            var grants = (features.Contains(FeatureIds.Domains) && creature.Choices.Domains.Count > 0)
                || (features.Contains(FeatureIds.ArcaneSchool) && specialist);

            if (!grants)
            {
                continue;
            }

            var table = taken.Class.SlotsAt(taken.Level);
            for (var level = 1; level <= table.Count; level++)
            {
                if (table[level - 1] > 0)
                {
                    levels.Add(level);
                }
            }
        }

        return levels;
    }

    /// <summary>
    /// The cure spells — or, for a cleric who channels negative energy, the inflict ones — that
    /// may take the place of anything prepared.
    /// </summary>
    public static void SetSpontaneous(Creature creature, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(library);

        if (!Has(creature, FeatureIds.SpontaneousCasting))
        {
            creature.Spells.SetSpontaneous([]);
            return;
        }

        var family = ClassPowers.ChannelKindOf(creature) == ChannelKind.Negative ? "inflict" : "cure";

        creature.Spells.SetSpontaneous(library.SpellIds
            .Select(library.GetSpell)
            .OfType<Spell>()
            .Where(spell => spell.Has(family))
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Id, StringComparer.Ordinal));
    }

    /// <summary>
    /// The feats that come with talents: Finesse Rogue is Weapon Finesse, the rogue's Weapon
    /// Training is Weapon Focus in whatever she is holding.
    /// </summary>
    public static void GrantTalentFeats(Creature creature, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(library);

        foreach (var talent in creature.Choices.Talents)
        {
            switch (talent.Effect)
            {
                case TalentEffect.FinesseRogue when !creature.HasFeat(FeatEffect.WeaponFinesse):
                    if (library.FeatIds.Select(library.GetFeat).FirstOrDefault(
                        feat => feat?.Effect == FeatEffect.WeaponFinesse) is { } finesse)
                    {
                        Take(creature, finesse);
                    }

                    break;

                case TalentEffect.WeaponTraining:
                    if (library.FeatIds.Select(library.GetFeat).FirstOrDefault(
                        feat => feat?.Effect == FeatEffect.WeaponFocus) is { } focus
                        && ClassLevelling.ChooseFor(creature, focus) is { } chosen
                        && !creature.Feats.Any(held => held.Effect == FeatEffect.WeaponFocus))
                    {
                        Take(creature, chosen);
                    }

                    break;
            }
        }
    }

    /// <summary>Both halves of taking a feat, exactly once: into the list, and its bonuses into the stacks.</summary>
    public static void Take(Creature creature, FeatDefinition feat)
    {
        creature.Feats.Add(feat);
        feat.ApplyTo(creature);

        if (feat.Effect == FeatEffect.Toughness)
        {
            creature.HitPoints.Base += ToughnessFor(creature.HitPoints.HitDice);
        }
    }

    /// <summary>Toughness: three hit points, or one a hit die once there are more than three.</summary>
    public static int ToughnessFor(int hitDice) => Math.Max(3, hitDice);

    /// <summary>
    /// Fills in a choice a class has handed out but the creature's file never made: a weapon
    /// group for a fighter's training, a talent for every talent row. A file that says nothing
    /// should still produce a character whose features work.
    /// </summary>
    private static void FillMissingChoices(Creature creature, ContentLibrary library)
    {
        while (creature.Choices.WeaponGroups.Count < Rank(creature, FeatureIds.WeaponTraining)
            && ClassLevelling.DefaultGroup(creature) is { } group)
        {
            creature.Choices.WeaponGroups.Add(group);
        }

        foreach (var list in new[] { FeatureIds.RogueTalents, FeatureIds.RagePowers })
        {
            var owed = Rows(creature, FeatureIds.Talent).Count(row => row.Parameter("list") == list);
            var held = creature.Choices.Talents.Count(talent => talent.List == list);

            for (; held < owed; held++)
            {
                if (ClassLevelling.DefaultTalent(creature, library, list) is not { } talent)
                {
                    break;
                }

                creature.Choices.Talents.Add(talent);
            }
        }

        if (Has(creature, FeatureIds.ArcaneBond) && !creature.Choices.BondedObject)
        {
            creature.Choices.BondedObject = true;
        }
    }

    /// <summary>
    /// A night's rest for the class features: every daily pool full, and whatever a rage left
    /// behind slept off. Called after the creature's effects are cleared.
    /// </summary>
    public static void Rest(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        // Clearing the effects ended any rage, and ending a rage leaves fatigue: that is the
        // one effect that can survive a clear, so it is taken off here.
        Rage.End(creature);
        creature.Effects.Remove(Rage.FatigueLabel);
        creature.DailyUses.Restore();
    }

    /// <summary>
    /// The fight is over: a rage does not outlast it, and nor does the fatigue that follows one,
    /// whether the rage ended just now or rounds ago.
    /// </summary>
    /// <remarks>
    /// The fatigue is the part that is not the rule as written. It lasts twice as many rounds as
    /// the rage — at most thirty-four at the levels this game reaches, under four minutes — and
    /// the engine does not move time between fights at all, so left alone it would be carried
    /// into the next room however long the walk there took. Shedding it here says the walk took
    /// longer than that, which on a level of any size it does.
    /// </remarks>
    public static void EndFight(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        Rage.End(creature);

        if (Has(creature, FeatureIds.Rage))
        {
            creature.Effects.Remove(Rage.FatigueLabel);
        }

        creature.Stances.EndRage();
    }

    /// <summary>
    /// What class features and feats add to land speed before armour has its say: a barbarian's
    /// fast movement out of heavy armour, swift foot while raging, and Fleet.
    /// </summary>
    public static int SpeedBonus(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var bonus = 0;

        if (Has(creature, FeatureIds.FastMovement) && creature.Equipment.ArmourWorn != ArmourCategory.Heavy)
        {
            bonus += FastMovementFeet;
        }

        if (Rage.IsRaging(creature) && creature.Choices.HasTalent(TalentEffect.SwiftFoot))
        {
            bonus += SwiftFootFeet;
        }

        // Fleet is five feet each time it is taken, and nothing at all in medium or heavy armour.
        if (creature.Equipment.ArmourWorn is not (ArmourCategory.Medium or ArmourCategory.Heavy))
        {
            bonus += Encounters.Movement.FleetFeet * creature.Feats.Count(feat => feat.Effect == FeatEffect.Fleet);
        }

        return bonus;
    }

    /// <summary>
    /// What armour and class features do to one skill: the armour check penalty on anything run
    /// on Strength or Dexterity, and trapfinding on Disable Device.
    /// </summary>
    public static ModifierStack SkillModifiers(Creature creature, Skill skill)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stack = new ModifierStack();

        if (SkillInfo.AbilityFor(skill) is Ability.Strength or Ability.Dexterity
            && Martial.CheckPenalty(creature) is < 0 and var penalty)
        {
            stack.Add(penalty, BonusType.Untyped, "Armour check penalty");
        }

        if (skill == Skill.DisableDevice && RogueDefences.Trapfinding(creature) is > 0 and var finding)
        {
            stack.Add(finding, BonusType.Untyped, "Trapfinding");
        }

        return stack;
    }

    /// <summary>Whether a raging creature is barred from a skill for the length of the rage.</summary>
    public static bool BarsSkill(Creature creature, Skill skill) =>
        Rage.IsRaging(creature) && Rage.Forbids(skill);

    /// <summary>Whether the fatigue a rage left behind is still on her.</summary>
    public static bool IsFatigued(Creature creature) => creature.Has(Condition.Fatigued);
}
