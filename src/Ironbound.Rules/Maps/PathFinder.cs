using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Maps;

/// <summary>
/// A* over the battlefield.
/// </summary>
/// <remarks>
/// The wrinkle that makes this worth its own file: because diagonals alternate between 5 and 10
/// feet, <b>the cost of a step depends on the path taken to reach it</b>, not just on the square.
/// The search therefore runs over (square, diagonal parity) rather than square alone. Ignoring
/// that produces routes that price themselves too cheaply — the sort of bug that surfaces much
/// later as "my move went one square further than it should have".
/// </remarks>
internal static class PathFinder
{
    private readonly record struct Node(GridSquare Square, int Parity);

    public static IReadOnlyList<GridSquare> Find(
        Battlefield field,
        GridSquare from,
        GridSquare to,
        Creature? mover = null)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (!field.IsPassable(from) || !field.IsPassable(to))
        {
            return [];
        }

        if (from == to)
        {
            return [from];
        }

        var start = new Node(from, 0);
        var cameFrom = new Dictionary<Node, Node>();
        var cost = new Dictionary<Node, int> { [start] = 0 };
        var queue = new PriorityQueue<Node, int>();
        queue.Enqueue(start, Distance.Between(from, to));

        while (queue.TryDequeue(out var current, out _))
        {
            if (current.Square == to)
            {
                return Rebuild(cameFrom, current);
            }

            foreach (var neighbour in Neighbours(current.Square))
            {
                if (!CanEnter(field, neighbour, mover))
                {
                    continue;
                }

                // An ally's square can be walked through, but nobody rests on anyone.
                if (neighbour == to && !field.IsFree(neighbour))
                {
                    continue;
                }

                var diagonals = current.Parity;
                var step = field.StepCost(current.Square, neighbour, ref diagonals);
                var next = new Node(neighbour, diagonals % 2);
                var candidate = cost[current] + step;

                if (cost.TryGetValue(next, out var known) && known <= candidate)
                {
                    continue;
                }

                cost[next] = candidate;
                cameFrom[next] = current;
                queue.Enqueue(next, candidate + Distance.Between(neighbour, to));
            }
        }

        return [];
    }

    /// <summary>
    /// You can squeeze past a friend but not through an enemy. With no mover given, anyone's
    /// square is walkable — which is how the battlefield behaved before it knew about sides.
    /// </summary>
    private static bool CanEnter(Battlefield field, GridSquare square, Creature? mover)
    {
        if (!field.IsPassable(square))
        {
            return false;
        }

        if (mover is null || field.OccupantOf(square) is not { } occupant)
        {
            return true;
        }

        return !mover.IsEnemyOf(occupant);
    }

    private static IEnumerable<GridSquare> Neighbours(GridSquare square)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0)
                {
                    yield return new GridSquare(square.X + dx, square.Y + dy);
                }
            }
        }
    }

    private static List<GridSquare> Rebuild(Dictionary<Node, Node> cameFrom, Node end)
    {
        var path = new List<GridSquare> { end.Square };
        var current = end;

        while (cameFrom.TryGetValue(current, out var previous))
        {
            path.Add(previous.Square);
            current = previous;
        }

        path.Reverse();
        return path;
    }
}
