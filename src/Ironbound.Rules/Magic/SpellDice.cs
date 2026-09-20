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
            return amount;
        }

        var dice = DiceAt(casterLevel);
        return DiceExpression.Parse($"{dice}d{Sides}").Plus(dice * FlatPerDie);
    }

    public override string ToString() =>
        FixedAmount?.ToString() ?? $"1d{Sides} per {LevelsPerDie} level(s), max {MaximumDice}";
}
