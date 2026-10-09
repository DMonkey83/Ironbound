using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Skills;

namespace Ironbound.Simulation;

/// <summary>One headed block of a character sheet.</summary>
public sealed record SheetSection(string Heading, IReadOnlyList<string> Lines)
{
    public override string ToString() =>
        $"{Heading}\n{string.Join("\n", Lines.Select(line => "  " + line))}";
}

/// <summary>
/// Everything worth knowing about a creature, written out.
/// </summary>
/// <remarks>
/// The project has been building <c>Explain()</c> breakdowns since the modifier layer, on the
/// principle that "why did that miss?" ought to have an answer. Until now nothing ever showed
/// one. This is where they surface — every derived number next to the parts it was made of,
/// including the ones that were suppressed for not stacking.
/// <para>
/// Plain text and plain sections, built here rather than in the Godot layer, so the hard part
/// is testable without an engine and the view has only to draw it.
/// </para>
/// </remarks>
public static class CharacterSheet
{
    public static IReadOnlyList<SheetSection> Of(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return
        [
            Who(creature),
            Abilities(creature),
            Defence(creature),
            Saves(creature),
            Attacks(creature),
            Features(creature),
            RacialTraits(creature),
            Training(creature),
            Gear(creature),
            Magic(creature),
            Afflictions(creature),
        ];
    }

    /// <summary>The whole sheet as one block of text, for a log or a console.</summary>
    public static string Describe(Creature creature) =>
        string.Join("\n\n", Of(creature).Where(section => section.Lines.Count > 0));

    private static SheetSection Who(Creature creature) => new("Who", [
        creature.Race is { } race ? $"{race.Name} {creature.Description}" : creature.Description,
        $"{CreatureSizes.Name(creature.Size)}, speed {creature.CurrentSpeed} ft, reach {Battlefield.ReachOf(creature)}",
        $"Hit points {creature.HitPoints}",
        $"Base attack +{creature.BaseAttackBonus}, "
            + $"{creature.AttacksPerFullAttack} attack(s) on a full attack",
        $"Attacks of opportunity {creature.AttacksOfOpportunityPerRound} a round",
        $"Fighting: {creature.Stances}",
        Load(creature),
    ]);

    /// <summary>
    /// "Carrying 38 lb: light load (light to 76 lb, …)", and the party's load when that is the
    /// worse of the two and so the one that counts.
    /// </summary>
    private static string Load(Creature creature)
    {
        var own = Encumbrance.Load(creature);
        var line = $"Carrying {Pricing.Pounds(own.Weight)}: {Encumbrance.Name(own.Category)} load ({own.Capacity})";
        var effective = Encumbrance.Effective(creature);

        return effective > own.Category
            ? $"{line}; the party's share makes it {Encumbrance.Name(effective)}"
            : line;
    }

    private static SheetSection Abilities(Creature creature) => new(
        "Abilities",
        [.. AbilityInfo.All.Select(ability => creature.Abilities[ability].ToString())]);

    private static SheetSection Defence(Creature creature)
    {
        var lines = new List<string>
        {
            $"Armour class {creature.ArmorClass.Total} — {creature.ArmorClass.Explain()}",
            $"Touch {creature.ArmorClass.Touch}, flat-footed {creature.ArmorClass.FlatFooted}",
            $"Manoeuvre defence {Maneuvers.Defense(creature)} — {Maneuvers.DefenseBonus(creature)}",
        };

        // A shield the armour class above leaves out has to say why, or it reads as a mistake.
        if (creature.Equipment.ShieldSetAside
            && creature.Equipment.InSlot(EquipmentSlot.Shield).FirstOrDefault() is { } shield
            && creature.MeleeAttack is { } twoHanded)
        {
            lines.Add($"{shield.Name} not counted: both hands are on the {twoHanded.Name}");
        }

        foreach (var reduction in creature.Defenses.Reductions)
        {
            lines.Add($"Damage reduction {reduction}");
        }

        foreach (var (type, amount) in creature.Defenses.Resistances.OrderBy(r => r.Key.ToString()))
        {
            lines.Add($"Resist {type} {amount}");
        }

        foreach (var immune in creature.Defenses.Immunities.OrderBy(type => type.ToString()))
        {
            lines.Add($"Immune to {immune}");
        }

        return new SheetSection("Defence", lines);
    }

    private static SheetSection Saves(Creature creature) => new(
        "Saving throws",
        [.. SaveInfo.All.Select(save =>
            $"{save} {creature.Saves[save].Total:+0;-0;+0} — {creature.Saves[save].Explain()}")]);

    private static SheetSection Attacks(Creature creature)
    {
        var lines = new List<string>();

        foreach (var weapon in creature.Attacks)
        {
            var bonus = Strike.AttackBonus(creature, weapon);
            var damage = Strike.DamageBonus(creature, weapon).Total;
            var reach = weapon.IsRanged
                ? $"range {weapon.RangeIncrement} ft"
                : $"reach {Battlefield.ReachOf(creature, weapon)}";
            var state = creature.Equipment.EntryFor(weapon) switch
            {
                { IsOutOfHand: true } => ", out of hand",
                { IsBroken: true } => ", broken",
                _ => string.Empty,
            };

            lines.Add($"{weapon.Name} {bonus.Total:+0;-0;+0} ({reach}{state}) — {bonus}");
            lines.Add($"  damage {weapon.Damage} {damage:+0;-0;+0}, {Strike.CriticalFor(creature, weapon)}");
        }

        if (lines.Count > 0)
        {
            lines.Add($"Manoeuvre bonus {Maneuvers.Bonus(creature).Total:+0;-0;+0}");
        }

        return new SheetSection("Attacks", lines);
    }

