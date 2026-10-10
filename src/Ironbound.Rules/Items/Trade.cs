namespace Ironbound.Rules.Items;

/// <summary>
/// What a merchant asks for something and what one will give for it, in copper pieces.
/// </summary>
/// <remarks>
/// The Core Rulebook's arithmetic (Open Game Content), in our words: things are bought at their
/// list price and sold for half of it, except gems, art and trade goods, which are as good as
/// money and fetch their full value. The GameMastery Guide adds a purchase limit — the most a
/// settlement's merchants can spend on any one item — so a +1 greatsword sold in a hamlet fetches
/// the limit and no more. Nothing broken is bought at all, and nobody buys teeth.
/// </remarks>
public static class Trade
{
    /// <summary>What a merchant will pay at most for one item when nothing says otherwise: a thorp's 500 gp.</summary>
    public const int DefaultPurchaseLimitGold = 500;

    /// <summary>What a merchant asks for one of something: its list price.</summary>
    public static int Asking(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Price;
    }

    /// <summary>
    /// What a merchant gives for one of something: half its price, rounded down to the copper, or
    /// the whole of it for a valuable — never more than the limit. Nought means it is not bought:
    /// it is broken, it is a natural weapon, or it is worth nothing.
    /// </summary>
    /// <param name="limitCopper">The most the merchant pays for any one item, in copper.</param>
    public static int Offer(ItemDefinition item, bool broken, int limitCopper)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegative(limitCopper);

        if (broken || item.IsNatural)
        {
            return 0;
        }

        var offer = item.Kind == ItemKind.Valuable ? item.Price : item.Price / 2;
        return Math.Clamp(offer, 0, limitCopper);
    }
}
