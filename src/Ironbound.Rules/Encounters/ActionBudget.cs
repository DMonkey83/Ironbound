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
    public bool HasStandard { get; private set; } = true;

    public bool HasMove { get; private set; } = true;

    public bool HasSwift { get; private set; } = true;

    public bool IsSpent => !HasStandard && !HasMove && !HasSwift;

    public bool CanAfford(ActionCost cost) => cost switch
    {
        ActionCost.Free => true,
        ActionCost.Swift => HasSwift,
        ActionCost.Move => HasMove || HasStandard,
        ActionCost.Standard => HasStandard,
        ActionCost.FullRound => HasStandard && HasMove,
        _ => false,
    };

    /// <summary>Spends the cost if it can be afforded. Returns false and changes nothing if not.</summary>
    public bool Spend(ActionCost cost)
    {
        if (!CanAfford(cost))
        {
            return false;
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

    public void Reset()
    {
        HasStandard = true;
        HasMove = true;
        HasSwift = true;
    }

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

        return parts.Count == 0 ? "nothing left" : string.Join(" + ", parts);
    }
}
