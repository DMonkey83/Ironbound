namespace Ironbound.Rules.Dice;

/// <summary>
/// PCG-XSH-RR 32-bit (O'Neill, 2014). Chosen over <see cref="System.Random"/> for three
/// reasons the engine depends on: the algorithm is fixed by this file rather than by the
/// runtime version, the whole state is two words that serialise into a save, and
/// <see cref="Next"/> is free of modulo bias.
/// </summary>
public sealed class PcgRandom : IRandomSource
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private ulong _increment;

    /// <param name="seed">Chooses the position in the stream.</param>
    /// <param name="sequence">Chooses the stream. Two sources with different sequences
    /// never correlate, so combat rolls and loot rolls can run independently.</param>
    public PcgRandom(ulong seed, ulong sequence = 1)
    {
        _increment = (sequence << 1) | 1UL;
        _state = 0;
        NextUInt32();
        _state += seed;
        NextUInt32();
    }

    private PcgRandom(in RandomState state)
    {
        _state = state.A;
        _increment = state.B | 1UL;
    }

    public static PcgRandom FromState(in RandomState state) => new(state);

    public uint NextUInt32()
    {
        var previous = _state;
        _state = unchecked(previous * Multiplier + _increment);

        var xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
        var rotation = (int)(previous >> 59);
        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                $"Empty range [{minInclusive}, {maxExclusive}).");
        }

        var range = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)NextBelow(range));
    }

    /// <summary>Uniform in [0, <paramref name="bound"/>), rejecting the biased tail.</summary>
    private uint NextBelow(uint bound)
    {
        if (bound == 1)
        {
            return 0;
        }

        // Values below this threshold would make the final modulo favour low results.
        var threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            var value = NextUInt32();
            if (value >= threshold)
            {
                return value % bound;
            }
        }
    }

    public RandomState Capture() => new(_state, _increment);

    public void Restore(in RandomState state)
    {
        _state = state.A;
        _increment = state.B | 1UL;
    }
}
