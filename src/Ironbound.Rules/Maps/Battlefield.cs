using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Maps;

/// <summary>
/// Where the fight happens: the ground, what blocks it, and who is standing where.
/// </summary>
/// <remarks>
/// A creature may squeeze <em>through</em> an ally's square but never stop on one, and never
/// pass through an enemy at all. Routes found without naming a mover treat every occupied square
/// as walkable, which is how this behaved before creatures knew whose side they were on.
/// </remarks>
public sealed class Battlefield
{
    private readonly HashSet<GridSquare> _blocked = [];
    private readonly HashSet<GridSquare> _difficult = [];
    private readonly Dictionary<Creature, GridSquare> _squares = [];
    private readonly Dictionary<GridSquare, Creature> _occupants = [];

    // Dictionary enumeration order is not a guarantee, and anything that walks the creatures —
    // who a burst catches, which ally is flanking — would then depend on it. That is exactly the
    // sort of thing that survives every test and then diverges after a save is reloaded.
    private readonly List<Creature> _arrivals = [];

    public Battlefield(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;
    }

    /// <summary>In squares, not feet.</summary>
    public int Width { get; }

    public int Height { get; }

    /// <summary>Everyone on the map, in the order they arrived on it.</summary>
    public IReadOnlyList<Creature> Creatures => _arrivals;

    public bool Contains(GridSquare square) =>
        square.X >= 0 && square.Y >= 0 && square.X < Width && square.Y < Height;

    public Battlefield Block(GridSquare square)
    {
        _blocked.Add(square);
        return this;
    }

    public bool IsBlocked(GridSquare square) => _blocked.Contains(square);

    /// <summary>Rubble, undergrowth, a body — costs double to enter.</summary>
    public Battlefield MakeDifficult(GridSquare square)
    {
        _difficult.Add(square);
        return this;
    }

    public bool IsDifficult(GridSquare square) => _difficult.Contains(square);

    /// <summary>On the map and not walled off. May still be occupied.</summary>
    public bool IsPassable(GridSquare square) => Contains(square) && !IsBlocked(square);

    /// <summary>Somewhere a creature could actually come to rest.</summary>
    public bool IsFree(GridSquare square) => IsPassable(square) && !_occupants.ContainsKey(square);

