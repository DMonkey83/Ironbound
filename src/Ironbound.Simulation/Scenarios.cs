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
        ContentLibrary library, string encounterId, ulong seed = 20260920, RuleOptions? rules = null) =>
        Build(library, encounterId, seed, rules, roster: null);

    /// <summary>
    /// Builds a fight, optionally reusing party members who have been here before.
    /// </summary>
    /// <param name="roster">
    /// Party creatures already alive, keyed by the content id they were built from. A campaign
    /// passes the same dictionary to every chapter, which is what carries wounds and spent
    /// spell slots from one fight into the next; anyone missing is built fresh and remembered.
    /// </param>
    internal static Battle Build(
        ContentLibrary library,
        string encounterId,
        ulong seed,
        RuleOptions? rules,
        Dictionary<string, Creature>? roster)
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
            var creature = placement.Party && roster is not null
                ? Enlist(library, roster, placement, rules)

                // A missing creature is already in library.Problems; skipping keeps the rest of
                // the fight loadable instead of trading one bad id for no game at all.
                : library.BuildCreature(placement.CreatureId, rules, placement.Name);

            if (creature is null)
            {
                continue;
            }

            field.Place(creature, placement.X, placement.Y);
            (placement.Party ? party : foes).Add(creature);
        }

        var battle = new Battle(party, foes, new PcgRandom(seed, DiceStream), rules, field);

        // Anybody the encounter says was lying in wait gets one chance to have been missed.
        var hiding = definition.Placements
            .Where(placement => placement.Hidden)
            .Select(placement => field.OccupantOf(new GridSquare(placement.X, placement.Y)))
            .OfType<Creature>()
            .ToList();

        battle.Ambush(hiding);

        return battle;
    }

    /// <summary>
    /// The same person who fought the last chapter, or a new one on their first outing.
    /// </summary>
    private static Creature? Enlist(
        ContentLibrary library,
        Dictionary<string, Creature> roster,
        PlacementDefinition placement,
        RuleOptions? rules)
    {
        if (roster.TryGetValue(placement.CreatureId, out var veteran))
        {
            return veteran;
        }

        if (library.BuildCreature(placement.CreatureId, rules, placement.Name) is not { } recruit)
        {
            return null;
        }

        roster[placement.CreatureId] = recruit;
        return recruit;
    }

    /// <summary>An AI to drive a battle, thinking on its own stream.</summary>
    public static IActionSource AutoPilot(Battle battle, ulong seed = 20260920, int competence = 100) =>
        new HeuristicActionSource(battle, new PcgRandom(seed, ThoughtStream), competence);
}
