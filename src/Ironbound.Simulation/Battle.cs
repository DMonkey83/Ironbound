using Ironbound.Rules;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
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

/// <summary>A creature and what it is swinging.</summary>
public sealed record Loadout(Creature Creature, WeaponAttack Weapon);

/// <summary>What one turn produced, ready for a log panel.</summary>
public sealed record BattleTurn(Turn Turn, IReadOnlyList<string> Lines)
{
    public Creature Actor => Turn.Actor;

    public int Round => Turn.Round;
}

/// <summary>
/// An encounter with sides, weapons and someone to decide what each creature does — the glue
/// between the rules and whatever is drawing them.
/// </summary>
/// <remarks>
/// This lives above <c>Ironbound.Rules</c> and below the Godot project on purpose. The rules know
/// nothing about factions or who is fighting whom, and the presentation layer should not have to
/// drive a turn loop. Everything here is still engine-free and still unit-testable.
/// </remarks>
public sealed class Battle
{
    private readonly Dictionary<Creature, Loadout> _loadouts = [];
    private readonly Dictionary<Creature, Side> _sides = [];
    private readonly List<string> _log = [];

    public Battle(
        IEnumerable<Loadout> party,
        IEnumerable<Loadout> foes,
        IRandomSource random,
        RuleOptions? rules = null,
        Battlefield? battlefield = null)
    {
        ArgumentNullException.ThrowIfNull(party);
        ArgumentNullException.ThrowIfNull(foes);
        ArgumentNullException.ThrowIfNull(random);

        Party = [.. party];
        Foes = [.. foes];

        foreach (var loadout in Party)
        {
            _loadouts[loadout.Creature] = loadout;
            _sides[loadout.Creature] = Side.Party;
        }

        foreach (var loadout in Foes)
        {
            _loadouts[loadout.Creature] = loadout;
            _sides[loadout.Creature] = Side.Foes;
        }

        // Explicitly party-then-foes rather than the dictionary's key order, which is not a
        // guaranteed sequence — initiative would then be rolled in an unspecified order and a
        // replay from the same seed could diverge.
        Encounter = new Encounter(
            Party.Concat(Foes).Select(loadout => loadout.Creature), random, rules, battlefield);
    }

    public IReadOnlyList<Loadout> Party { get; }

    public IReadOnlyList<Loadout> Foes { get; }

    public Encounter Encounter { get; }

    /// <summary>The ground, when the fight is happening somewhere in particular.</summary>
    public Battlefield? Battlefield => Encounter.Battlefield;

    /// <summary>Every line produced so far, oldest first.</summary>
    public IReadOnlyList<string> Log => _log;

    public int Round => Encounter.Round;

    public Side SideOf(Creature creature) => _sides[creature];

    public WeaponAttack WeaponOf(Creature creature) => _loadouts[creature].Weapon;

    /// <summary>Everyone still standing on the other side from this creature.</summary>
    public IReadOnlyList<Creature> EnemiesOf(Creature creature)
    {
        var side = SideOf(creature);
        return [.. Encounter.ActiveCombatants
            .Select(combatant => combatant.Creature)
            .Where(other => _sides[other] != side)];
    }

    public bool AnyStanding(Side side) =>
        Encounter.ActiveCombatants.Any(combatant => _sides[combatant.Creature] == side);

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
