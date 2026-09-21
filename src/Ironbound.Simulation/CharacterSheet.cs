using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Items;
using Ironbound.Rules.Saves;

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
            Gear(creature),
            Magic(creature),
            Afflictions(creature),
        ];
    }

    /// <summary>The whole sheet as one block of text, for a log or a console.</summary>
    public static string Describe(Creature creature) =>
        string.Join("\n\n", Of(creature).Where(section => section.Lines.Count > 0));

    private static SheetSection Who(Creature creature) => new("Who", [
        creature.Description,
        $"{CreatureSizes.Name(creature.Size)}, speed {creature.CurrentSpeed} ft, reach {creature.Reach} ft",
        $"Hit points {creature.HitPoints}",
        $"Base attack +{creature.BaseAttackBonus}, "
            + $"{creature.AttacksPerFullAttack} attack(s) on a full attack",
        $"Attacks of opportunity {creature.AttacksOfOpportunityPerRound} a round",
    ]);

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
                : $"reach {creature.Reach} ft";

            lines.Add($"{weapon.Name} {bonus.Total:+0;-0;+0} ({reach}) — {bonus}");
            lines.Add($"  damage {weapon.Damage} {damage:+0;-0;+0}, {weapon.Attack.Critical}");
        }

        if (lines.Count > 0)
        {
            lines.Add($"Manoeuvre bonus {Maneuvers.Bonus(creature).Total:+0;-0;+0}");
        }

        return new SheetSection("Attacks", lines);
    }

    private static SheetSection Gear(Creature creature)
    {
        var lines = new List<string>();

        foreach (var entry in creature.Equipment.Worn)
        {
            var where = entry.IsWorn ? entry.Slot.ToString() : "stowed";
            lines.Add($"{entry.Item.Name} ({where})");
        }

        foreach (var feat in creature.Feats)
        {
            lines.Add(feat.Description.Length > 0
                ? $"{feat.Name} — {feat.Description}"
                : feat.Name);
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

        foreach (var level in creature.Spells.SlotLevels.OrderBy(level => level))
        {
            lines.Add($"Level {level} slots: "
                + $"{creature.Spells.SlotsRemaining(level)} of {creature.Spells.SlotsMaximum(level)}");
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
