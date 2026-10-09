using Ironbound.Rules;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Content;
using Ironbound.Rules.Persistence;

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

    public Combatant Combatant => Turn.Combatant;

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

    private Battle(Encounter encounter)
    {
        Encounter = encounter;

        var creatures = encounter.Order.Select(combatant => combatant.Creature).ToArray();
        Party = [.. creatures.Where(c => c.Allegiance == PartyAllegiance)];
        Foes = [.. creatures.Where(c => c.Allegiance != PartyAllegiance)];
    }

    /// <summary>
    /// Rebuilds a fight from a save. Sides come back off the creatures themselves, which is one
    /// of the things moving allegiance into the rules layer bought.
    /// </summary>
    /// <remarks>
    /// One thing does <em>not</em> travel: the action source's own random stream. At full
    /// competence the heuristic never draws from it, so a reloaded fight plays out identically —
    /// but a deliberately fallible opponent would diverge, and its state would have to be saved
    /// alongside everything else.
    /// </remarks>
    public static Battle Restore(SavedGame save, ContentLibrary library)
    {
        ArgumentNullException.ThrowIfNull(save);
        return new Battle(GameSave.Restore(save, library));
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
    /// Opens the next creature's turn and returns it, or null once the fight is decided. The
    /// turn stays open until <see cref="EndTurn"/>, which is what lets a person take as long as
    /// they like over it.
    /// </summary>
    public BattleTurn? BeginTurn()
    {
        if (Outcome != BattleOutcome.InProgress || Encounter.BeginNextTurn() is not { } turn)
        {
            return null;
        }

        var lines = turn.Events.Select(happened => happened.Description).ToList();
        _log.AddRange(lines);

        return new BattleTurn(turn, lines);
    }

    /// <summary>
    /// Takes one action on the open turn and returns the lines it produced. Empty means the
    /// action was refused — out of reach, unaffordable, nothing left to spend it on.
    /// </summary>
    /// <summary>
    /// Whether <see cref="Act"/> would do anything, so an interface can grey out or colour in
    /// what it offers without guessing at the rules.
    /// </summary>
    public bool CanAct(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return Encounter.Current is { IsEnded: false } turn && turn.CanTake(action);
    }

    /// <summary>
    /// What the last action that went through actually did, or null if it was refused.
    /// </summary>
    /// <remarks>
    /// The log lines are for reading; this is for showing. A screen that wants to walk a figure
    /// down the path it took, or drop a body on the blow that killed it, needs the path and the
    /// blow rather than a sentence about them.
    /// </remarks>
    public ActionResult? LastResult { get; private set; }

    public IReadOnlyList<string> Act(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        LastResult = null;

        if (Encounter.Current is not { IsEnded: false } turn || turn.Take(action) is not { } result)
        {
            return [];
        }

        LastResult = result;

        var lines = Describe(result);
        _log.AddRange(lines);
        return lines;
    }

    public void EndTurn() => Encounter.Current?.End();

    /// <summary>
    /// Resolves an ambush: one Stealth check for those lying in wait, one Perception check each
    /// for everybody else. Whoever fails walks into it and loses their first turn.
    /// </summary>
    /// <remarks>
    /// The hiders roll once between them rather than each — the check is against the ambush,
    /// not against each individual goblin, and rolling per hider would mean the more of them
    /// there were the likelier you were to spot one, which is backwards.
    /// </remarks>
    public IReadOnlyList<string> Ambush(IReadOnlyList<Creature> hiding)
    {
        ArgumentNullException.ThrowIfNull(hiding);

        if (hiding.Count == 0)
        {
            return [];
        }

        var lines = new List<string>();
        var stealth = hiding
            .Select(hider => hider.Skills.Check(Skill.Stealth, Encounter.Random))
            .MaxBy(check => check.Total)!;

        lines.Add($"{stealth} — lying in wait");

        foreach (var combatant in Encounter.Order)
        {
            var creature = combatant.Creature;
            if (hiding.Contains(creature))
            {
                continue;
            }

            var noticed = creature.Skills.Check(Skill.Perception, Encounter.Random, stealth.Total);
            lines.Add(noticed.ToString());

            if (noticed.Succeeded != true)
            {
                Encounter.Surprise(creature);
                lines.Add($"  {creature.Name} is taken unawares");
            }
        }

        _log.AddRange(lines);
        return lines;
    }

    /// <summary>
    /// Opens the fight with these creatures asleep: no turns and no Dexterity until they are
    /// hurt or the third round begins.
    /// </summary>
    /// <remarks>
    /// A condition rather than a second kind of surprise. Being unaware lasts one turn and is
    /// gone; sleep has to outlast a turn, survive a save, and end early on a blow, and the
    /// effect clock already does the first two of those for every other condition.
    /// </remarks>
    public IReadOnlyList<string> Lull(IReadOnlyList<Creature> sleepers)
    {
        ArgumentNullException.ThrowIfNull(sleepers);

        var lines = sleepers
            .Select(sleeper => $"{sleeper.Name} is asleep ({Sleep.Fall(sleeper).Effect.Duration})")
            .ToList();

        _log.AddRange(lines);
        return lines;
    }

    /// <summary>Whether the open turn belongs to somebody on the player's side.</summary>
    public bool IsPartyTurn =>
        Encounter.Current is { IsEnded: false } turn && SideOf(turn.Actor) == Side.Party;

    /// <summary>
    /// Whether the open turn is one the player actually has a decision to make on.
    /// </summary>
    /// <remarks>
    /// Belonging to the party is not enough. A hero who has been cut down still gets a turn —
    /// their effects tick, their bleeding continues — but there is nothing to decide, and
    /// handing it to the player means making them click past a corpse once a round. The same
    /// goes for anyone the ambush caught: a full action budget and not one legal thing to
    /// spend it on.
    /// </remarks>
    public bool NeedsPlayer =>
        IsPartyTurn && Encounter.Current is { Combatant.CanAct: true };

    /// <summary>
    /// Runs a whole turn from beginning to end, asking the source what to do. The way an
    /// AI-controlled creature takes its turn, and the way every test drives a fight.
    /// </summary>
    public BattleTurn? AdvanceTurn(IActionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (BeginTurn() is not { } started)
        {
            return null;
        }

        var lines = new List<string>(started.Lines);

        // The loop lives here, not in the rules, so nothing ever waits on a decision.
        while (Encounter.Current is { IsEnded: false } turn && source.NextAction(turn) is { } action)
        {
            var produced = Act(action);
            if (produced.Count == 0)
            {
                break;
            }

            lines.AddRange(produced);
        }

        EndTurn();
        return new BattleTurn(started.Turn, lines);
    }

    /// <summary>
    /// Opportunities happen inside somebody else's action and a spell's effect happens to several
    /// creatures at once; neither reaches the log unless it is unpacked here.
    /// </summary>
    private static List<string> Describe(ActionResult result)
    {
        var lines = new List<string> { result.Description };

        if (result is MoveActionResult { Opportunities.Count: > 0 } moved)
        {
            lines.AddRange(moved.Opportunities.Select(strike => $"  {strike}"));
        }

        if (result is StandUpResult { Opportunities.Count: > 0 } stood)
        {
            lines.AddRange(stood.Opportunities.Select(strike => $"  {strike}"));
        }

        if (result is ManeuverActionResult { Opportunities.Count: > 0 } grappled)
        {
            lines.AddRange(grappled.Opportunities.Select(strike => $"  {strike}"));
        }

        if (result is AttackActionResult { Opportunities.Count: > 0 } swung)
        {
            lines.AddRange(swung.Opportunities.Select(strike => $"  {strike}"));
        }

        if (result is FullAttackResult full)
        {
            lines.AddRange(full.Opportunities.Select(strike => $"  {strike}"));
            lines.AddRange(full.Strikes.Select(strike => $"  {strike}"));
        }

        if (result is CastSpellResult cast)
        {
            lines.AddRange(cast.Opportunities.Select(strike => $"  {strike}"));
            lines.AddRange(cast.Cast?.Targets.Select(hit => $"  {hit}") ?? []);
        }

        return lines;
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
