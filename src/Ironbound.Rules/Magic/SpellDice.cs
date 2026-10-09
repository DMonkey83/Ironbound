using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Magic;

/// <summary>
/// How much a spell rolls, given who is casting it.
/// </summary>
/// <remarks>
/// One formula covers the two shapes that matter: fireball's die per level to a cap, and magic
/// missile's extra missile every two levels. Keeping it declarative rather than a delegate is
/// what lets a spell be written down as data later.
/// </remarks>
public sealed record SpellDice
{
    private SpellDice(DiceExpression? fixedAmount, int sides, int maximumDice, int levelsPerDie, int flatPerDie)
    {
        FixedAmount = fixedAmount;
        Sides = sides;
        MaximumDice = maximumDice;
        LevelsPerDie = levelsPerDie;
        FlatPerDie = flatPerDie;
    }

    public DiceExpression? FixedAmount { get; }

    public int Sides { get; }

    public int MaximumDice { get; }

    public int LevelsPerDie { get; }

    public int FlatPerDie { get; }

    /// <summary>
    /// A flat bonus per caster level on top of the dice: the cure spells' "+1 per level".
    /// </summary>
    public int BonusPerLevel { get; private init; }

    /// <summary>The most <see cref="BonusPerLevel"/> may come to: five for a light wound. Zero is no limit.</summary>
    public int BonusMaximum { get; private init; }

    /// <summary>
    /// The same dice plus so much a caster level, to a ceiling — "2d8 + 1 per level (maximum
    /// 10)". The dial the cure spells wanted.
    /// </summary>
    public SpellDice PlusPerLevel(int perLevel, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(perLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        return this with { BonusPerLevel = perLevel, BonusMaximum = maximum };
    }

    private int LevelBonus(int casterLevel)
    {
        var bonus = BonusPerLevel * Math.Max(0, casterLevel);
        return BonusMaximum > 0 ? Math.Min(bonus, BonusMaximum) : bonus;
    }

    /// <summary>Always the same, whoever casts it.</summary>
    public static SpellDice Fixed(string expression) =>
        new(DiceExpression.Parse(expression), 0, 0, 1, 0);

    /// <param name="levelsPerDie">Two for magic missile, which gains a missile every other level.</param>
    /// <param name="flatPerDie">One for magic missile: each missile is 1d4+1.</param>
    public static SpellDice PerLevel(int sides, int maximumDice, int levelsPerDie = 1, int flatPerDie = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDice, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(levelsPerDie, 1);

        return new SpellDice(null, sides, maximumDice, levelsPerDie, flatPerDie);
    }

    public int DiceAt(int casterLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(casterLevel);
        return Math.Clamp(1 + ((casterLevel - 1) / LevelsPerDie), 1, MaximumDice);
    }

    public DiceExpression At(int casterLevel)
    {
        if (FixedAmount is { } amount)
        {
            return BonusPerLevel == 0 ? amount : amount.Plus(LevelBonus(casterLevel));
        }

        var dice = DiceAt(casterLevel);
        return DiceExpression.Parse($"{dice}d{Sides}").Plus((dice * FlatPerDie) + LevelBonus(casterLevel));
    }

    public override string ToString()
    {
        var dice = FixedAmount?.ToString() ?? $"1d{Sides} per {LevelsPerDie} level(s), max {MaximumDice}";

        return BonusPerLevel == 0
            ? dice
            : $"{dice} + {BonusPerLevel} per level" + (BonusMaximum > 0 ? $" (max {BonusMaximum})" : string.Empty);
    }
}
