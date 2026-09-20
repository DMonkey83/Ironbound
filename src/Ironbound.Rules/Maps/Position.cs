namespace Ironbound.Rules.Maps;

/// <summary>A five-foot square, which is the unit every rule actually measures in.</summary>
public readonly record struct GridSquare(int X, int Y)
{
    /// <summary>The middle of the square, where a creature standing in it is drawn.</summary>
    public Position Centre => new(
        (X + 0.5f) * Distance.FeetPerSquare,
        (Y + 0.5f) * Distance.FeetPerSquare);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// Somewhere on the battlefield, in feet.
/// </summary>
/// <remarks>
/// Positions are continuous so that movement can be interpolated and a real-time scheduler stays
/// possible. No rule reads them directly, though: every question about distance, reach or
/// threatened ground goes through <see cref="Square"/>. That is deliberate — it means sub-square
/// precision can never change an outcome, so floating-point drift can never change a replay.
/// </remarks>
public readonly record struct Position(float X, float Y)
{
    public GridSquare Square => new(
        (int)MathF.Floor(X / Distance.FeetPerSquare),
        (int)MathF.Floor(Y / Distance.FeetPerSquare));

    public static Position Of(GridSquare square) => square.Centre;

    public static Position Of(int x, int y) => new GridSquare(x, y).Centre;

    public override string ToString() => $"({X:0.#}, {Y:0.#}) ft";
}
