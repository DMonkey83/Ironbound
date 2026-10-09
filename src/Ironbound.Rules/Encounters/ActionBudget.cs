using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Encounters;

/// <summary>What an action costs out of a turn.</summary>
public enum ActionCost
{
    /// <summary>Dropping something, speaking. Unlimited.</summary>
    Free,

    /// <summary>One a turn, on top of everything else.</summary>
    Swift,

    Move,

    Standard,

    /// <summary>Consumes the standard and the move together.</summary>
    FullRound,
}

public static class ActionCosts
{
    /// <summary>
    /// How long the action occupies its actor once time runs continuously. A round is six
    /// seconds and a turn is a standard plus a move, so each is half of one.
    /// </summary>
    /// <remarks>
    /// Unused by the turn-based scheduler, which resolves a whole turn at a single instant.
    /// It exists so that actions are already timed when a real-time scheduler needs them —
    /// the alternative is rewriting every action later.
    /// </remarks>
    public static Duration DefaultDuration(ActionCost cost) => cost switch
    {
        ActionCost.Standard => Duration.FromTicks(Duration.TicksPerRound / 2),
        ActionCost.Move => Duration.FromTicks(Duration.TicksPerRound / 2),
        ActionCost.FullRound => Duration.Rounds(1),
        _ => Duration.Zero,
    };
}

/// <summary>
/// What a creature has left this turn: a standard action, a move action and a swift action.
/// </summary>
/// <remarks>
/// The one rule that is easy to miss: a standard action may be spent <em>as</em> a move, which is
/// how a creature takes two moves in a turn. The reverse is never allowed.
/// </remarks>
public sealed class ActionBudget
{
    /// <summary>
    /// Whether the creature is down to a single action a round.
    /// </summary>
    /// <remarks>
    /// What being <em>disabled</em> — at exactly zero hit points — costs you. One move or one
    /// standard action, and no full attack at all. Without it a creature on nought hit points
    /// fights exactly as well as one on full, which makes the whole death's-door band
    /// meaningless.
    /// </remarks>
    public bool IsSingleAction { get; private set; }

    public bool HasStandard { get; private set; } = true;

    public bool HasMove { get; private set; } = true;

    public bool HasSwift { get; private set; } = true;

    public bool IsSpent => !HasStandard && !HasMove && !HasSwift;

    public bool CanAfford(ActionCost cost)
    {
        if (IsSingleAction)
        {
            // One thing, then nothing. A full attack is not one thing.
            return cost switch
            {
                ActionCost.Free => true,
                ActionCost.Swift => HasSwift,
                ActionCost.Move or ActionCost.Standard => HasStandard && HasMove,
                _ => false,
            };
        }

        return cost switch
        {
            ActionCost.Free => true,
            ActionCost.Swift => HasSwift,
            ActionCost.Move => HasMove || HasStandard,
            ActionCost.Standard => HasStandard,
            ActionCost.FullRound => HasStandard && HasMove,
            _ => false,
        };
    }

    /// <summary>Spends the cost if it can be afforded. Returns false and changes nothing if not.</summary>
    public bool Spend(ActionCost cost)
    {
        if (!CanAfford(cost))
        {
            return false;
        }

        if (IsSingleAction && cost is ActionCost.Move or ActionCost.Standard)
        {
            // Whichever of the two it was, it was the only one.
            HasStandard = false;
            HasMove = false;
            return true;
        }

        switch (cost)
        {
            case ActionCost.Free:
                break;

            case ActionCost.Swift:
                HasSwift = false;
                break;

            case ActionCost.Move:
                if (HasMove)
                {
                    HasMove = false;
                }
                else
                {
                    HasStandard = false;
                }

                break;

            case ActionCost.Standard:
                HasStandard = false;
                break;

            case ActionCost.FullRound:
                HasStandard = false;
                HasMove = false;
                break;
        }

        return true;
    }

    /// <summary>Puts a budget back exactly as a save recorded it.</summary>
    internal void Restore(bool standard, bool move, bool swift)
    {
        HasStandard = standard;
        HasMove = move;
        HasSwift = swift;
    }

    public void Reset()
    {
        HasStandard = true;
        HasMove = true;
        HasSwift = true;
        IsSingleAction = false;
    }

    /// <summary>Gives up the swift action alone, as an immediate action taken before the turn does.</summary>
    public void SpendSwift() => HasSwift = false;

    /// <summary>Cuts the turn down to one action, as being disabled does.</summary>
    public void RestrictToSingleAction() => IsSingleAction = true;

    /// <summary>Gives up whatever is left, as ending a turn early does.</summary>
    public void SpendAll()
    {
        HasStandard = false;
        HasMove = false;
        HasSwift = false;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (HasStandard)
        {
            parts.Add("standard");
        }

        if (HasMove)
        {
            parts.Add("move");
        }

        if (HasSwift)
        {
            parts.Add("swift");
        }

        if (IsSingleAction && parts.Count > 0)
        {
            parts.Add("disabled: one action only");
        }

        return parts.Count == 0 ? "nothing left" : string.Join(" + ", parts);
    }
}
