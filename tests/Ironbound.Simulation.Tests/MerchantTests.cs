using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation.Tests;

/// <summary>Tobin at the cave mouth and the pedlar on the road: when they trade, and what for.</summary>
public class MerchantTests
{
    private const string Cure = "potion-of-cure-light-wounds";

    private static ContentLibrary Library => ContentFiles.Default;

    private static ItemDefinition Item(string id) => Library.GetItem(id)!;

    /// <summary>A square beside Tobin, west of the figure, in the cave mouth.</summary>
    private static readonly GridSquare BesideTobin = new(8, 36);

    private static Campaign Caves() => Campaign.Begin(Library, "caves-of-shadow");

    private static Creature Member(Campaign run, string name) => run.Party.Single(one => one.Name == name);

    /// <summary>The cave mouth won and collected, and Pip standing beside Tobin.</summary>
    private static Campaign TobinOpen()
    {
        var run = Caves();
        run.Engage("cave-mouth");
        SleepingPlacementTests.Win(run);
        run.Collect();
        Assert.True(run.Walk(Member(run, "Pip"), BesideTobin));
        return run;
    }

    private static BagEntry Stocked(Campaign run, string merchant, string id) =>
        run.GetMerchant(merchant)!.Stock.Single(line => line.Id == id);

    private static int StockOf(Campaign run, string merchant, string id) =>
        run.GetMerchant(merchant)!.Stock.Where(line => line.Id == id).Sum(line => line.Count);

    // ---- when Tobin trades ----

    [Fact]
    public void TobinIsThereFromTheStartButClosedUntilTheCaveMouthIsWon()
    {
        var run = Caves();
        var pip = Member(run, "Pip");
        var tobin = run.GetMerchant("tobin")!;

        Assert.Equal([tobin], run.Merchants);
        Assert.False(tobin.IsOpen);
        Assert.Equal(new GridSquare(9, 36), tobin.StandsAt);
        Assert.Same(run.Level!.GetFeature("tobin"), run.FeatureAt(new GridSquare(10, 37)));

        Assert.True(run.Walk(pip, BesideTobin));
        var refused = run.Use("tobin", pip);

        Assert.False(refused.Success);
        Assert.Equal("Tobin is in no state to trade with orcs about.", refused.Lines[0]);
        Assert.False(run.CanUse("tobin", pip));
        Assert.Equal("Tobin is in no state to trade with orcs about.", run.WhyNotBuy("tobin", Stocked(run, "tobin", Cure)));

        // Nor while the orcs are on their feet.
        run.Engage("cave-mouth");
        Assert.Equal(CampaignState.Fighting, run.State);
        Assert.False(run.Use("tobin", pip).Success);
    }

    [Fact]
    public void OnceTheOrcsAreBeatenTobinGreetsWhoeverComesUpAndIsNeverUsedUp()
    {
        var run = TobinOpen();
        var pip = Member(run, "Pip");

        Assert.True(run.GetMerchant("tobin")!.IsOpen);
        Assert.True(run.CanUse("tobin", pip));

        var greeted = run.Use("tobin", pip);

        Assert.True(greeted.Success);
        Assert.Equal([run.Level!.GetFeature("tobin")!.Text], greeted.Lines);
        Assert.Null(greeted.MovedTo);
        Assert.False(run.IsUsed("tobin"));
        Assert.True(run.Use("tobin", pip).Success);
    }

    [Fact]
    public void NobodyTradesInTheMiddleOfAnotherFight()
    {
        var run = TobinOpen();
        var pip = Member(run, "Pip");

        run.Engage("guard-post");
        var refused = run.Use("tobin", pip);

        Assert.False(refused.Success);
        Assert.StartsWith("Not now", refused.Lines[0]);
        Assert.StartsWith("Not now", run.WhyNotBuy("tobin", Stocked(run, "tobin", Cure)));
        Assert.False(run.Buy("tobin", Stocked(run, "tobin", Cure)));
    }

