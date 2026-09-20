using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Ironbound.Rules.Dice;

/// <summary>
/// A parsed dice formula: "1d20", "2d6+3", "1d8+1d6-1", "4d6kh3", "2d20kl1".
/// Content files store the text; this parses it once at load and is then immutable
/// and safe to share. Flat terms are folded together, so equal formulas compare equal
/// however they were written.
/// </summary>
public sealed class DiceExpression : IEquatable<DiceExpression>
{
    public const int MaxDiceCount = 100;
    public const int MaxSides = 1000;

    private readonly DiceTerm[] _terms;
    private readonly string _canonical;

    private DiceExpression(DiceTerm[] terms)
    {
        _terms = terms;
        _canonical = Canonicalise(terms);

        var minimum = 0;
        var maximum = 0;
        var average = 0.0;
        foreach (var term in terms)
        {
            minimum += term.Minimum;
            maximum += term.Maximum;
            average += term.Average;
        }

        Minimum = minimum;
        Maximum = maximum;
        Average = average;
    }

    public IReadOnlyList<DiceTerm> Terms => _terms;

    /// <summary>Lowest total the formula can produce.</summary>
    public int Minimum { get; }

    /// <summary>Highest total the formula can produce.</summary>
    public int Maximum { get; }

    /// <summary>Exact expected total, including keep-highest/lowest terms. Used by AI scoring.</summary>
    public double Average { get; }

    /// <summary>Dice the formula rolls, ignoring any that kh/kl will drop.</summary>
    public int DiceRolled => _terms.Where(t => !t.IsConstant).Sum(t => t.Count);