    /// <summary>
    /// What the classes hand out, line by line, and then what is left of each daily allowance —
    /// the numbers a player checks before deciding whether this is the fight to spend them on.
    /// </summary>
    private static SheetSection Features(Creature creature)
    {
        var lines = ClassFeatures.Describe(creature).Select(line => line.ToString()).ToList();

        // Powers that share a pool — every spell an arcane bond can cast — are one allowance,
        // so they are one line.
        foreach (var pool in creature.Powers.GroupBy(power => power.Pool))
        {
            var first = pool.First();
            var left = creature.UsesLeft(first);
            var perDay = creature.UsesPerDay(first);
            var unit = pool.Key == Rage.Pool ? " rounds of rage" : pool.Key == "dimensional-steps" ? " ft" : string.Empty;

            lines.Add(pool.Count() == 1
                ? $"{first.Name}: {left} of {perDay}{unit} left today"
                : $"{Title(pool.Key)}: {left} of {perDay}{unit} left today — "
                    + string.Join(", ", pool.Select(power => power.Effect.Name)));
        }

        return new SheetSection("Class features", lines);
    }

    /// <summary>
    /// The creature's people and each of its traits, a line apiece, as class features are. Empty
    /// for a creature with no race, which leaves the heading out of the written form.
    /// </summary>
    private static SheetSection RacialTraits(Creature creature) => new(
        "Racial traits",
        creature.Race is { } race ? [.. race.Traits.Select(trait => trait.ToString())] : []);

    private static string Title(string pool) => pool switch
    {
        ClassPowers.ArcaneBondPool => "Bonded object",
        _ => pool,
    };

    private static SheetSection Training(Creature creature) => new(
        "Skills",
        [.. creature.Skills.Trained.Select(skill =>
            $"{SkillInfo.Name(skill)} {creature.Skills.Total(skill):+0;-0;+0}"
            + (creature.Skills.IsClassSkill(skill) ? " (class)" : string.Empty)
            + $" — {creature.Skills.Explain(skill)}")]);

    private static SheetSection Gear(Creature creature)
    {
        var lines = new List<string>();

        foreach (var entry in creature.Equipment.Worn)
        {
            var where = entry.IsWorn ? entry.Slot.ToString() : "stowed";
            var state = (entry.IsBroken ? ", broken" : string.Empty) + (entry.IsOutOfHand ? ", out of hand" : string.Empty);
            var untrained = Proficiency.IsProficient(creature, entry.Item) ? string.Empty : ", not proficient";
            lines.Add($"{entry.Item.Name} ({where}{state}{untrained})");
        }

        if (Proficiency.Describe(creature) is { Length: > 0 } trained)
        {
            lines.Add(trained);
        }

        foreach (var feat in creature.Feats)
        {
            lines.Add(feat.Description.Length > 0
                ? $"{feat.Title} — {feat.Description}"
                : feat.Title);
        }

        return new SheetSection("Gear and training", lines);
    }

    private static SheetSection Magic(Creature creature)
    {
        if (creature.Spells.CasterLevel == 0 && creature.Spells.Prepared.Count == 0)
        {
            return new SheetSection("Magic", []);
        }

        var lines = new List<string>
        {
            $"Caster level {creature.Spells.CasterLevel}, {creature.Spells.CastingAbility}",
        };

        // A domain's or a school's slot holds only its own spells, so it is shown beside the
        // general ones rather than added to them.
        var specialty = creature.Choices.Domains.Count > 0 ? "domain" : "school";

        foreach (var level in creature.Spells.SlotLevels.Union(creature.Spells.SpecialtyLevels).Order())
        {
            var line = $"Level {level} slots: "
                + $"{creature.Spells.SlotsRemaining(level)} of {creature.Spells.SlotsMaximum(level)}";

            if (creature.Spells.SpecialtyMaximum(level) > 0)
            {
                line += $", {specialty} {creature.Spells.SpecialtyRemaining(level)} "
                    + $"of {creature.Spells.SpecialtyMaximum(level)}";
            }

            lines.Add(line);
        }

        foreach (var spell in creature.Spells.Prepared)
        {
            var ready = creature.Spells.CanCast(spell) ? string.Empty : " — no slots left";
            lines.Add($"{spell.Name} (level {spell.Level}, DC {creature.Spells.SaveDC(spell)}){ready}");
        }

        return new SheetSection("Magic", lines);
    }

    private static SheetSection Afflictions(Creature creature)
    {
        var lines = new List<string>();

        foreach (var condition in creature.Conditions)
        {
            lines.Add(condition.ToString());
        }

        foreach (var effect in creature.Effects.Active)
        {
            lines.Add($"{effect.Name} — {effect.Remaining} remaining");
        }

        return new SheetSection("Conditions", lines);
    }
}