    [Fact]
    public void TradingNeedsSomebodyBesideTheStall()
    {
        var run = Caves();
        run.Engage("cave-mouth");
        SleepingPlacementTests.Win(run);
        run.Collect();
        run.Bag.AddCoins(new Money(0, 100, 0, 0));
        var aldric = Member(run, "Aldric");

        var far = run.Use("tobin", aldric);

        Assert.False(far.Success);
        Assert.Equal("Aldric is not close enough to Tobin.", far.Lines[0]);
        Assert.Equal("Nobody is close enough to trade with Tobin.", run.WhyNotBuy("tobin", Stocked(run, "tobin", Cure)));
        Assert.False(run.Buy("tobin", Stocked(run, "tobin", Cure)));

        // Beside the cart is as good as beside the man.
        Assert.True(run.Walk(aldric, new GridSquare(11, 38)));
        Assert.True(run.Use("tobin", aldric).Success);
        Assert.True(run.Buy("tobin", Stocked(run, "tobin", Cure)));
    }

    [Fact]
    public void SomebodyDownCannotGoToTrade()
    {
        var run = TobinOpen();
        var pip = Member(run, "Pip");
        pip.HitPoints.Take(pip.HitPoints.Current + 2);

        var refused = run.Use("tobin", pip);

        Assert.False(refused.Success);
        Assert.Equal("Pip is in no state to trade with Tobin.", refused.Lines[0]);
    }

    // ---- buying and selling ----

    [Fact]
    public void BuyingWithTooLittleIsRefusedAndChangesNothing()
    {
        var run = TobinOpen();
        run.Bag.AddCoins(new Money(0, 49, 9, 9));
        var purse = run.Bag.Money;

        var refused = run.Buy("tobin", Stocked(run, "tobin", Cure));

        Assert.False(refused);
        Assert.Equal("The potion of cure light wounds costs 50 gp, and the purse holds 49 gp 9 sp 9 cp.", refused.Line);
        Assert.Equal(purse, run.Bag.Money);
        Assert.False(run.Bag.Contains(Cure));
        Assert.Equal(3, StockOf(run, "tobin", Cure));
    }

    [Fact]
    public void BuyingMovesTheItemIntoTheBagAndLightensThePurse()
    {
        var run = TobinOpen();

        // The storeroom sack's silver and copper and the niche's gold, as the party would have them.
        run.Bag.AddCoins(new Money(0, 12, 400, 600));
        var weight = run.Bag.Weight;

        var bought = run.Buy("tobin", Stocked(run, "tobin", Cure));

        Assert.True(bought, bought.Line);
        Assert.Equal("Bought the potion of cure light wounds from Tobin for 50 gp.", bought.Line);
        Assert.Equal(1, run.Bag.CountOf(Cure));
        Assert.Equal(new Money(0, 8, 0, 0), run.Bag.Money);
        Assert.True(run.Bag.Weight < weight - 19);
        Assert.Equal(2, StockOf(run, "tobin", Cure));
    }

    [Fact]
    public void SeveralAreBoughtAtOnceOrNotAtAll()
    {
        var run = TobinOpen();
        run.Bag.AddCoins(new Money(0, 100, 0, 0));

        Assert.False(run.Buy("tobin", Stocked(run, "tobin", "alchemists-fire"), 4));
        Assert.Equal("Tobin has only 3 of the alchemist's fire.", run.WhyNotBuy("tobin", Stocked(run, "tobin", "alchemists-fire"), 4));

        var bought = run.Buy("tobin", Stocked(run, "tobin", "alchemists-fire"), 3);

        Assert.True(bought, bought.Line);
        Assert.Equal(3, run.Bag.CountOf("alchemists-fire"));
        Assert.Equal(new Money(0, 40, 0, 0), run.Bag.Money);
        Assert.DoesNotContain(run.GetMerchant("tobin")!.Stock, line => line.Id == "alchemists-fire");
        Assert.Equal("Tobin has no alchemist's fire to sell.", run.WhyNotBuy("tobin", new BagEntry(Item("alchemists-fire"))));
    }

