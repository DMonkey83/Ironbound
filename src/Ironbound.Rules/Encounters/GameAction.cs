using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// Something a creature can do on its turn, as an object rather than a function call.
/// </summary>
/// <remarks>
/// The phase timings below are the reason this is a class at all. A turn-based scheduler resolves
/// a whole turn at a single instant and never reads them — but an action modelled as
/// <c>Attack(attacker, target)</c> returning immediately cannot be interrupted, queued or
/// interleaved, and every one would have to be rewritten for a real-time scheduler. Declaring the
/// timings now costs nothing and keeps that door open.
/// </remarks>
public abstract class GameAction
{
    public abstract string Name { get; }

    public abstract ActionCost Cost { get; }

    /// <summary>How long the actor is busy once time runs continuously.</summary>
    public virtual Duration Occupies => ActionCosts.DefaultDuration(Cost);

    /// <summary>
    /// How far into <see cref="Occupies"/> the action actually happens — the wind-up before a
    /// blade lands. Zero means it resolves the moment it begins.
    /// </summary>
    public virtual Duration ResolvesAt => Duration.Zero;

    /// <summary>Whether the action makes sense right now. Checked before the budget is spent.</summary>
    public virtual bool CanPerform(ActionContext context) => true;

    public abstract ActionResult Perform(ActionContext context);

    public override string ToString() => Name;
}

/// <summary>What an action is handed when it runs.</summary>
public sealed class ActionContext
{
    internal ActionContext(Turn turn) => Turn = turn;

    public Turn Turn { get; }

    public Combatant Combatant => Turn.Combatant;

    public Creature Actor => Turn.Actor;

    public Encounter Encounter => Turn.Encounter;

    public IRandomSource Random => Encounter.Random;

    public RuleOptions Rules => Encounter.Rules;
}

/// <summary>
/// What an action did. Actions with a richer outcome derive from this rather than smuggling one
/// through an <c>object</c>, so callers can pattern-match for the detail they care about.
/// </summary>
public record ActionResult(GameAction Action, Creature Actor, string Description)
{
    public ActionCost Cost => Action.Cost;

    /// <summary>
    /// Sealed on purpose: a derived record regenerates <c>ToString</c> as a dump of its members,
    /// which would quietly replace the log line every subclass is supposed to inherit.
    /// </summary>
    public sealed override string ToString() => Description;
}
