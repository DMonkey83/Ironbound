namespace Ironbound.Rules.Modifiers;

/// <summary>
/// A single signed adjustment to a value, tagged with its stacking category and the
/// thing that granted it. <paramref name="Source"/> is both the log label and the
/// handle used to take the modifier away again.
/// </summary>
public readonly record struct Modifier(int Value, BonusType Type, string Source)
{
    public static Modifier Untyped(int value, string source) => new(value, BonusType.Untyped, source);

    public bool IsPenalty => Value < 0;

    public override string ToString() =>
        $"{Value:+0;-0;+0} {BonusTypes.Name(Type)} ({Source ?? "unknown"})";
}

/// <summary>One modifier as it was resolved by a <see cref="ModifierStack"/>.</summary>
/// <param name="SuppressedBy">Source of the modifier that beat this one, when not applied.</param>
public readonly record struct ModifierEntry(Modifier Modifier, bool Applied, string? SuppressedBy);