    [Fact]
    public void SomethingSoldCanBeBoughtBackAtItsListPrice()
    {
        var run = TobinOpen();
        run.Bag.Add(Item("longsword"));

        Assert.Equal(750, run.OfferFor("tobin", run.InBag("longsword")));

        var sold = run.Sell("tobin", run.InBag("longsword"));

        Assert.True(sold, sold.Line);
        Assert.Equal("Sold the longsword to Tobin for 7 gp 5 sp.", sold.Line);
        Assert.False(run.Bag.Contains("longsword"));
        Assert.Equal(new Money(0, 7, 5, 0), run.Bag.Money);
        Assert.Equal(1, StockOf(run, "tobin", "longsword"));

        // Fifteen gold to have it back: half again what it fetched.
        Assert.False(run.Buy("tobin", Stocked(run, "tobin", "longsword")));
        run.Bag.AddCoins(new Money(0, 7, 5, 0));

        var bought = run.Buy("tobin", Stocked(run, "tobin", "longsword"));

        Assert.True(bought, bought.Line);
        Assert.True(run.Bag.Contains("longsword"));
        Assert.Equal(Money.None, run.Bag.Money);
        Assert.Equal(0, StockOf(run, "tobin", "longsword"));
    }

    [Fact]
    public void ValuablesFetchTheirWholeWorthAndNothingFetchesMoreThanTheLimit()
    {
        var run = TobinOpen();
        run.Bag.Add(Item("bloodstone"), 2);
        run.Bag.Add(Item("greatsword-plus-one"));

        Assert.Equal(50 * Pricing.CopperPerGold, run.OfferFor("tobin", run.InBag("bloodstone")));
        Assert.Equal(500 * Pricing.CopperPerGold, run.OfferFor("tobin", run.InBag("greatsword-plus-one")));

        Assert.True(run.Sell("tobin", run.InBag("bloodstone"), 2));
        Assert.True(run.Sell("tobin", run.InBag("greatsword-plus-one")));

        Assert.Equal(new Money(0, 600, 0, 0), run.Bag.Money);
    }

    [Fact]
    public void TobinWillNotBuyAnythingBroken()
    {
        var run = TobinOpen();
        run.Bag.Add(Item("longsword"), 1, broken: true);
        var broken = run.Bag.Entries.Single(line => line.IsBroken);

        Assert.Equal(0, run.OfferFor("tobin", broken));

        var refused = run.Sell("tobin", broken);

        Assert.False(refused);
        Assert.Equal("Tobin will not buy a broken longsword.", refused.Line);
        Assert.True(run.Bag.Contains("longsword"));
        Assert.Equal(Money.None, run.Bag.Money);
    }

    [Fact]
    public void OnlyTheBagIsForSale()
    {
        var run = TobinOpen();
        var pip = Member(run, "Pip");
        run.Bag.Add(Item(Cure));
        run.Stow(pip, run.InBag(Cure));

        var refused = run.Sell("tobin", new BagEntry(Item(Cure)));

        Assert.False(refused);
        Assert.Contains("goes back in the bag", refused.Line);
        Assert.Equal(1, Consumables.Count(pip, Item(Cure)));

        var sword = pip.Equipment.Worn.First(worn => worn.Item.IsWeapon && !worn.Item.IsNatural);
        Assert.False(run.Sell("tobin", new BagEntry(sword.Item)));
    }

    // ---- the pedlar ----

    [Fact]
    public void ThePedlarTradesFromTheStart()
    {
        var run = Campaign.Begin(Library, "the-long-road");
        var valeria = Member(run, "Valeria");
        var pedlar = run.GetMerchant("pedlar")!;

        Assert.True(pedlar.IsOpen);
        Assert.Equal("cart", pedlar.Feature.Stall);
        Assert.Equal(19, pedlar.Stock.Sum(line => line.Count));

        Assert.True(run.Walk(valeria, new GridSquare(5, 4)));
        Assert.Null(run.Alarm());

        var greeted = run.Use("pedlar", valeria);
        Assert.True(greeted.Success, greeted.Lines[0]);

        run.Bag.AddCoins(new Money(0, 60, 0, 0));
        Assert.True(run.Buy("pedlar", Stocked(run, "pedlar", "potion-of-mage-armor")));
        Assert.True(run.Bag.Contains("potion-of-mage-armor"));
        Assert.Equal(new Money(0, 10, 0, 0), run.Bag.Money);
    }

    // ---- saves ----

