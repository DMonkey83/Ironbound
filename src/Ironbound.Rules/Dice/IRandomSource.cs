namespace Ironbound.Rules.Dice;

/// <summary>
/// Every random number the rules engine consumes comes from here. Nothing in
/// <c>Ironbound.Rules</c> may touch <see cref="System.Random"/> directly: a roll that
/// bypasses this interface cannot be seeded, saved, or replayed.
/// </summary>
public interface IRandomSource
{
    /// <summary>Uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Snapshot of the generator, to be written into a save file.</summary>
    RandomState Capture();

    /// <summary>Rewinds the generator to a snapshot, resuming the identical stream.</summary>
    void Restore(in RandomState state);
}

/// <summary>
/// Opaque two-word generator state. The meaning of the words is the generator's business;
/// treat it as a blob that round-trips through a save file.
/// </summary>
public readonly record struct RandomState(ulong A, ulong B);

public static class RandomSourceExtensions
{
    /// <summary>Rolls one die: uniform in [1, <paramref name="sides"/>].</summary>
    public static int NextDie(this IRandomSource random, int sides)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);
        return random.Next(1, sides + 1);
    }
}
