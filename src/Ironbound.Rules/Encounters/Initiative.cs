using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Encounters;

/// <summary>Who goes first.</summary>
public static class Initiative
{
    public const int DieSides = 20;

    /// <summary>Dexterity plus anything that improves initiative, resolved for stacking.</summary>
    public static ModifierBreakdown Explain(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var stack = new ModifierStack();
        if (creature.Abilities.Dexterity.Modifier != 0)
        {
            stack.Add(Modifier.Untyped(creature.Abilities.Dexterity.Modifier, "Dex"));
        }

        foreach (var modifier in creature.InitiativeModifiers.Modifiers)
        {
            stack.Add(modifier);
        }

        return stack.Explain();
    }

    public static int Bonus(Creature creature) => Explain(creature).Total;

    /// <summary>
    /// Rolls for everyone and sorts them. Ties fall to the higher Dexterity, then to the order
    /// given — never to chance, so a replay produces the same fight.
    /// </summary>
    public static List<Combatant> Roll(IEnumerable<Creature> creatures, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(creatures);
        ArgumentNullException.ThrowIfNull(random);

        var rolled = creatures
            .Select((creature, index) =>
            {
                var natural = random.NextDie(DieSides);
                return (Combatant: new Combatant(creature, natural, natural + Bonus(creature)), Index: index);
            })
            .ToList();

        return rolled
            .OrderByDescending(entry => entry.Combatant.Initiative)
            .ThenByDescending(entry => entry.Combatant.Creature.Abilities[Ability.Dexterity].Modifier)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Combatant)
            .ToList();
    }
}
