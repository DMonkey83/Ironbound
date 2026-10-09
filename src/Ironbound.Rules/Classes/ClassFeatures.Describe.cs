using Ironbound.Rules.Creatures;
using Ironbound.Rules.Magic;

namespace Ironbound.Rules.Classes;

public static partial class ClassFeatures
{
    /// <summary>
    /// Every class feature the creature has, as a character sheet prints it: a name and what it
    /// is worth right now — "Sneak attack: +1d6", "Rage: 12 of 17 rounds left".
    /// </summary>
    /// <remarks>
    /// One line per feature however many rows of the table it has reached, in the order the
    /// classes and their tables list them. A god, for anybody who has one, goes first, because
    /// half of what a cleric can do is decided by who she prays to.
    /// </remarks>
    public static IReadOnlyList<FeatureLine> Describe(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var lines = new List<FeatureLine>();

        if (creature.Choices.Deity is { } deity)
        {
            var weapon = deity.FavoredWeapon.Length > 0
                ? $", favoured weapon {deity.FavoredWeapon.Replace('-', ' ')}"
                : string.Empty;

            lines.Add(new FeatureLine("Deity", $"{deity}{weapon}"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var taken in creature.Levels)
        {
            foreach (var row in taken.Class.FeaturesAt(taken.Level))
            {
                // Bonus feats and talents come in kinds, and each kind is a line of its own.
                var key = row.Id switch
                {
                    FeatureIds.BonusFeat => $"{row.Id}:{row.Parameter("kind", FeatureIds.CombatFeat)}",
                    FeatureIds.Talent => $"{row.Id}:{row.Parameter("list")}",
                    _ => row.Id,
                };

                if (seen.Add(key))
                {
                    lines.AddRange(Line(creature, row));
                }
            }
        }

        return lines;
    }

    private static IEnumerable<FeatureLine> Line(Creature creature, ClassFeatureDefinition row)
    {
        var title = FeatureIds.Title(row.Id);

        switch (row.Id)
        {
            case FeatureIds.BonusFeat:
            {
                var kind = row.Parameter("kind", FeatureIds.CombatFeat);
                var count = Rows(creature, FeatureIds.BonusFeat)
                    .Count(other => other.Parameter("kind", FeatureIds.CombatFeat) == kind);

                yield return kind == FeatureIds.WizardFeat
                    ? new FeatureLine("Wizard bonus feats", $"{count} (metamagic, item creation or Spell Mastery)")
                    : new FeatureLine(title, $"{count} combat");
                break;
            }

            case FeatureIds.Bravery:
                yield return new FeatureLine(title, $"+{Martial.Bravery(creature)} Will against fear");
                break;

            case FeatureIds.ArmorTraining:
            {
                var rank = Martial.ArmorTraining(creature);
                var speed = rank >= 2 ? "medium and heavy armour" : "medium armour";
                yield return new FeatureLine(
                    title, $"{rank}: check penalty -{rank}, max Dex +{rank}, full speed in {speed}");
                break;
            }

            case FeatureIds.WeaponTraining:
            {
                var groups = Martial.TrainedGroups(creature);
                yield return new FeatureLine(title, groups.Count == 0
                    ? "no group chosen"
                    : string.Join(", ", groups.Select(group =>
                        $"{Martial.GroupName(group)} +{Martial.WeaponTraining(creature, group)}")));
                break;
            }

            case FeatureIds.SneakAttack:
                yield return new FeatureLine(title, $"+{SneakAttack.Dice(creature)}d6");
                break;

            case FeatureIds.Trapfinding:
                yield return new FeatureLine(
                    title, $"+{RogueDefences.Trapfinding(creature)} Disable Device, and Perception to find traps");
                break;

            case FeatureIds.Evasion:
                yield return new FeatureLine(
                    title, RogueDefences.HasImprovedEvasion(creature)
                        ? "improved: no damage on a Reflex save made, half on one failed"
                        : "no damage on a Reflex save made, in light armour or none");
                break;

            case FeatureIds.Talent:
            {
                var list = row.Parameter("list");
                var taken = creature.Choices.Talents.Where(talent => talent.List == list).ToList();
                var name = list == FeatureIds.RagePowers ? "Rage powers" : "Rogue talents";

                yield return new FeatureLine(name, taken.Count == 0
                    ? "none chosen"
                    : string.Join(", ", taken.Select(talent => talent.Name.ToLowerInvariant())));
                break;
            }

            case FeatureIds.TrapSense:
                yield return new FeatureLine(title, $"+{RogueDefences.TrapSense(creature)} Reflex and AC against traps");
                break;

            case FeatureIds.UncannyDodge:
                yield return new FeatureLine(title, "never caught flat-footed");
                break;

            case FeatureIds.ImprovedUncannyDodge:
                yield return new FeatureLine(
                    title, $"flanked only by a rogue of level {UncannyDodge.Levels(creature) + UncannyDodge.FlankingMargin} or more");
                break;

            case FeatureIds.AdvancedTalents:
                yield return new FeatureLine(title, "advanced rogue talents may be chosen");
                break;

            case FeatureIds.Aura:
                yield return new FeatureLine(title, creature.Choices.Deity is { } god
                    ? $"{Alignments.Abbreviate(god.Alignment)}, as her god's"
                    : "of her faith");
                break;

            case FeatureIds.ChannelEnergy:
            {
                var kind = ClassPowers.ChannelKindOf(creature) == ChannelKind.Negative ? "negative" : "positive";
                yield return new FeatureLine(title,
                    $"{kind} {ClassPowers.ChannelDice(creature)}d6, "
                    + $"{ClassPowers.Left(creature, ClassPowers.ChannelPool)} of "
                    + $"{ClassPowers.PerDay(creature, ClassPowers.ChannelPool)} left, "
                    + $"DC {ClassPowers.ChannelDifficulty(creature)}");
                break;
            }

            case FeatureIds.Domains:
                yield return new FeatureLine(title, creature.Choices.Domains.Count == 0
                    ? "none chosen"
                    : string.Join(", ", creature.Choices.Domains.Select(domain => domain.Name)));

                if (ClassPowers.HasDomainPower(creature, GrantedPowerEffect.HealersBlessing))
                {
                    yield return new FeatureLine("Healer's Blessing", "cure spells are cast empowered");
                }

                break;

            case FeatureIds.Orisons:
                yield return new FeatureLine(title, "0-level spells at will (none in the game yet)");
                break;

            case FeatureIds.SpontaneousCasting:
                yield return new FeatureLine(title, ClassPowers.ChannelKindOf(creature) == ChannelKind.Negative
                    ? "inflict spells in place of anything prepared"
                    : "cure spells in place of anything prepared");
                break;

            case FeatureIds.ArcaneBond:
                yield return new FeatureLine(title, creature.Choices.BondedObject
                    ? $"bonded object, {ClassPowers.Left(creature, ClassPowers.ArcaneBondPool)} of "
                        + $"{ClassPowers.PerDay(creature, ClassPowers.ArcaneBondPool)} spell left today"
                    : "none");
                break;

            case FeatureIds.ArcaneSchool:
            {
                var school = creature.Choices.School;
                var opposed = creature.Choices.Opposition.Count > 0
                    ? $" (opposed: {string.Join(", ", creature.Choices.Opposition.Select(o => o.ToString().ToLowerInvariant()))})"
                    : string.Empty;

                yield return new FeatureLine(title, school is null ? "none chosen" : $"{school.Name}{opposed}");

                if (ClassPowers.IntenseBonus(creature) is > 0 and var intense)
                {
                    yield return new FeatureLine("Intense spells", $"+{intense} damage from evocation spells");
                }

                if (ClassPowers.HasSchoolPower(creature, GrantedPowerEffect.SummonersCharm))
                {
                    yield return new FeatureLine("Summoner's charm", "recorded only: there is no summoning yet");
                }

                break;
            }

            case FeatureIds.Cantrips:
                yield return new FeatureLine(title, "0-level spells at will (none in the game yet)");
                break;

            case FeatureIds.ScribeScroll:
                yield return new FeatureLine(title, "recorded only: there is no crafting yet");
                break;

            case FeatureIds.FastMovement:
                yield return new FeatureLine(title, $"+{FastMovementFeet} ft");
                break;

            case FeatureIds.Rage:
                yield return new FeatureLine(
                    title,
                    $"{Rage.RoundsLeft(creature)} of {Rage.RoundsPerDay(creature)} rounds left"
                    + (Rage.IsRaging(creature) ? ", raging" : string.Empty)
                    + (IsFatigued(creature) ? ", fatigued" : string.Empty));
                break;

            case FeatureIds.DamageReduction:
                yield return new FeatureLine(title, $"{Rank(creature, FeatureIds.DamageReduction)}/—");
                break;

            default:
                yield return new FeatureLine(title, string.Empty);
                break;
        }
    }
}
