using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Encounters.Actions;

/// <summary>
/// Walking somewhere. Costs a move action and no more than the creature's speed in ground.
/// </summary>
/// <remarks>
/// Movement needs no budget of its own: a move action buys up to your speed, and a double move is
/// simply two move actions — which the standard-spent-as-a-move rule already allows.
/// </remarks>
public sealed class MoveAction : GameAction
{
    /// <param name="path">Squares walked, beginning with the one the creature already stands in.</param>
    public MoveAction(IReadOnlyList<GridSquare> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }

    public IReadOnlyList<GridSquare> Path { get; }

    public GridSquare? Destination => Path.Count > 0 ? Path[^1] : null;

    public override string Name => "move";

    public override ActionCost Cost => ActionCost.Move;

    /// <summary>
    /// Finds as much of the way towards <paramref name="target"/> as this turn's speed pays for,
    /// or null when there is nowhere worth going.
    /// </summary>
    public static MoveAction? Towards(Battlefield field, Creature mover, Creature target)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(target);

        var path = field.FindApproach(mover, target, mover.CurrentSpeed);
        return path.Count > 1 ? new MoveAction(path) : null;
    }

    public override bool CanPerform(ActionContext context)
    {
        if (context.Encounter.Battlefield is not { } field || Path.Count < 2)
        {
            return false;
        }

        if (field.SquareOf(context.Actor) != Path[0] || !field.IsFree(Path[^1]))
        {
            return false;
        }

        for (var i = 1; i < Path.Count; i++)
        {
            // Every step has to be to a touching square that is actually walkable.
            if (!Distance.AreAdjacent(Path[i - 1], Path[i]) || !field.IsPassable(Path[i]))
            {
                return false;
            }
        }

        return field.PathCost(Path) <= context.Actor.CurrentSpeed;
    }

    public override ActionResult Perform(ActionContext context)
    {
        var field = context.Encounter.Battlefield!;
        var cost = field.PathCost(Path);

        field.Place(context.Actor, Path[^1]);

        return new ActionResult(
            this,
            context.Actor,
            $"{context.Actor.Name} moves to {Path[^1]} ({cost} ft of {context.Actor.CurrentSpeed})");
    }
}
