using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// One combatant's window of control.
/// </summary>
/// <remarks>
/// Nothing here asks anyone what they want to do. The caller drives the loop — a player-controlled
/// actor returns nothing until the interface queues something, an AI returns its choice — so the
/// rules layer never waits on input. That is what keeps a turn-based engine portable to real time,
/// and it is the single easiest thing to get wrong.
/// </remarks>
public sealed class Turn
{
    private readonly List<ActionResult> _taken = [];

    internal Turn(Encounter encounter, Combatant combatant, IReadOnlyList<EffectEvent> events)
    {
        Encounter = encounter;
        Combatant = combatant;
        Events = events;
        Tick = encounter.Tick;
        Round = encounter.Round;
    }

    public Encounter Encounter { get; }

    public Combatant Combatant { get; }

    public Creature Actor => Combatant.Creature;

    public ActionBudget Budget => Combatant.Budget;

    /// <summary>Absolute tick this turn began at.</summary>
    public long Tick { get; }

    public int Round { get; }

    /// <summary>
    /// What happened while time advanced to this turn — poison ticking, a buff wearing off.
    /// Reported here because the start of a creature's turn is when a player expects to see it.
    /// </summary>
    public IReadOnlyList<EffectEvent> Events { get; }

    public IReadOnlyList<ActionResult> Taken => _taken;

    public bool IsEnded { get; private set; }

    /// <summary>
    /// Attempts an action. Returns null — changing nothing — if the turn is over, the action does
    /// not make sense, or the budget cannot pay for it.
    /// </summary>
    public ActionResult? Take(GameAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsEnded)
        {
            return null;
        }

        var context = new ActionContext(this);
        if (!action.CanPerform(context) || !Budget.CanAfford(action.Cost))
        {
            return null;
        }

        Budget.Spend(action.Cost);
        var result = action.Perform(context);
        _taken.Add(result);

        return result;
    }

    /// <summary>Gives up whatever is left. Idempotent.</summary>
    public void End()
    {
        if (IsEnded)
        {
            return;
        }

        IsEnded = true;
        Budget.SpendAll();
        Encounter.CompleteTurn(Combatant);
    }

    public override string ToString() =>
        $"round {Round}, tick {Tick}: {Actor.Name} ({Budget})";
}
