using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Items;

/// <summary>The four coins, smallest last.</summary>
public enum Coin
{
    Platinum,
    Gold,
    Silver,
    Copper,
}

/// <summary>
/// Coins as they were found: so many of each kind.
/// </summary>
/// <remarks>
/// Kept by kind rather than as one sum because coins weigh by the coin, fifty to the pound
/// whatever they are made of. Four hundred silver and six hundred copper are worth forty-six
/// gold and weigh twenty pounds; the same forty-six gold pieces weigh under one. Nobody changes
/// money yet, so what was picked up is what is carried.
/// </remarks>
public readonly record struct Money(int Platinum, int Gold, int Silver, int Copper)
{
    /// <summary>Fifty coins to the pound, of any metal.</summary>
    public const int CoinsPerPound = 50;

    public static Money None => default;

    /// <summary>So many of one coin.</summary>
    public static Money Of(Coin coin, int count) => coin switch
    {
        Coin.Platinum => new Money(count, 0, 0, 0),
        Coin.Gold => new Money(0, count, 0, 0),
        Coin.Silver => new Money(0, 0, count, 0),
        _ => new Money(0, 0, 0, count),
    };

    /// <summary>What it is all worth, in copper pieces.</summary>
    public int Value =>
        Platinum * Pricing.CopperPerPlatinum
        + Gold * Pricing.CopperPerGold
        + Silver * Pricing.CopperPerSilver
        + Copper;

    /// <summary>How many coins, of every kind.</summary>
    public int Count => Platinum + Gold + Silver + Copper;

    /// <summary>What they weigh, in pounds.</summary>
    public decimal Weight => (decimal)Count / CoinsPerPound;

    public bool IsEmpty => Count == 0;

    public int this[Coin coin] => coin switch
    {
        Coin.Platinum => Platinum,
        Coin.Gold => Gold,
        Coin.Silver => Silver,
        _ => Copper,
    };

    public static Money operator +(Money a, Money b) =>
        new(a.Platinum + b.Platinum, a.Gold + b.Gold, a.Silver + b.Silver, a.Copper + b.Copper);

    public static Money operator -(Money a, Money b) =>
        new(a.Platinum - b.Platinum, a.Gold - b.Gold, a.Silver - b.Silver, a.Copper - b.Copper);

    /// <summary>Whether there is at least this much of every kind of coin to take out.</summary>
    public bool Covers(Money other) =>
        Platinum >= other.Platinum && Gold >= other.Gold && Silver >= other.Silver && Copper >= other.Copper;

    /// <summary>"12 gp, 30 sp, 4 cp" — the coins as they are, not changed into gold.</summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (Platinum != 0)
        {
            parts.Add($"{Platinum} pp");
        }

        if (Gold != 0)
        {
            parts.Add($"{Gold} gp");
        }

        if (Silver != 0)
        {
            parts.Add($"{Silver} sp");
        }

        if (Copper != 0)
        {
            parts.Add($"{Copper} cp");
        }

        return parts.Count == 0 ? "no coins" : string.Join(", ", parts);
    }
}

/// <summary>
/// The dice for what a creature carries in its pockets: "2d6" silver, "3d10" copper.
/// </summary>
public sealed record PurseDefinition(IReadOnlyList<(Coin Coin, string Dice)> Coins)
{
    public static PurseDefinition Empty { get; } = new([]);

    public bool IsEmpty => Coins.Count == 0;

    /// <summary>Rolls every coin's dice, in the order written.</summary>
    public Money Roll(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);

        var money = Money.None;
        foreach (var (coin, dice) in Coins)
        {
            if (DiceExpression.TryParse(dice, out var expression))
            {
                money += Money.Of(coin, Math.Max(0, expression.Roll(random).Total));
            }
        }

        return money;
    }

    /// <summary>What it comes to on average, in copper pieces: for the treasure yardstick.</summary>
    public decimal Average => Coins.Sum(entry =>
        DiceExpression.TryParse(entry.Dice, out var expression)
            ? (decimal)expression.Average * Money.Of(entry.Coin, 1).Value
            : 0m);
}
