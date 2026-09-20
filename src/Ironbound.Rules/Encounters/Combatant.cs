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

    public override string ToString() => $"{Creature.Name} (initiative {Initiative})";
}
