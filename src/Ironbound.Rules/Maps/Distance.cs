namespace Ironbound.Rules.Maps;

/// <summary>
/// How far apart things are, measured the way the rules measure.
/// </summary>
/// <remarks>
/// Diagonals alternate: the first costs 5 feet, the second 10, the third 5 again. Four diagonal
/// squares are therefore 30 feet, not the 28.3 of straight-line geometry nor the 20 of counting
/// squares. Every speed and range in the game is written against that arithmetic, so using
/// anything else quietly rescales the whole ruleset.
/// </remarks>
public static class Distance
{
    public const int FeetPerSquare = 5;

    /// <summary>Cost in feet of <paramref name="diagonals"/> diagonal steps, alternating 5 and 10.</summary>
    public static int DiagonalCost(int diagonals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diagonals);
        return (diagonals / 2 * 15) + (diagonals % 2 * FeetPerSquare);
    }

    /// <summary>Number of steps between two squares, counting a diagonal as one step.</summary>
    public static int Steps(GridSquare from, GridSquare to) =>
        Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y));

    public static int Between(GridSquare from, GridSquare to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        var diagonals = Math.Min(dx, dy);
        var straights = Math.Max(dx, dy) - diagonals;

        return (straights * FeetPerSquare) + DiagonalCost(diagonals);
    }

    public static int Between(Position from, Position to) => Between(from.Square, to.Square);

    public static bool AreAdjacent(GridSquare a, GridSquare b) => a != b && Steps(a, b) == 1;

    /// <summary>True when the step between two touching squares is a diagonal one.</summary>
    public static bool IsDiagonalStep(GridSquare from, GridSquare to) =>
        from.X != to.X && from.Y != to.Y;
}
