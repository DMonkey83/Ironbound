using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation;

/// <summary>
/// Somebody on a level who trades: a stall, a stock, and the coin to buy what the party brings.
/// </summary>
/// <remarks>
/// A merchant's purse is bottomless, which is a simplification: the GameMastery Guide caps only
/// what any one item fetches, and that cap is <see cref="PurchaseLimit"/>. What the party sells
/// joins the stock and can be bought back at its list price; nothing else ever arrives, so a
/// potion bought is one fewer on the stall for good.
/// </remarks>
public sealed class Merchant
{
    private readonly Campaign _campaign;

    internal Merchant(FeatureDefinition feature, Campaign campaign)
    {
        Feature = feature;
        _campaign = campaign;
    }

    public string Id => Feature.Id;

    /// <summary>"Tobin".</summary>
    public string Name => Feature.Name;

    /// <summary>The level feature the merchant is: squares, stall, model and the rest.</summary>
    public FeatureDefinition Feature { get; }

    /// <summary>What is for sale, a line per item, stacked as a bag stacks them.</summary>
    public IReadOnlyList<BagEntry> Stock => Goods.Entries;

    /// <summary>
    /// Whether they will trade: once the room their file names has been won, or from the start
    /// when it names none. Being open says nothing about whether this is a moment to trade in —
    /// nobody shops in the middle of a fight; see <see cref="Campaign.WhyNotBuy"/>.
    /// </summary>
    public bool IsOpen => Feature.OpensAfter is not { } area || _campaign.IsCleared(area);

    /// <summary>The most they pay for any one item, in copper.</summary>
    public int PurchaseLimit => Feature.PurchaseLimit * Pricing.CopperPerGold;

    public IReadOnlyList<GridSquare> Squares => Feature.Squares;

    /// <summary>Where the figure stands: the file's square, or the first of them.</summary>
    public GridSquare StandsAt => Feature.StandsAt ?? Feature.Squares[0];

    /// <summary>Whether somebody standing here is close enough to trade: beside the figure or the stall.</summary>
    public bool IsWithinReach(GridSquare from) => Squares.Any(square => Distance.AreAdjacent(square, from));

    internal EntryList Goods { get; } = new();

    public override string ToString() =>
        $"{Name}: {(Stock.Count == 0 ? "nothing left" : string.Join(", ", Stock.Select(entry => entry.ToString())))}";
}

/// <summary>
/// Buying and selling: the merchants on the level, what they ask and offer, and the coin going
/// between them and the party's purse.
/// </summary>
/// <remarks>
/// Trading is something done between fights, as handling gear is, by whoever is beside the stall.
/// Only the bag trades: something worn or on a belt is put back in the bag first, which keeps a
/// sword from being sold out of somebody's hand. Paying spends the purse's smallest coins first
/// and change comes back in gold (<see cref="Money.Pay"/>); a sale is paid in the fewest coins.
/// </remarks>
public sealed partial class Campaign
{
    private readonly List<Merchant> _merchants = [];

    /// <summary>Everybody on the level who trades, open or not. Empty for a run of separate fights.</summary>
    public IReadOnlyList<Merchant> Merchants => _merchants;

