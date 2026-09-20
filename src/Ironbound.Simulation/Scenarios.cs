using Ironbound.Rules;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation;

/// <summary>
/// Ready-made fights. One definition, used by the tests and by the game, so the two cannot drift
/// apart — and every one of them is reproducible from its seed.
/// </summary>
/// <remarks>
/// Nothing here describes a creature any more. The fights live in content files; this only knows
/// how to turn one of those into a <see cref="Battle"/>, which means deciding which side the
/// player is on — a question the rules layer deliberately does not answer.
/// </remarks>
public static class Scenarios
{
    /// <summary>Stream 1 is the dice; stream 2 is what the AI is thinking. They never interfere.</summary>
    public const ulong DiceStream = 1;

    public const ulong ThoughtStream = 2;

    public const string GoblinAmbushId = "goblin-ambush";

    /// <summary>
    /// The opening fight: two ranks facing each other across thirty-odd feet, so the first round
    /// is spent closing.
    /// </summary>
    public static Battle GoblinAmbush(
        ulong seed = 20260920, RuleOptions? rules = null, ContentLibrary? library = null) =>
        Build(library ?? ContentFiles.Default, GoblinAmbushId, seed, rules);

    /// <summary>Builds a fight from its written-down form.</summary>
    /// <exception cref="ArgumentException">There is no encounter by that id.</exception>
    public static Battle Build(
        ContentLibrary library, string encounterId, ulong seed = 20260920, RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var definition = library.GetEncounter(encounterId)
            ?? throw new ArgumentException($"No encounter called '{encounterId}'.", nameof(encounterId));

        var field = new Battlefield(definition.Width, definition.Height);
        foreach (var square in definition.Blocked)
        {
            field.Block(new GridSquare(square.X, square.Y));
        }

        foreach (var square in definition.Difficult)
        {
            field.MakeDifficult(new GridSquare(square.X, square.Y));
        }

        var party = new List<Creature>();
        var foes = new List<Creature>();

        foreach (var placement in definition.Placements)
        {
            // A missing creature is already in library.Problems; skipping keeps the rest of the
            // fight loadable instead of trading one bad id for no game at all.
            if (library.BuildCreature(placement.CreatureId, rules, placement.Name) is not { } creature)
            {
                continue;
            }

            field.Place(creature, placement.X, placement.Y);
            (placement.Party ? party : foes).Add(creature);
        }

        return new Battle(party, foes, new PcgRandom(seed, DiceStream), rules, field);
    }

    /// <summary>An AI to drive a battle, thinking on its own stream.</summary>
    public static IActionSource AutoPilot(Battle battle, ulong seed = 20260920, int competence = 100) =>
        new HeuristicActionSource(battle, new PcgRandom(seed, ThoughtStream), competence);
}
