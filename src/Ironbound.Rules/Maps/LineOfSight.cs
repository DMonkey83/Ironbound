namespace Ironbound.Rules.Maps;

/// <summary>
/// Whether one square can see another, and whether the view is obstructed enough to count as
/// cover.
/// </summary>
/// <remarks>
/// The rules draw these lines from <em>corners</em>, not from the middles of squares, and that
/// detail is the whole reason this is fiddly enough to deserve its own file. Two creatures
/// diagonally opposite a pillar can see each other down the seam beside it; centre-to-centre
/// geometry would say the pillar is squarely in the way, and every archer duel fought around a
/// column would come out wrong.
/// <para>
/// A line that runs exactly <em>along</em> the edge of a blocking square is not obstructed by it.
/// That is why every test here is against a square's open interior rather than its closed
/// rectangle: a wall you are sliding your arrow past is a wall you are not shooting through.
/// </para>
/// </remarks>
public static class LineOfSight
{
    /// <summary>
    /// Slop for comparing feet. Positions are floats, but every corner lands on an exact
    /// multiple of five, so this only ever absorbs representation error — never a real distance.
    /// </summary>
    private const float Epsilon = 1e-4f;

    /// <summary>The four corners of a square, in a fixed order so a replay traverses them alike.</summary>
    public static Position[] Corners(GridSquare square)
    {
        var left = square.X * Distance.FeetPerSquare;
        var top = square.Y * Distance.FeetPerSquare;
        var right = left + Distance.FeetPerSquare;
        var bottom = top + Distance.FeetPerSquare;

        return
        [
            new Position(left, top),
            new Position(right, top),
            new Position(left, bottom),
            new Position(right, bottom),
        ];
    }

    /// <summary>
    /// Whether the segment between two points passes through the interior of any blocking
    /// square.
    /// </summary>
    public static bool IsClear(Position from, Position to, IEnumerable<GridSquare> obstructions)
    {
        ArgumentNullException.ThrowIfNull(obstructions);

        var left = MathF.Min(from.X, to.X);
        var right = MathF.Max(from.X, to.X);
        var top = MathF.Min(from.Y, to.Y);
        var bottom = MathF.Max(from.Y, to.Y);

        foreach (var square in obstructions)
        {
            // Cheap rejection first: most walls on a map are nowhere near any given shot.
            var x = square.X * Distance.FeetPerSquare;
            var y = square.Y * Distance.FeetPerSquare;

            if (x + Distance.FeetPerSquare <= left + Epsilon || x >= right - Epsilon
                || y + Distance.FeetPerSquare <= top + Epsilon || y >= bottom - Epsilon)
            {
                continue;
            }

            if (CrossesInterior(from, to, square))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a segment passes through a square's open interior, by clipping the segment
    /// against the square's two slabs and asking whether anything of it survives.
    /// </summary>
    public static bool CrossesInterior(Position from, Position to, GridSquare square)
    {
        var left = square.X * Distance.FeetPerSquare;
        var top = square.Y * Distance.FeetPerSquare;

        var enter = 0f;
        var exit = 1f;

        return Clip(from.X, to.X - from.X, left, left + Distance.FeetPerSquare, ref enter, ref exit)
            && Clip(from.Y, to.Y - from.Y, top, top + Distance.FeetPerSquare, ref enter, ref exit)
            && exit - enter > Epsilon;
    }

    /// <summary>
    /// Narrows the surviving stretch of the segment to the part inside one slab. Returns false
    /// the moment nothing can be left.
    /// </summary>
    private static bool Clip(float start, float delta, float low, float high, ref float enter, ref float exit)
    {
        if (MathF.Abs(delta) < Epsilon)
        {
            // Parallel to this slab: either strictly inside it for the whole segment, or the
            // segment runs along the boundary and misses the interior entirely.
            return start > low + Epsilon && start < high - Epsilon;
        }

        var near = (low - start) / delta;
        var far = (high - start) / delta;

        if (near > far)
        {
            (near, far) = (far, near);
        }

        enter = MathF.Max(enter, near);
        exit = MathF.Min(exit, far);

        return enter < exit;
    }
}
