namespace Ironbound.Rules.Dice;

/// <summary>Which dice of a term count toward the total.</summary>
public enum KeepMode
{
    All,

    /// <summary>Keep the highest N, drop the rest. "4d6kh3", advantage.</summary>
    Highest,

    /// <summary>Keep the lowest N, drop the rest. "2d20kl1", disadvantage.</summary>
    Lowest,
}

/// <summary>
/// One addend of a <see cref="DiceExpression"/>: either a roll of dice or a flat number.
/// <see cref="Sides"/> of zero marks a flat number, whose magnitude lives in <see cref="Count"/>.
/// </summary>
public readonly record struct DiceTerm
{
    private DiceTerm(int sign, int count, int sides, int keep, KeepMode mode)
    {
        Sign = sign;
        Count = count;
        Sides = sides;
        Keep = keep;
        Mode = mode;
    }

    /// <summary>+1 or -1: whether the term is added or subtracted.</summary>
    public int Sign { get; }

    /// <summary>Dice rolled, or the magnitude of a flat number.</summary>
    public int Count { get; }

    /// <summary>Faces per die; zero for a flat number.</summary>
    public int Sides { get; }

    /// <summary>Dice that count toward the total; equals <see cref="Count"/> unless kh/kl is used.</summary>
    public int Keep { get; }

    public KeepMode Mode { get; }

    public bool IsConstant => Sides == 0;

    public static DiceTerm Dice(int count, int sides, int keep, KeepMode mode, int sign = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, DiceExpression.MaxDiceCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sides, DiceExpression.MaxSides);
        ArgumentOutOfRangeException.ThrowIfLessThan(keep, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keep, count);
        return new DiceTerm(Math.Sign(sign) < 0 ? -1 : 1, count, sides, keep, keep == count ? KeepMode.All : mode);
    }

    public static DiceTerm Constant(int value) =>
        new(value < 0 ? -1 : 1, Math.Abs(value), 0, 0, KeepMode.All);

    /// <summary>Signed value of a flat term; zero for dice.</summary>
    public int ConstantValue => IsConstant ? Sign * Count : 0;

    public int Minimum => Sign > 0 ? UnsignedMinimum : -UnsignedMaximum;

    public int Maximum => Sign > 0 ? UnsignedMaximum : -UnsignedMinimum;

    public double Average => Sign * UnsignedAverage;

    private int UnsignedMinimum => IsConstant ? Count : Keep;

    private int UnsignedMaximum => IsConstant ? Count : Keep * Sides;

    private double UnsignedAverage
    {
        get
        {
            if (IsConstant)
            {
                return Count;
            }

            return Mode switch
            {
                KeepMode.Highest => HighestAverage(Count, Sides, Keep),
                KeepMode.Lowest => PlainAverage(Count, Sides) - HighestAverage(Count, Sides, Count - Keep),
                _ => PlainAverage(Count, Sides),
            };
        }
    }

    private static double PlainAverage(int count, int sides) => count * (sides + 1) / 2.0;

    /// <summary>
    /// Exact expected value of the highest <paramref name="keep"/> of <paramref name="count"/>
    /// dice. Uses E[X] = sum over thresholds t of P(X >= t): the number of kept dice that reach
    /// t is min(keep, N) where N, the dice showing at least t, is binomial.
    /// </summary>
    private static double HighestAverage(int count, int sides, int keep)
    {
        if (keep <= 0)
        {
            return 0;
        }

        if (keep >= count)
        {
            return PlainAverage(count, sides);
        }

        // Threshold 1: every die reaches it, so exactly `keep` dice contribute.
        var total = (double)keep;

        for (var threshold = 2; threshold <= sides; threshold++)
        {
            var p = (sides - threshold + 1) / (double)sides;
            var q = 1 - p;

            var probability = Math.Pow(q, count);
            var expected = 0.0;
            for (var j = 0; j <= count; j++)
            {
                if (j > 0)
                {
                    probability = probability * (count - j + 1) / j * (p / q);
                }

                expected += Math.Min(keep, j) * probability;
            }

            total += expected;
        }

        return total;
    }

    public override string ToString()
    {
        if (IsConstant)
        {
            return Count.ToString();
        }

        var keep = Mode switch
        {
            KeepMode.Highest => $"kh{Keep}",
            KeepMode.Lowest => $"kl{Keep}",
            _ => string.Empty,
        };

        return $"{Count}d{Sides}{keep}";
    }
}
