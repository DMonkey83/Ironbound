namespace Ironbound.Rules.Dice;

/// <summary>
/// A scripted <see cref="IRandomSource"/> that hands out the values it was given, in order.
/// Lets a test say "the attack roll is 17" instead of hunting for a seed that produces 17.
/// Also useful behind a debug console command.
/// </summary>
/// <remarks>
/// A value outside the requested range throws rather than being clamped: it almost always
/// means the test is counting rolls wrong, and clamping would hide that.
/// </remarks>
public sealed class SequenceRandom : IRandomSource
{
    private readonly int[] _values;
    private readonly bool _repeat;
    private int _index;

    public SequenceRandom(params int[] values) : this(repeat: false, values)
    {
    }

    public SequenceRandom(bool repeat, params int[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Need at least one value.", nameof(values));
        }

        _values = (int[])values.Clone();
        _repeat = repeat;
    }

    /// <summary>A source that returns the same value forever. "Every d20 comes up 20."</summary>
    public static SequenceRandom Always(int value) => new(repeat: true, value);

    /// <summary>How many values have been consumed. Lets a test assert on rolls made.</summary>
    public int Consumed => _index;

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                $"Empty range [{minInclusive}, {maxExclusive}).");
        }

        if (_index >= _values.Length)
        {
            if (!_repeat)
            {
                throw new InvalidOperationException(
                    $"Scripted sequence exhausted after {_values.Length} value(s); something asked for one more.");
            }

            _index = 0;
        }

        var value = _values[_index++];
        if (value < minInclusive || value >= maxExclusive)
        {
            throw new InvalidOperationException(
                $"Scripted value {value} (position {_index - 1}) is outside the requested range " +
                $"[{minInclusive}, {maxExclusive}).");
        }

        return value;
    }

    public RandomState Capture() => new((ulong)_index, 0);

    public void Restore(in RandomState state) => _index = (int)state.A;
}
