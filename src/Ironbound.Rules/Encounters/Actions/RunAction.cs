using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Running: the whole round spent covering four times the creature's speed in a straight line
/// — three times in heavy armour — and no Dexterity to armour class until its next turn.
/// </summary>
/// <remarks>
/// The Run feat makes it five times (four in heavy armour) and keeps the Dexterity. A straight
/// line on a grid is every step in the same direction. The fatigued and the exhausted cannot
/// run at all, nor can anybody who has already moved this turn: it is all of the turn's movement.
/// </remarks>
public sealed class RunAction(IReadOnlyList<GridSquare> path) : MoveAction(path)
{
    public override string Name => "run";

    public override ActionCost Cost => ActionCost.FullRound;

    /// <summary>How many times its speed a creature runs.</summary>
    public static int Multiple(Creature runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        var heavy = runner.Equipment.ArmourWorn == ArmourCategory.Heavy;
        var trained = runner.HasFeat(FeatEffect.Run);

        return (heavy ? 3 : 4) + (trained ? 1 : 0);
    }

    protected override int Allowance(Creature mover) => mover.CurrentSpeed * Multiple(mover);

    protected override bool AlreadyMoved(Combatant combatant) =>
        combatant.HasMoved || combatant.HasTakenFiveFootStep;

    protected override void RecordMovement(Combatant combatant)
    {
        combatant.HasMoved = true;
        combatant.IsRunning = !combatant.Creature.HasFeat(FeatEffect.Run);
    }

    public override bool CanPerform(ActionContext context)
    {
        if (context.Actor.IsProne
            || context.Actor.Has(Condition.Fatigued)
            || context.Actor.Has(Condition.Exhausted)
            || !IsStraight())
        {
            return false;
        }

        return base.CanPerform(context);
    }

    private bool IsStraight()
    {
        if (Path.Count < 2)
        {
            return false;
        }

        var dx = Path[1].X - Path[0].X;
        var dy = Path[1].Y - Path[0].Y;

        for (var i = 2; i < Path.Count; i++)
        {
            if (Path[i].X - Path[i - 1].X != dx || Path[i].Y - Path[i - 1].Y != dy)
            {
                return false;
            }
        }

        return true;
    }
}
