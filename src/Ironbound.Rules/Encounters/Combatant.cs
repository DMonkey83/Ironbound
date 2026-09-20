using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Encounters;

/// <summary>
/// A creature's place in an encounter: what it rolled, when it next acts, and what it has left
/// to spend this turn.
/// </summary>
/// <remarks>
/// <see cref="NextTurnTick"/> is the heart of the scheduler. Each combatant carries its own round
/// clock rather than sharing a global round boundary, so "until the start of your next turn"
/// means exactly that — and the same arrangement is what a real-time scheduler would need.
/// </remarks>
public sealed class Combatant
{
    internal Combatant(Creature creature, int naturalRoll, int initiative)
    {
        Creature = creature;
        NaturalRoll = naturalRoll;
        Initiative = initiative;
    }

    public Creature Creature { get; }

    /// <summary>The d20 alone, for the log.</summary>
    public int NaturalRoll { get; }

    public int Initiative { get; }

    /// <summary>Absolute tick at which this combatant next acts.</summary>
    public long NextTurnTick { get; internal set; }

    public ActionBudget Budget { get; } = new();

    /// <summary>Unconscious and dead combatants are skipped, but their effects keep running.</summary>
    public bool IsActive => Creature.IsConscious;

    /// <summary>Opportunities spent since this creature's last turn.</summary>
    public int OpportunitiesUsed { get; internal set; }

    public bool CanTakeOpportunity =>
        IsActive && OpportunitiesUsed < Creature.AttacksOfOpportunityPerRound;

    /// <summary>Set by ordinary movement. Bars a five-foot step for the rest of the turn.</summary>
    public bool HasMoved { get; internal set; }

    /// <summary>Set by a five-foot step. Bars ordinary movement for the rest of the turn.</summary>
    public bool HasTakenFiveFootStep { get; internal set; }

    /// <summary>
    /// Everything that refreshes at the start of a creature's own turn — which is exactly when
    /// the rules say the opportunity allotment comes back.
    /// </summary>
    internal void BeginTurn()
    {
        Budget.Reset();
        OpportunitiesUsed = 0;
        HasMoved = false;
        HasTakenFiveFootStep = false;
    }

    public override string ToString() => $"{Creature.Name} (initiative {Initiative})";
}