    [Fact]
    public void StockSurvivesASave()
    {
        var run = TobinOpen();
        run.Bag.AddCoins(new Money(0, 100, 0, 0));
        run.Bag.Add(Item("longsword"));
        run.Buy("tobin", Stocked(run, "tobin", Cure), 2);
        run.Sell("tobin", run.InBag("longsword"));

        var json = run.ToJson();
        var restored = Campaign.FromJson(json, Library);

        Assert.Equal(17, GameSave.FromJson(json).Version);
        Assert.Equal(run.GetMerchant("tobin")!.Stock, restored.GetMerchant("tobin")!.Stock);
        Assert.Equal(1, StockOf(restored, "tobin", Cure));
        Assert.Equal(1, StockOf(restored, "tobin", "longsword"));
        Assert.True(restored.GetMerchant("tobin")!.IsOpen);
        Assert.Equal(run.Bag.Money, restored.Bag.Money);
    }

    [Fact]
    public void ASaveFromSixteenFindsTheMerchantsAsTheLevelStocksThem()
    {
        var run = TobinOpen();
        run.Bag.AddCoins(new Money(0, 100, 0, 0));
        run.Buy("tobin", Stocked(run, "tobin", Cure), 2);

        var save = GameSave.FromJson(run.ToJson());
        var old = save with { Version = 16, Campaign = save.Campaign! with { Merchants = null } };

        var restored = Campaign.FromJson(GameSave.ToJson(old), Library);

        Assert.Equal(3, StockOf(restored, "tobin", Cure));
        Assert.Equal(11, restored.GetMerchant("tobin")!.Stock.Sum(line => line.Count));
        Assert.True(restored.GetMerchant("tobin")!.IsOpen);
        Assert.Equal(2, restored.Bag.CountOf(Cure));
    }

    [Fact]
    public void ARunOfSeparateFightsHasNobodyToTradeWith()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");

        Assert.Empty(run.Merchants);
        Assert.Equal("There is nobody like that to trade with.", run.WhyNotBuy("tobin", new BagEntry(Item(Cure))));

        var restored = Campaign.FromJson(run.ToJson(), ChainContent.Library);
        Assert.Empty(restored.Merchants);
    }
}

/// <summary>The new placements: the sergeant's draught and the road's and the caves' finds.</summary>
public class ConsumablePlacementTests
{
    private const string Cure = "potion-of-cure-light-wounds";

    private static ItemDefinition Item(string id) => ContentFiles.Default.GetItem(id)!;

    [Fact]
    public void TheSergeantWearsHisDraughtOnHisBelt()
    {
        var sergeant = ContentFiles.Default.BuildCreature("hobgoblin-sergeant")!;

        Assert.Equal([new BeltStack(Item(Cure), 1)], Consumables.OnBelt(sergeant));
    }

    [Fact]
    public void IfHeDoesNotDrinkItHisBodyHasIt()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Engage("goblin-ambush");
        var sergeant = run.Battle.Foes.Single(foe => foe.Name == "Sergeant Grask");
        SleepingPlacementTests.Win(run);
        run.Collect();

        var body = run.Containers.Single(container => container.Body == sergeant);
        Assert.Contains(body.Contents, entry => entry.Id == Cure);
    }

    [Fact]
    public void TheLevelsHideAFewDraughtsAndFlasks()
    {
        var caves = Campaign.Begin(ContentFiles.Default, "caves-of-shadow");
        var road = Campaign.Begin(ContentFiles.Default, "the-long-road");

        int In(Campaign run, string container, string id) =>
            run.GetContainer(container)!.Contents.Where(entry => entry.Id == id).Sum(entry => entry.Count);

        Assert.Equal(1, In(caves, "tobins-cart", Cure));
        Assert.Equal(2, In(caves, "storeroom-crates", "alchemists-fire"));
        Assert.Equal(1, In(road, "goblin-sack", "alchemists-fire"));
        Assert.Equal(2, In(road, "travellers-cart", Cure));
        Assert.Equal(1, In(road, "travellers-cart", "tanglefoot-bag"));
        Assert.Equal([new GridSquare(18, 6)], road.GetContainer("goblin-sack")!.Squares);
    }
}
