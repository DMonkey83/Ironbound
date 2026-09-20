using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Abilities;

/// <summary>
/// One ability score: a permanent <see cref="Base"/> plus a <see cref="ModifierStack"/> of
/// everything currently acting on it. The split matters — in d20 terms, ability *drain* and
/// level-ups move <see cref="Base"/>, while buffs and ability *damage* are modifiers that go
/// away again.
/// </summary>
public sealed class AbilityScore
{
    public const int MinimumBase = 0;
    public const int MaximumBase = 100;

    private int _base;

    public AbilityScore(Ability ability, int baseScore)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(baseScore, MinimumBase);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(baseScore, MaximumBase);
        Ability = ability;
        HasScore = true;
        _base = baseScore;
    }

    private AbilityScore(Ability ability)
    {
        Ability = ability;
        HasScore = false;
    }

    /// <summary>
    /// A creature that lacks the ability entirely — undead and constructs have no Constitution.
    /// This is not the same as a score of 0: the modifier is +0 rather than -5, and effects
    /// that target the ability have nothing to bite on.
    /// </summary>
    public static AbilityScore NonAbility(Ability ability) => new(ability);

    public Ability Ability { get; }

    public bool HasScore { get; }

    /// <summary>Permanent score before any modifier. Moved by level-ups, inherent bonuses, drain.</summary>
    public int Base
    {
        get => _base;
        set
        {
            if (!HasScore)
            {
                throw new InvalidOperationException(
                    $"{AbilityInfo.Abbreviate(Ability)} is a non-ability; it has no base score to set.");
            }

            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumBase);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumBase);
            _base = value;
        }
    }

    /// <summary>Buffs, penalties and ability damage currently applied.</summary>
    public ModifierStack Modifiers { get; } = new();

    /// <summary>Current score, or null for a non-ability. Never drops below zero.</summary>
    public int? Score => HasScore ? Current : null;

    /// <summary>
    /// What almost everything actually reads. floor((score - 10) / 2), and +0 for a non-ability.
    /// </summary>
    public int Modifier => HasScore ? ModifierFor(Current) : 0;

    /// <summary>floor((score - 10) / 2). The shift floors toward negative infinity; integer
    /// division would truncate toward zero and hand a score of 7 a modifier of -1 instead of -2.</summary>
    public static int ModifierFor(int score) => (score - 10) >> 1;

    private int Current => Math.Max(0, _base + Modifiers.Total);

    public ModifierBreakdown Explain() => Modifiers.Explain();

    public override string ToString()
    {
        var name = AbilityInfo.Abbreviate(Ability);
        if (!HasScore)
        {
            return $"{name} — (+0)";
        }

        var detail = Modifiers.Count == 0 ? string.Empty : $" [{_base} base, {Modifiers.Explain()}]";
        return $"{name} {Current} ({Modifier:+0;-0;+0}){detail}";
    }
}
