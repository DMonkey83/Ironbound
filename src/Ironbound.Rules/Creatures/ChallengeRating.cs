using System.Globalization;
using Ironbound.Rules.Classes;

namespace Ironbound.Rules.Creatures;

/// <summary>
/// How dangerous something is, in the Bestiary's terms: a whole number, or one of the five
/// fractions below one.
/// </summary>
/// <remarks>
/// Held as a fraction rather than a double because the fractions are names, not quantities —
/// "1/3" is a row in a table, and 0.333 is a rounding error waiting to land on the wrong one.
/// </remarks>
public readonly record struct ChallengeRating : IComparable<ChallengeRating>
{
    /// <summary>The fractions the book has below one, largest last.</summary>
    private static readonly int[] Fractions = [8, 6, 4, 3, 2];

    private ChallengeRating(int whole, int denominator)
    {
        Whole = whole;
        Denominator = denominator;
    }

    /// <summary>The rating itself for a whole one; nought for a fraction.</summary>
    public int Whole { get; }

    /// <summary>1 for a whole rating; 2, 3, 4, 6 or 8 for one-half to one-eighth.</summary>
    public int Denominator { get; }

    public bool IsFraction => Denominator > 1;

    /// <summary>A whole rating, one or more.</summary>
    public static ChallengeRating Of(int whole)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(whole, 1);
        return new ChallengeRating(whole, 1);
    }

    /// <summary>One of the fractions: <c>Fraction(3)</c> is CR 1/3.</summary>
    public static ChallengeRating Fraction(int denominator)
    {
        if (!Fractions.Contains(denominator))
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), $"There is no CR 1/{denominator}.");
        }

        return new ChallengeRating(0, denominator);
    }

    public static ChallengeRating Half { get; } = Fraction(2);

    public static ChallengeRating Third { get; } = Fraction(3);

    /// <summary>"3", "1/3" — what a content file writes. False for anything else.</summary>
    public static bool TryParse(string? text, out ChallengeRating rating)
    {
        rating = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();

        if (text.StartsWith("1/", StringComparison.Ordinal)
            && int.TryParse(text[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            && Fractions.Contains(denominator))
        {
            rating = new ChallengeRating(0, denominator);
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var whole) && whole >= 1)
        {
            rating = new ChallengeRating(whole, 1);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The Bestiary's rule for a creature made of class levels: one less than its level for an
    /// adventurer's classes, two less for an NPC's, and below one the fractions — a first-level
    /// warrior is CR 1/3, a second-level one 1/2, a first-level fighter 1/2.
    /// </summary>
    /// <remarks>
    /// A creature with both kinds of level counts as an adventurer: the book's one-less is for
    /// "PC class levels", and one adventurer's level among warrior ones is enough to make it
    /// so. That case is not in the book and is not in the content; it is here so the rule has
    /// an answer rather than a crash.
    /// </remarks>
    public static ChallengeRating ForClassLevels(IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var taken = levels.ToList();
        var total = taken.Sum(level => level.Level);
        var heroic = taken.Any(level => !level.Class.Npc && level.Level > 0);

        return FromSteps(total - (heroic ? 1 : 2));
    }

    /// <summary>
    /// A rating from the class-level arithmetic: one and up is that number, nought is 1/2, and
    /// anything less is 1/3 — the Bestiary's floor for something with a level at all.
    /// </summary>
    public static ChallengeRating FromSteps(int steps) => steps switch
    {
        >= 1 => Of(steps),
        0 => Half,
        _ => Third,
    };

    /// <summary>
    /// What defeating it is worth, in total, before anybody shares it out: the Bestiary's
    /// Table 1–1 and the Game Mastery screen's.
    /// </summary>
    /// <remarks>
    /// Above two the table doubles every two steps — CR 3 is 800, 5 is 1,600, 7 is 3,200, and
    /// the even ones half again of the odd one below — so it is that arithmetic rather than
    /// twenty-five numbers typed in. It agrees with the printed table all the way to CR 25.
    /// </remarks>
    public int Experience
    {
        get
        {
            if (IsFraction)
            {
                return Denominator switch
                {
                    8 => 50,
                    6 => 65,
                    4 => 100,
                    3 => 135,
                    _ => 200,
                };
            }

            return Whole switch
            {
                1 => 400,
                _ when Whole % 2 == 0 => 600 << ((Whole - 2) / 2),
                _ => 800 << ((Whole - 3) / 2),
            };
        }
    }

    /// <summary>As a number, for ordering and for comparing with a level.</summary>
    public double Value => IsFraction ? 1.0 / Denominator : Whole;

    public int CompareTo(ChallengeRating other) => Value.CompareTo(other.Value);

    public static bool operator <(ChallengeRating left, ChallengeRating right) => left.CompareTo(right) < 0;

    public static bool operator >(ChallengeRating left, ChallengeRating right) => left.CompareTo(right) > 0;

    public static bool operator <=(ChallengeRating left, ChallengeRating right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ChallengeRating left, ChallengeRating right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        IsFraction ? $"1/{Denominator}" : Whole.ToString(CultureInfo.InvariantCulture);
}