    public void Place(Creature creature, GridSquare square)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!IsPassable(square))
        {
            throw new ArgumentException($"{square} is off the map or blocked.", nameof(square));
        }

        if (_occupants.TryGetValue(square, out var sitting) && !ReferenceEquals(sitting, creature))
        {
            throw new ArgumentException($"{square} is already held by {sitting.Name}.", nameof(square));
        }

        Remove(creature);
        _squares[creature] = square;
        _occupants[square] = creature;
        _arrivals.Add(creature);
    }

    public void Place(Creature creature, int x, int y) => Place(creature, new GridSquare(x, y));

    public bool Remove(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!_squares.Remove(creature, out var square))
        {
            return false;
        }

        _occupants.Remove(square);
        _arrivals.Remove(creature);
        return true;
    }

    public GridSquare? SquareOf(Creature creature) =>
        _squares.TryGetValue(creature, out var square) ? square : null;

    public Position? PositionOf(Creature creature) => SquareOf(creature)?.Centre;

    public Creature? OccupantOf(GridSquare square) =>
        _occupants.TryGetValue(square, out var creature) ? creature : null;

    /// <summary>Null when either creature is not on the map.</summary>
    public int? DistanceInFeet(Creature from, Creature to)
    {
        if (SquareOf(from) is not { } a || SquareOf(to) is not { } b)
        {
            return null;
        }

        return Distance.Between(a, b);
    }

    /// <summary>
    /// Whether <paramref name="attacker"/> can touch <paramref name="target"/>. A creature that
    /// is not on the map is not constrained by it, so this is true when either is unplaced.
    /// </summary>
    public bool IsWithinReach(Creature attacker, Creature target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        return DistanceInFeet(attacker, target) is not { } feet || feet <= attacker.Reach;
    }

    /// <summary>Everyone standing within <paramref name="feet"/> of a point, in placement order.</summary>
    public IReadOnlyList<Creature> CreaturesWithin(GridSquare centre, int feet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(feet);

        return [.. _arrivals.Where(creature =>
            _squares.TryGetValue(creature, out var square)
            && Distance.Between(square, centre) <= feet)];
    }

    /// <summary>
    /// Whether <paramref name="creature"/> could make a melee attack into
    /// <paramref name="square"/>. Its own square does not count, and a creature that cannot act
    /// threatens nothing at all.
    /// </summary>
    public bool Threatens(Creature creature, GridSquare square)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (!creature.IsConscious || creature.Reach <= 0 || SquareOf(creature) is not { } standing)
        {
            return false;
        }

        return standing != square && Distance.Between(standing, square) <= creature.Reach;
    }

    /// <summary>Every square a creature could strike into.</summary>
    public IEnumerable<GridSquare> ThreatenedBy(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (SquareOf(creature) is not { } standing || creature.Reach <= 0 || !creature.IsConscious)
        {
            yield break;
        }

        var ring = creature.Reach / Distance.FeetPerSquare;
        for (var dx = -ring; dx <= ring; dx++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                var square = new GridSquare(standing.X + dx, standing.Y + dy);
                if (Contains(square) && Threatens(creature, square))
                {
                    yield return square;
                }
            }
        }
    }

    /// <summary>
    /// Whether two allies have <paramref name="target"/> between them.
    /// </summary>
    /// <remarks>
    /// "Directly opposite" is one line of arithmetic: the two flankers' squares have to average
    /// to the target's. That holds for reach weapons too — (3,5) and (7,5) flank a target at
    /// (5,5) exactly as (4,5) and (6,5) do.
    /// </remarks>
    public bool AreFlanking(Creature a, Creature b, Creature target)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(target);

        if (!a.IsAllyOf(b) || !a.IsEnemyOf(target) || !b.IsEnemyOf(target))
        {
            return false;
        }

        if (SquareOf(a) is not { } first || SquareOf(b) is not { } second
            || SquareOf(target) is not { } middle)
        {
            return false;
        }

        if (!Threatens(a, middle) || !Threatens(b, middle))
        {
            return false;
        }

        return first.X + second.X == 2 * middle.X && first.Y + second.Y == 2 * middle.Y;
    }

    /// <summary>
    /// Whether standing in <paramref name="square"/> would put an ally of
    /// <paramref name="mover"/> directly opposite <paramref name="target"/> — asked of a square
    /// nobody is standing in yet, so a creature can work out where to go before going there.
    /// </summary>
    public bool WouldFlankFrom(GridSquare square, Creature mover, Creature target)
    {
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(target);

        if (SquareOf(target) is not { } middle || Distance.Between(square, middle) > mover.Reach)
        {
            return false;
        }

        var opposite = new GridSquare((2 * middle.X) - square.X, (2 * middle.Y) - square.Y);

        return OccupantOf(opposite) is { } ally
            && ally.IsAllyOf(mover)
            && ally.IsEnemyOf(target)
            && Threatens(ally, middle);
    }

    /// <summary>An ally positioned opposite <paramref name="attacker"/>, if there is one.</summary>
    public Creature? FindFlankingPartner(Creature attacker, Creature target)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var other in _arrivals)
        {
            if (AreFlanking(attacker, other, target))
            {
                return other;
            }
        }

        return null;
    }

    /// <summary>
    /// Cost in feet of walking a path, where the path starts at the square already occupied.
    /// A single step's price is not a property of that step: a diagonal costs 5 or 10 depending
    /// on how many diagonals the path has already spent, which is why the count is carried along
    /// rather than recomputed.
    /// </summary>
    public int PathCost(IReadOnlyList<GridSquare> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var total = 0;
        var diagonals = 0;

        for (var i = 1; i < path.Count; i++)
        {
            total += StepCost(path[i - 1], path[i], ref diagonals);
        }

        return total;
    }

    /// <summary>Cost of one step, advancing the running diagonal count.</summary>
    internal int StepCost(GridSquare from, GridSquare to, ref int diagonals)
    {
        int cost;
        if (Distance.IsDiagonalStep(from, to))
        {
            cost = diagonals % 2 == 0 ? Distance.FeetPerSquare : Distance.FeetPerSquare * 2;
            diagonals++;
        }
        else
        {
            cost = Distance.FeetPerSquare;
        }

        return IsDifficult(to) ? cost * 2 : cost;
    }

    /// <summary>The cheapest route, starting at <paramref name="from"/>. Empty when there is none.</summary>
    /// <param name="mover">Optional. Given one, enemies block the way and allies do not.</param>
    public IReadOnlyList<GridSquare> FindPath(GridSquare from, GridSquare to, Creature? mover = null) =>
        PathFinder.Find(this, from, to, mover);

    /// <summary>
    /// As much of the route towards <paramref name="target"/> as <paramref name="feetAvailable"/>
    /// pays for, stopping on a square that is free and no further than the mover's reach.
    /// Empty when there is nowhere useful to go.
    /// </summary>
    public IReadOnlyList<GridSquare> FindApproach(Creature mover, Creature target, int feetAvailable)
    {
        ArgumentNullException.ThrowIfNull(mover);
        ArgumentNullException.ThrowIfNull(target);

        if (SquareOf(mover) is not { } from || SquareOf(target) is not { } to)
        {
            return [];
        }

        if (Distance.Between(from, to) <= mover.Reach)
        {
            return [];
        }

        // Aim beside the target rather than at it: its own square is occupied, so no route can
        // legally end there.
        var ring = Math.Max(1, mover.Reach / Distance.FeetPerSquare);

        IReadOnlyList<GridSquare> nearest = [];
        var nearestCost = int.MaxValue;
        IReadOnlyList<GridSquare> flanking = [];
        var flankingCost = int.MaxValue;

        for (var dx = -ring; dx <= ring; dx++)
        {
            for (var dy = -ring; dy <= ring; dy++)
            {
                var candidate = new GridSquare(to.X + dx, to.Y + dy);
                if (candidate == to || !IsFree(candidate)
                    || Distance.Between(candidate, to) > mover.Reach)
                {
                    continue;
                }

                var route = PathFinder.Find(this, from, candidate, mover);
                if (route.Count < 2)
                {
                    continue;
                }

                var cost = PathCost(route);
                if (cost < nearestCost)
                {
                    nearestCost = cost;
                    nearest = route;
                }

                if (cost < flankingCost && WouldFlankFrom(candidate, mover, target))
                {
                    flankingCost = cost;
                    flanking = route;
                }
            }
        }

        // Going round the far side is worth a few extra feet: it is +2 on every swing after.
        if (flanking.Count > 1 && flankingCost <= feetAvailable)
        {
            return flanking;
        }

        if (nearest.Count < 2)
        {
            return [];
        }

        return nearestCost <= feetAvailable ? nearest : AsFarAs(nearest, feetAvailable);
    }

    /// <summary>The longest affordable prefix of a route that still ends somewhere standable.</summary>
    private IReadOnlyList<GridSquare> AsFarAs(IReadOnlyList<GridSquare> route, int feetAvailable)
    {
        var walked = new List<GridSquare> { route[0] };
        var furthest = 0;
        var spent = 0;
        var diagonals = 0;

        for (var i = 1; i < route.Count; i++)
        {
            spent += StepCost(route[i - 1], route[i], ref diagonals);
            if (spent > feetAvailable)
            {
                break;
            }

            walked.Add(route[i]);
            if (IsFree(route[i]))
            {
                furthest = walked.Count - 1;
            }
        }

        return furthest >= 1 ? walked[..(furthest + 1)] : [];
    }

    public override string ToString() =>
        $"{Width}x{Height} squares, {_squares.Count} creatures, {_blocked.Count} blocked";
}