    public Merchant? GetMerchant(string id) =>
        _merchants.FirstOrDefault(merchant => string.Equals(merchant.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// What a merchant gives for one of a bag line: half its price, a valuable's whole price, no
    /// more than the merchant's limit, and nought for something broken. Nought for nobody.
    /// </summary>
    public int OfferFor(string merchantId, BagEntry line)
    {
        ArgumentNullException.ThrowIfNull(line);

        return GetMerchant(merchantId) is { } merchant
            ? Trade.Offer(line.Item, line.IsBroken, merchant.PurchaseLimit)
            : 0;
    }

    /// <summary>Why so many of a stock line could not be bought now, or null when they can.</summary>
    public string? WhyNotBuy(string merchantId, BagEntry stockLine, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(stockLine);

        if (GetMerchant(merchantId) is not { } merchant)
        {
            return "There is nobody like that to trade with.";
        }

        if (TradeRefusal(merchant) is { } why)
        {
            return why;
        }

        if (count < 1)
        {
            return "Choose how many to buy: at least one.";
        }

        var held = merchant.Goods.CountOf(stockLine);
        if (held < count)
        {
            return held == 0
                ? $"{merchant.Name} has no {stockLine.Item.Name} to sell."
                : $"{merchant.Name} has only {held} of the {stockLine.Item.Name}.";
        }

        var price = Trade.Asking(stockLine.Item) * count;
        return _bag.Money.Pay(price) is null
            ? $"{Capital(Line(stockLine, count))} costs {Pricing.Format(price)}, and the purse holds {Pricing.Format(_bag.Purse)}."
            : null;
    }

    /// <summary>Why so many of a bag line could not be sold now, or null when they can.</summary>
    public string? WhyNotSell(string merchantId, BagEntry bagLine, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(bagLine);

        if (GetMerchant(merchantId) is not { } merchant)
        {
            return "There is nobody like that to trade with.";
        }

        if (TradeRefusal(merchant) is { } why)
        {
            return why;
        }

        if (count < 1)
        {
            return "Choose how many to sell: at least one.";
        }

        var held = CountInBag(bagLine);
        if (held < count)
        {
            return held == 0
                ? $"There is no {bagLine.Item.Name} in the bag. Whatever is worn or on a belt goes back in the bag before it is sold."
                : $"The bag holds only {held} of the {bagLine.Item.Name}.";
        }

        if (bagLine.IsBroken)
        {
            return $"{merchant.Name} will not buy a broken {bagLine.Item.Name}.";
        }

        return Trade.Offer(bagLine.Item, bagLine.IsBroken, merchant.PurchaseLimit) == 0
            ? $"{merchant.Name} will not give anything for the {bagLine.Item.Name}."
            : null;
    }

    /// <summary>
    /// Buys so many of a stock line at the list price: off the stall, into the bag, and the purse
    /// paid out, change and all. All or nothing: refused, and nothing changed, when anything is
    /// short — the stock, the purse, or somebody beside the stall.
    /// </summary>
    public GearResult Buy(string merchantId, BagEntry stockLine, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(stockLine);

        Collect();

        if (WhyNotBuy(merchantId, stockLine, count) is { } why)
        {
            return GearResult.Refused(why);
        }

        var merchant = GetMerchant(merchantId)!;
        var line = merchant.Goods.Find(stockLine)!;
        var price = Trade.Asking(line.Item) * count;
        var left = _bag.Money.Pay(price)!.Value;

        merchant.Goods.Remove(line, count);
        _bag.RemoveCoins(_bag.Money);
        _bag.AddCoins(left);
        _bag.Add(line.Item, count, line.IsBroken);

        return GearResult.Done($"Bought {Line(line, count)} from {merchant.Name} for {Pricing.Format(price)}.");
    }

    /// <summary>
    /// Sells so many of a bag line for the merchant's offer apiece: out of the bag, onto the
    /// stall — where it can be bought back at its full price — and the coins into the purse.
    /// </summary>
    public GearResult Sell(string merchantId, BagEntry bagLine, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(bagLine);

        Collect();

        if (WhyNotSell(merchantId, bagLine, count) is { } why)
        {
            return GearResult.Refused(why);
        }

        var merchant = GetMerchant(merchantId)!;
        var line = _bag.Find(bagLine)!;
        var paid = Trade.Offer(line.Item, line.IsBroken, merchant.PurchaseLimit) * count;

        _bag.Remove(line, count);
        merchant.Goods.Add(line.Item, count, line.IsBroken);
        _bag.AddCoins(Money.FromCopper(paid));

        return GearResult.Done($"Sold {Line(line, count)} to {merchant.Name} for {Pricing.Format(paid)}.");
    }

    /// <summary>"the potion of cure light wounds", "3 potions of…": a line at a count, for the log.</summary>
    private static string Line(BagEntry line, int count) =>
        count == 1 ? $"the {line.Item.Name}" : (line with { Count = count }).ToString();

    private int CountInBag(BagEntry line) =>
        _bag.Entries.Where(entry => entry.Matches(line)).Sum(entry => entry.Count);

    /// <summary>
    /// Why nobody can trade with a merchant at all just now: shut, a fight on, or nobody of the
    /// party on their feet beside the stall. Null when somebody can.
    /// </summary>
    private string? TradeRefusal(Merchant merchant)
    {
        if (!merchant.IsOpen)
        {
            return ClosedLine(merchant);
        }

        if (!AtLeisure)
        {
            return $"Not now: trading with {merchant.Name} will have to wait.";
        }

        return Party.Any(member => member.IsConscious
            && Battle.Battlefield?.SquareOf(member) is { } at && merchant.IsWithinReach(at))
            ? null
            : $"Nobody is close enough to trade with {merchant.Name}.";
    }

    /// <summary>
    /// Why this one cannot go to trade with a merchant: what <see cref="Use"/> says before it
    /// greets them.
    /// </summary>
    private string? MerchantRefusal(Merchant? merchant, Creature who)
    {
        if (merchant is null)
        {
            return "There is nothing like that here.";
        }

        if (!merchant.IsOpen)
        {
            return ClosedLine(merchant);
        }

        if (!AtLeisure)
        {
            return $"Not now: trading with {merchant.Name} will have to wait.";
        }

        if (!Party.Contains(who) || !who.IsConscious)
        {
            return $"{who.Name} is in no state to trade with {merchant.Name}.";
        }

        return Field.SquareOf(who) is { } at && merchant.IsWithinReach(at)
            ? null
            : $"{who.Name} is not close enough to {merchant.Name}.";
    }

    private string ClosedLine(Merchant merchant)
    {
        if (merchant.Feature.Closed.Length > 0)
        {
            return merchant.Feature.Closed;
        }

        var area = merchant.Feature.OpensAfter is { } id ? Level?.GetArea(id) : null;
        return area is null
            ? $"{merchant.Name} is not trading."
            : $"{merchant.Name} will not trade until {area.Name} is safe.";
    }

    /// <summary>
    /// Goes to trade: the greeting, and nothing else changes. The buying and selling are their own
    /// steps, as taking from an opened chest is. A merchant is never used up.
    /// </summary>
    private FeatureResult Greet(Merchant merchant) =>
        new(true, [merchant.Feature.Text.Length > 0 ? merchant.Feature.Text : $"{merchant.Name} is ready to trade."], null);

    /// <summary>A level's merchant, with the stock the file gives them.</summary>
    private Merchant BuildMerchant(FeatureDefinition feature)
    {
        var merchant = new Merchant(feature, this);

        foreach (var line in feature.Stock)
        {
            if (_library.GetItem(line.ItemId) is { } item)
            {
                merchant.Goods.Add(item, line.Count);
            }
        }

        return merchant;
    }

    // ---- saving ----

    private static SavedMerchant Capture(Merchant merchant) =>
        new(merchant.Id, [.. merchant.Stock.Select(Capture)]);

    /// <summary>
    /// Puts the level's merchants back: each built from the file, then given the stock the save
    /// says they have. A save before seventeen has none, so every merchant comes back as the file
    /// stocks them; a merchant the level no longer has is left out, stock and all.
    /// </summary>
    private void RestoreMerchants(SavedMerchant[]? saved, LevelDefinition level)
    {
        var byId = (saved ?? []).ToDictionary(merchant => merchant.Id, StringComparer.Ordinal);

        foreach (var feature in level.Features.Where(feature => feature.Kind == FeatureKind.Merchant))
        {
            var merchant = BuildMerchant(feature);

            if (byId.TryGetValue(feature.Id, out var state))
            {
                merchant.Goods.Clear();
                foreach (var entry in state.Stock)
                {
                    merchant.Goods.Add(Item(entry.Id), entry.Count, entry.Broken);
                }
            }

            _merchants.Add(merchant);
        }
    }
}
