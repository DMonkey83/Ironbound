using Ironbound.Rules;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation;

public enum Side
{
    Party,
    Foes,
}

public enum BattleOutcome
{
    InProgress,
    PartyWon,
    PartyLost,

    /// <summary>Everyone is down. Rare, but it has to mean something.</summary>
    Draw,
}

/// <summary>What one turn produced, ready for a log panel.</summary>
public sealed record BattleTurn(Turn Turn, IReadOnlyList<string> Lines)
{
    public Creature Actor => Turn.Actor;

    public int Round => Turn.Round;
}

/// <summary>
/// An encounter with sides and someone to decide what each creature does — the glue between the
/// rules and whatever is drawing them.
/// </summary>
/// <remarks>
/// This lives above <c>Ironbound.Rules</c> and below the Godot project on purpose: the
/// presentation layer should not have to drive a turn loop. Everything here is still engine-free
/// and still unit-testable.
/// <para>
/// Sides are recorded on the creatures themselves rather than in a private dictionary, because
/// the rules need them too — flanking, and every other rule that says "ally".
/// </para>
/// </remarks>
public sealed class Battle
{
    public const int PartyAllegiance = 1;
    public const int FoeAllegiance = 2;

    private readonly List<string> _log = [];

    public Battle(
        IEnumerable<Creature> party,
        IEnumerable<Creature> foes,
        IRandomSource random,
        RuleOptions? rules = null,
        Battlefield? battlefield = null)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(foes);
        ArgumentNullException.ThrowIfNull(random);

        Party = [.. party];
        Foes = [.. foes];

        foreach (var creature in Party)
        {
            creature.Allegiance = PartyAllegiance;
        }

        foreach (var creature in Foes)
        {
            creature.Allegiance = FoeAllegiance;
        }

        // Explicitly party-then-foes rather than any incidental ordering: initiative is rolled in
        // this sequence, and a replay from the same seed has to see the same one.
        Encounter = new Encounter(Party.Concat(Foes), random, rules, battlefield);
    }

    public IReadOnlyList<Creature> Party { get; }

    public IReadOnlyList<Creature> Foes { get; }

    public Encounter Encounter { get; }

    /// <summary>The ground, when the fight is happening somewhere in particular.</summary>
    public Battlefield? Battlefield => Encounter.Battlefield;

    /// <summary>Every line produced so far, oldest first.</summary>
    public IReadOnlyList<string> Log => _log;

    public int Round => Encounter.Round;

    public Side SideOf(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return creature.Allegiance == PartyAllegiance ? Side.Party : Side.Foes;
    }

    /// <summary>Everyone still standing on the other side from this creature.</summary>
    public IReadOnlyList<Creature> EnemiesOf(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return [.. Encounter.ActiveCombatants
            .Select(combatant => combatant.Creature)
            .Where(creature.IsEnemyOf)];
    }

    public bool AnyStanding(Side side) =>
        Encounter.ActiveCombatants.Any(combatant => SideOf(combatant.Creature) == side);

    public BattleOutcome Outcome => (AnyStanding(Side.Party), AnyStanding(Side.Foes)) switch
    {
        (true, true) => BattleOutcome.InProgress,
        (true, false) => BattleOutcome.PartyWon,
        (false, true) => BattleOutcome.PartyLost,
        _ => BattleOutcome.Draw,
    };

    /// <summary>
    /// Runs one creature's whole turn: begins it, keeps asking the action source until it has
    /// nothing more to say, and ends it. Returns null once the fight is decided.
    /// </summary>
    public BattleTurn? AdvanceTurn(IActionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (Outcome != BattleOutcome.InProgress || Encounter.BeginNextTurn() is not { } turn)
        {
            return null;
        }

        var lines = new List<string>();

        foreach (var happened in turn.Events)
        {
            lines.Add(happened.Description);
        }

        // The loop lives here, not in the rules, so nothing ever waits on a decision.
        while (source.NextAction(turn) is { } action)
        {
            if (turn.Take(action) is not { } result)
            {
                break;
            }

            lines.Add(result.Description);

            // An opportunity is taken inside somebody else's action, so it has to be unpacked
            // here or it never reaches the log at all.
            if (result is MoveActionResult { Opportunities.Count: > 0 } moved)
            {
                lines.AddRange(moved.Opportunities.Select(strike => $"  {strike}"));
            }
        }

        turn.End();
        _log.AddRange(lines);

        return new BattleTurn(turn, lines);
    }

    /// <summary>Runs turns until someone wins or the cap is reached, guarding against a stalemate.</summary>
    public IReadOnlyList<BattleTurn> RunToCompletion(IActionSource source, int maximumTurns = 200)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTurns, 1);

        var turns = new List<BattleTurn>();
        while (turns.Count < maximumTurns && AdvanceTurn(source) is { } turn)
        {
            turns.Add(turn);
        }

        return turns;
    }
}
