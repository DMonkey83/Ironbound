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
/// gold and weigh twenty pounds; the same forty-six gold pieces weigh under one. What was picked
/// up is what is carried until somebody trades: paying a merchant spends the small coins first
/// and the change comes back in the largest, so a purse heavy with silver and copper is
/// lightened by shopping.
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

    /// <summary>
    /// A sum as the fewest coins a merchant would count it out in: gold, then silver, then copper.
    /// Never platinum: prices are reckoned in gold, and nobody is handed change in platinum.
    /// </summary>
    public static Money FromCopper(int copper)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(copper);

        return new Money(
            0,
            copper / Pricing.CopperPerGold,
            copper % Pricing.CopperPerGold / Pricing.CopperPerSilver,
            copper % Pricing.CopperPerSilver);
    }

    /// <summary>
    /// The purse left after paying a price in copper, or null when it is not worth that much.
    /// </summary>
    /// <remarks>
    /// The smallest coins go first — copper, then silver, then gold, then platinum — and only as
    /// many of each as the price still wants, rounded up to the coin. Whatever that overpays comes
    /// back as change in the largest coins (<see cref="FromCopper"/>). So paying fifty gold out of
    /// six hundred copper, four hundred silver and twelve gold spends all the copper and silver and
    /// four of the gold, and leaves eight gold: the same value as any other way of paying, and
    /// twenty pounds lighter.
    /// </remarks>
    public Money? Pay(int copper)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(copper);

        if (Value < copper)
        {
            return null;
        }

        var owed = copper;
        var spent = None;

        foreach (var coin in new[] { Coin.Copper, Coin.Silver, Coin.Gold, Coin.Platinum })
        {
            if (owed <= 0)
            {
                break;
            }

            var worth = Of(coin, 1).Value;
            var count = Math.Min(this[coin], (owed + worth - 1) / worth);
            spent += Of(coin, count);
            owed -= count * worth;
        }

        // Owed is nought or less now: less is what was handed over beyond the price.
        return this - spent + FromCopper(-owed);
    }

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
