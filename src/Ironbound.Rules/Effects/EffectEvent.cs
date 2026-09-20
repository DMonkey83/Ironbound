using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Effects;

public enum EffectEventKind
{
    Applied,
    Ticked,
    Expired,

    /// <summary>Taken away early — dispelled, or the source destroyed.</summary>
    Removed,
}

/// <summary>Something an effect did. The combat log reads these; there is no event bus.</summary>
public readonly record struct EffectEvent(EffectEventKind Kind, Effect Effect, string Description)
{
    public override string ToString() => Description;
}

/// <summary>
/// What a periodic effect is handed when it fires: who it is happening to, the randomness it
/// may consume, and somewhere to say what it did.
/// </summary>
public sealed class EffectContext(Creatures.Creature target, IRandomSource random, Duration elapsed)
{
    private readonly List<string> _reports = [];

    public Creatures.Creature Target { get; } = target;

    public IRandomSource Random { get; } = random;

    /// <summary>The span that just elapsed — one period.</summary>
    public Duration Elapsed { get; } = elapsed;

    internal IReadOnlyList<string> Reports => _reports;

    /// <summary>Records a line for the log. An effect that does nothing this tick reports nothing.</summary>
    public void Report(string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(what);
        _reports.Add(what);
    }
}