    public static DiceExpression Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ParseCore(text, out var expression, out var error)
            ? expression!
            : throw new FormatException($"Bad dice expression \"{text}\": {error}");
    }

    public static bool TryParse(string? text, [NotNullWhen(true)] out DiceExpression? result)
    {
        if (text is null)
        {
            result = null;
            return false;
        }

        return ParseCore(text, out result, out _);
    }

    /// <summary>A formula that is just a number, e.g. fixed damage.</summary>
    public static DiceExpression Constant(int value) => new([DiceTerm.Constant(value)]);

    public DiceRoll Roll(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var dice = new List<DieResult>(DiceRolled);
        var total = 0;
        var constant = 0;

        for (var index = 0; index < _terms.Length; index++)
        {
            var term = _terms[index];
            if (term.IsConstant)
            {
                constant += term.ConstantValue;
                total += term.ConstantValue;
                continue;
            }

            var values = new int[term.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = random.NextDie(term.Sides);
            }

            var counted = SelectCounted(values, term);
            var sum = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (counted[i])
                {
                    sum += values[i];
                }

                dice.Add(new DieResult(index, term.Sides, values[i], counted[i]));
            }

            total += term.Sign * sum;
        }

        return new DiceRoll(_canonical, total, dice, constant);
    }

    /// <summary>
    /// Which dice survive kh/kl. Ties break on roll order so the same values always drop
    /// the same die, keeping the log stable across a replay.
    /// </summary>
    private static bool[] SelectCounted(int[] values, DiceTerm term)
    {
        var counted = new bool[values.Length];
        if (term.Mode == KeepMode.All)
        {
            Array.Fill(counted, true);
            return counted;
        }

        var order = new int[values.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        var highest = term.Mode == KeepMode.Highest;
        Array.Sort(order, (a, b) =>
        {
            var comparison = highest ? values[b].CompareTo(values[a]) : values[a].CompareTo(values[b]);
            return comparison != 0 ? comparison : a.CompareTo(b);
        });

        for (var i = 0; i < term.Keep; i++)
        {
            counted[order[i]] = true;
        }

        return counted;
    }

    private static bool ParseCore(string text, out DiceExpression? result, out string? error)
    {
        result = null;
        error = null;

        var terms = new List<DiceTerm>();
        var constant = 0;
        var index = 0;
        var sign = 1;

        SkipWhitespace(text, ref index);
        if (index < text.Length && (text[index] == '+' || text[index] == '-'))
        {
            sign = text[index] == '-' ? -1 : 1;
            index++;
        }

        while (true)
        {
            if (!TryReadTerm(text, ref index, sign, terms, ref constant, out error))
            {
                return false;
            }

            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                break;
            }

            var op = text[index];
            if (op != '+' && op != '-')
            {
                error = $"expected '+' or '-' at position {index}, found '{op}'";
                return false;
            }

            sign = op == '-' ? -1 : 1;
            index++;
        }

        if (constant != 0 || terms.Count == 0)
        {
            terms.Add(DiceTerm.Constant(constant));
        }

        result = new DiceExpression([.. terms]);
        return true;
    }

    private static bool TryReadTerm(
        string text,
        ref int index,
        int sign,
        List<DiceTerm> terms,
        ref int constant,
        out string? error)
    {
        error = null;
        SkipWhitespace(text, ref index);

        var hasLeadingNumber = TryReadInteger(text, ref index, out var leading);

        if (index < text.Length && (text[index] == 'd' || text[index] == 'D'))
        {
            index++;
            if (!TryReadInteger(text, ref index, out var sides))
            {
                error = $"expected the number of sides after 'd' at position {index}";
                return false;
            }

            var count = hasLeadingNumber ? leading : 1;
            var keep = count;
            var mode = KeepMode.All;

            if (index < text.Length && (text[index] == 'k' || text[index] == 'K'))
            {
                index++;
                if (index >= text.Length)
                {
                    error = $"expected 'h' or 'l' after 'k' at position {index}";
                    return false;
                }

                mode = char.ToLowerInvariant(text[index]) switch
                {
                    'h' => KeepMode.Highest,
                    'l' => KeepMode.Lowest,
                    _ => KeepMode.All,
                };

                if (mode == KeepMode.All)
                {
                    error = $"expected 'h' or 'l' after 'k' at position {index}, found '{text[index]}'";
                    return false;
                }

                index++;
                if (!TryReadInteger(text, ref index, out keep))
                {
                    error = $"expected how many dice to keep at position {index}";
                    return false;
                }
            }

            if (count is < 1 or > MaxDiceCount)
            {
                error = $"dice count must be between 1 and {MaxDiceCount}, found {count}";
                return false;
            }

            if (sides is < 1 or > MaxSides)
            {
                error = $"die must have between 1 and {MaxSides} sides, found {sides}";
                return false;
            }

            if (keep < 1 || keep > count)
            {
                error = $"cannot keep {keep} of {count} dice";
                return false;
            }

            terms.Add(DiceTerm.Dice(count, sides, keep, mode, sign));
            return true;
        }

        if (!hasLeadingNumber)
        {
            var found = index < text.Length ? $"'{text[index]}'" : "end of input";
            error = $"expected a number or dice at position {index}, found {found}";
            return false;
        }

        constant += sign * leading;
        return true;
    }

    private static bool TryReadInteger(string text, ref int index, out int value)
    {
        value = 0;
        var start = index;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            // Saturate rather than overflow; the range checks above reject the result anyway.
            value = value > MaxSides ? value : (value * 10) + (text[index] - '0');
            index++;
        }

        return index > start;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    private static string Canonicalise(DiceTerm[] terms)
    {
        var text = new StringBuilder();
        foreach (var term in terms)
        {
            if (text.Length == 0)
            {
                if (term.Sign < 0)
                {
                    text.Append('-');
                }
            }
            else
            {
                text.Append(term.Sign < 0 ? '-' : '+');
            }

            text.Append(term);
        }

        return text.ToString();
    }

    public bool Equals(DiceExpression? other) =>
        other is not null && string.Equals(_canonical, other._canonical, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as DiceExpression);

    public override int GetHashCode() => _canonical.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => _canonical;
}
