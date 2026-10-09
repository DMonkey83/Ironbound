using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// A fight: who is in it, whose turn it is, and the clock they all share.
/// </summary>
/// <remarks>
/// Time only moves through <see cref="Advance"/>, so there is one place where effects tick and
/// one place a replay has to reproduce. "Turn" is a policy this class applies on top of that
/// clock, not something the rules underneath know about — which is why a real-time scheduler
/// could replace this class without touching anything below it.
/// <para>
/// It deliberately does not decide when the fight is over. That needs factions, which belong with
/// the map and the AI; ask <see cref="ActiveCombatants"/> and decide for yourself.
/// </para>
/// </remarks>
public sealed class Encounter
{
    private readonly List<Combatant> _combatants;

    /// <param name="battlefield">Optional. Without one there is no distance, so nothing is ever
    /// out of reach — which is exactly how every encounter behaved before the map existed.</param>
    public Encounter(
        IEnumerable<Creature> creatures,
        IRandomSource random,
        RuleOptions? rules = null,
        Battlefield? battlefield = null)
    {
        ArgumentNullException.ThrowIfNull(creatures);
        ArgumentNullException.ThrowIfNull(random);

        Random = random;
        Rules = rules ?? RuleOptions.Pathfinder;
        Battlefield = battlefield;
        _combatants = Initiative.Roll(creatures, random);

        // One tick apart, so everyone's first turn falls inside round one and the order, once
        // rolled, never drifts — each turn pushes its owner forward by exactly a round.
        for (var index = 0; index < _combatants.Count; index++)
        {
            _combatants[index].NextTurnTick = index;
        }
    }

    /// <summary>Rebuilds an encounter from a save, with the order and the clock already decided.</summary>
    internal Encounter(
        IRandomSource random,
        RuleOptions rules,
        Battlefield? battlefield,
        List<Combatant> order,
        long tick)
    {
        Random = random;
        Rules = rules;
        Battlefield = battlefield;
        _combatants = order;
        Tick = tick;
    }

    public IRandomSource Random { get; }

    public RuleOptions Rules { get; }

    /// <summary>The ground they are fighting over, or null when position does not matter.</summary>
    public Battlefield? Battlefield { get; }

    /// <summary>Absolute tick. A round is <see cref="Duration.TicksPerRound"/> of them.</summary>
    public long Tick { get; private set; }

    public int Round => (int)(Tick / Duration.TicksPerRound) + 1;

    /// <summary>Everyone, in initiative order.</summary>
    public IReadOnlyList<Combatant> Order => _combatants;

    public IEnumerable<Combatant> ActiveCombatants => _combatants.Where(combatant => combatant.IsActive);

    public Turn? Current { get; private set; }

    public Combatant? CombatantFor(Creature creature) =>
        _combatants.FirstOrDefault(combatant => ReferenceEquals(combatant.Creature, creature));

    /// <summary>
    /// Brings a summoned or arriving creature into the fight, queued for the next tick. Anyone
    /// already waiting on that tick keeps their place — an arrival joins the back of the queue
    /// rather than jumping it.
    /// </summary>
    public Combatant Add(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        var natural = Random.NextDie(Initiative.DieSides);
        var combatant = new Combatant(creature, natural, natural + Initiative.Bonus(creature))
        {
            NextTurnTick = Tick + 1,
        };

        _combatants.Add(combatant);
        return combatant;
    }

    /// <summary>
    /// Ends the current turn, moves time to whoever acts next, and hands back their turn.
    /// Null when nobody is left standing.
    /// </summary>
    public Turn? BeginNextTurn()
    {
        Current?.End();
        Current = null;

        var pending = new List<EffectEvent>();

        while (_combatants.Any(combatant => combatant.IsActive))
        {
            var next = _combatants.MinBy(combatant => combatant.NextTurnTick)!;
            pending.AddRange(AdvanceTo(next.NextTurnTick));

            if (next.IsActive)
            {
                next.BeginTurn();
                Current = new Turn(this, next, pending);
                return Current;
            }

            // Out of the fight but still on the clock, so its effects keep running and it
            // rejoins in the right place if someone wakes it up.
            next.NextTurnTick += Duration.TicksPerRound;
        }

        return null;
    }

    /// <summary>The only way time moves. Every combatant's effects advance with it.</summary>
    public IReadOnlyList<EffectEvent> Advance(Duration elapsed)
    {
        if (elapsed.IsPermanent)
        {
            throw new ArgumentException("Time cannot advance by a permanent duration.", nameof(elapsed));
        }

        if (elapsed.IsZero)
        {
            return [];
        }

        var events = new List<EffectEvent>();
        foreach (var combatant in _combatants)
        {
            // One place decides who is bleeding. Damage arrives from strikes, spells, poison
            // and falling, and none of those should have to remember to check.
            Bleeding.Sync(combatant.Creature);

            // And one place decides who has been woken by it, for the same reason.
            if (Conditions.Sleep.Sync(combatant.Creature) is { } woken)
            {
                events.Add(woken);
            }

            events.AddRange(combatant.Creature.Effects.Advance(elapsed, Random));
        }

        Tick += elapsed.Ticks;
        return events;
    }

    private IReadOnlyList<EffectEvent> AdvanceTo(long tick)
    {
        var elapsed = tick - Tick;
        return elapsed <= 0 ? [] : Advance(Duration.FromTicks(checked((int)elapsed)));
    }

    /// <summary>Whether somebody is currently caught with their guard down.</summary>
    public bool IsFlatFooted(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var combatant in Order)
        {
            if (ReferenceEquals(combatant.Creature, creature))
            {
                return combatant.IsFlatFooted;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks somebody as having walked into this without knowing. They lose their first turn
    /// and are flat-footed until they take one.
    /// </summary>
    public bool Surprise(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        foreach (var combatant in Order)
        {
            if (ReferenceEquals(combatant.Creature, creature))
            {
                combatant.IsUnaware = true;
                return true;
            }
        }

        return false;
    }

    internal void CompleteTurn(Combatant combatant) =>
        combatant.NextTurnTick += Duration.TicksPerRound;

    public override string ToString() =>
        $"round {Round}, tick {Tick}: {string.Join(", ", _combatants)}";
}
