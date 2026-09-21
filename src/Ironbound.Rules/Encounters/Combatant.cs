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
    /// <summary>
    /// Whether they have had a turn yet.
    /// </summary>
    /// <remarks>
    /// Until you act you are flat-footed, which is the rule that makes initiative worth caring
    /// about and Improved Initiative worth a feat. It was simply missing: everybody started a
    /// fight with their full armour class regardless of who moved first.
    /// </remarks>
    public bool HasActed { get; internal set; }

    /// <summary>
    /// Whether they walked into this without knowing it was happening.
    /// </summary>
    /// <remarks>
    /// Set when somebody's Stealth beat their Perception. An unaware combatant is flat-footed
    /// and loses their first turn — which is what an ambush <em>is</em>, and what the opening
    /// encounter has been called since long before it could do it.
    /// </remarks>
    public bool IsUnaware { get; internal set; }

    /// <summary>Caught with their guard down: before their first turn, or surprised entirely.</summary>
    public bool IsFlatFooted => !HasActed || IsUnaware;

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

        // Standing, but only just: at exactly nought hit points you get one action a round.
        if (Creature.HitPoints.State == HitPointState.Disabled)
        {
            Budget.RestrictToSingleAction();
        }

        OpportunitiesUsed = 0;
        HasMoved = false;
        HasTakenFiveFootStep = false;
    }

    public override string ToString() => $"{Creature.Name} (initiative {Initiative})";
}
