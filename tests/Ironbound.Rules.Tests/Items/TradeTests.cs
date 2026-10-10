using Ironbound.Rules.Content;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Items;

public class MoneyChangeTests
{
    [Fact]
    public void ASumIsCountedOutInTheFewestCoinsAndNeverPlatinum()
    {
        Assert.Equal(new Money(0, 54, 3, 2), Money.FromCopper(5432));
        Assert.Equal(new Money(0, 1234, 0, 0), Money.FromCopper(123400));
        Assert.Equal(new Money(0, 0, 0, 7), Money.FromCopper(7));
        Assert.Equal(Money.None, Money.FromCopper(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromCopper(-1));
    }

    [Fact]
    public void FiftyGoldFromTheStoreroomSackAndTheNicheSpendsTheSmallCoinsFirst()
    {
        // Six hundred copper, four hundred silver and twelve gold: forty-six gold in small coin
        // and twenty pounds of it, plus the twelve. Fifty gold takes all the copper and silver
        // and four of the gold.
        var purse = new Money(0, 12, 400, 600);

        var left = purse.Pay(50 * Pricing.CopperPerGold);

        Assert.Equal(new Money(0, 8, 0, 0), left);
        Assert.True(left!.Value.Weight < purse.Weight);
    }

    [Fact]
    public void WhatIsOverpaidComesBackInTheLargestCoins()
    {
        // Fifteen copper out of a gold piece: ninety-five back, as nine silver and five copper.
        Assert.Equal(new Money(0, 0, 9, 5), new Money(0, 1, 0, 0).Pay(5));
        Assert.Equal(new Money(0, 0, 8, 5), new Money(0, 1, 0, 0).Pay(15));

        // Three silver go first; the next coin up is a gold piece, and eight silver come back.
        Assert.Equal(new Money(0, 1, 8, 0), new Money(0, 2, 3, 0).Pay(50));

        // Platinum is spent last, and the change is in gold.
        Assert.Equal(new Money(1, 2, 0, 0), new Money(1, 5, 0, 0).Pay(300));
        Assert.Equal(new Money(0, 7, 0, 0), new Money(1, 0, 0, 0).Pay(300));
    }

    [Fact]
    public void APriceThePurseCannotCoverIsRefused()
    {
        Assert.Null(new Money(0, 49, 9, 9).Pay(5000));
        Assert.Null(Money.None.Pay(1));
        Assert.Equal(new Money(0, 3, 0, 0), new Money(0, 3, 0, 0).Pay(0));
    }

    [Fact]
    public void PayingAlwaysLeavesTheValueLessThePrice()
    {
        var purses = new[]
        {
            new Money(0, 12, 400, 600), new Money(2, 0, 0, 0), new Money(0, 3, 17, 9),
            new Money(1, 1, 1, 1), new Money(0, 0, 0, 5000), new Money(0, 50, 0, 0),
        };

        foreach (var purse in purses)
        {
            foreach (var price in new[] { 1, 9, 10, 11, 99, 100, 101, 999, 1000, 1001, 4999, 5000 })
            {
                if (purse.Pay(price) is not { } left)
                {
                    Assert.True(purse.Value < price);
                    continue;
                }

                // Not always lighter: a copper paid from a platinum piece brings back 9 gp 9 sp
                // 9 cp. Only ever the right sum, in coins that exist.
                Assert.Equal(purse.Value - price, left.Value);
                Assert.True(left.Platinum >= 0 && left.Gold >= 0 && left.Silver >= 0 && left.Copper >= 0);
                Assert.True(left.Platinum <= purse.Platinum);
            }
        }
    }
}

public class TradeOfferTests
{
    private static ItemDefinition Item(string id) => TestContent.Library.GetItem(id)!;

    private const int Limit = 500 * Pricing.CopperPerGold;

    [Fact]
    public void AMerchantAsksTheListPrice()
    {
        Assert.Equal(15 * Pricing.CopperPerGold, Trade.Asking(Item("longsword")));
        Assert.Equal(50 * Pricing.CopperPerGold, Trade.Asking(Item("potion-of-cure-light-wounds")));
    }

    [Fact]
    public void GearSellsForHalfAndValuablesForTheirWholeWorth()
    {
        Assert.Equal(750, Trade.Offer(Item("longsword"), broken: false, Limit));
        Assert.Equal(25 * Pricing.CopperPerGold, Trade.Offer(Item("potion-of-cure-light-wounds"), false, Limit));
        Assert.Equal(50 * Pricing.CopperPerGold, Trade.Offer(Item("bloodstone"), false, Limit));
    }

    [Fact]
    public void NoOneItemFetchesMoreThanThePurchaseLimit()
    {
        // A +1 greatsword lists at 2,350 gp; half is 1,175, and a thorp pays 500.
        Assert.Equal(2350 * Pricing.CopperPerGold, Item("greatsword-plus-one").Price);
        Assert.Equal(Limit, Trade.Offer(Item("greatsword-plus-one"), false, Limit));
        Assert.Equal(1175 * Pricing.CopperPerGold, Trade.Offer(Item("greatsword-plus-one"), false, 5000 * Pricing.CopperPerGold));
    }

    [Fact]
    public void NothingBrokenIsBoughtAndTeethAreNeverOffered()
    {
        Assert.Equal(0, Trade.Offer(Item("longsword"), broken: true, Limit));
        Assert.Equal(0, Trade.Offer(Item("bite"), broken: false, Limit));
    }
}

public class MerchantContentTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void TobinTradesFromTheOverturnedCartAtTheCaveMouth()
    {
        var caves = Library.GetLevel("caves-of-shadow")!;
        var tobin = caves.GetFeature("tobin")!;

        Assert.Equal(FeatureKind.Merchant, tobin.Kind);
        Assert.Equal("Tobin", tobin.Name);
        Assert.Equal([new GridSquare(9, 36), new GridSquare(9, 37), new GridSquare(10, 37)], tobin.Squares);
        Assert.Equal(new GridSquare(9, 36), tobin.StandsAt);
        Assert.Equal("cave-mouth", tobin.OpensAfter);
        Assert.Equal("cart-overturned", tobin.Stall);
        Assert.Equal("res://art/tobin.glb", tobin.Model);
        Assert.Equal(500, tobin.PurchaseLimit);
        Assert.Equal(0, tobin.Experience);
        Assert.All(tobin.Squares, square => Assert.Equal(LevelCell.Crate, caves.CellAt(square)));
        Assert.Equal(
            [new LootDefinition("potion-of-cure-light-wounds", 3), new LootDefinition("alchemists-fire", 3),
                new LootDefinition("acid-flask", 3), new LootDefinition("tanglefoot-bag"), new LootDefinition("thunderstone")],
            tobin.Stock);
    }

    [Fact]
    public void ThePedlarIsAtTheStartOfTheRoadAndOpenFromIt()
    {
        var road = Library.GetLevel("the-long-road")!;
        var pedlar = road.GetFeature("pedlar")!;

        Assert.Equal(FeatureKind.Merchant, pedlar.Kind);
        Assert.Equal([new GridSquare(4, 3), new GridSquare(5, 3), new GridSquare(6, 3)], pedlar.Squares);
        Assert.Equal(new GridSquare(4, 3), pedlar.StandsAt);
        Assert.Null(pedlar.OpensAfter);
        Assert.Equal("cart", pedlar.Stall);
        Assert.Equal("res://art/pedlar.glb", pedlar.Model);
        Assert.All(pedlar.Squares, square => Assert.Equal(LevelCell.Crate, road.CellAt(square)));

        // West of the ambush: nobody trades from inside the standing stones.
        var ambush = road.GetArea("goblin-ambush")!;
        Assert.All(pedlar.Squares, square => Assert.True(square.X < ambush.X));
        Assert.Equal(19, pedlar.Stock.Sum(line => line.Count));
    }

    [Fact]
    public void NoContainerIsBuriedInRockButTheNicheInTheWall()
    {
        foreach (var level in Library.Levels)
        {
            foreach (var feature in level.Features.Where(feature => feature.Kind == FeatureKind.Container))
            {
                foreach (var square in feature.Squares)
                {
                    var cell = level.CellAt(square);
                    var furniture = cell is LevelCell.Crate or LevelCell.Bed or LevelCell.Table;
                    var floor = !level.IsBlockedCell(square);

                    if (feature.Id == "junction-niche")
                    {
                        Assert.Equal(LevelCell.Wall, cell);
                    }
                    else
                    {
                        Assert.True(furniture || floor, $"{feature.Id} at {square} is in {cell}.");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheGoblinsSackLiesOnTheGroundBehindTheStandingStone()
    {
        var road = Library.GetLevel("the-long-road")!;
        var sack = road.GetFeature("goblin-sack")!;

        Assert.Equal([new GridSquare(18, 6)], sack.Squares);
        Assert.Equal(LevelCell.Stone, road.CellAt(18, 6));
        Assert.Equal(LevelCell.Rock, road.CellAt(17, 6));
    }

    [Fact]
    public void TheSergeantCarriesADraught()
    {
        Assert.Contains("potion-of-cure-light-wounds", Library.GetCreature("hobgoblin-sergeant")!.Items);
    }
}

public class MerchantValidationTests
{
    private static ContentLibrary With(string features, string map = "\"#####\", \"#.,c#\", \"#.cc#\", \"#####\"") =>
        ContentLibrary.Load(TestContent.Files().Append(("level.json", $$"""
            { "kind": "level", "id": "box", "name": "Box",
              "map": [ {{map}} ],
              "start": [ { "creature": "pip", "x": 1, "y": 1 } ],
              "areas": [
                { "id": "room", "name": "Room", "x": 1, "y": 1, "width": 3, "height": 2, "final": true,
                  "foes": [ { "creature": "goblin", "x": 1, "y": 2, "name": "G" } ] },
                { "id": "empty", "name": "Empty", "x": 1, "y": 1 } ],
              "features": [ {{features}} ] }
            """)));

    private const string Good = """
        "id": "trader", "kind": "merchant", "name": "Trader",
        "squares": [ { "x": 3, "y": 1 }, { "x": 3, "y": 2 } ], "standsAt": { "x": 3, "y": 1 },
        "stall": "cart", "model": "res://art/trader.glb"
        """;

    [Fact]
    public void AWellFormedMerchantLoads()
    {
        var library = With($$"""
            { {{Good}}, "opensAfter": "room", "purchaseLimit": 200, "closed": "Not yet.",
              "stock": [ "dagger", { "item": "acid-flask", "count": 2 } ] }
            """);

        Assert.Empty(library.Problems);

        var trader = library.GetLevel("box")!.GetFeature("trader")!;
        Assert.Equal(200, trader.PurchaseLimit);
        Assert.Equal("Not yet.", trader.Closed);
        Assert.Equal([new LootDefinition("dagger"), new LootDefinition("acid-flask", 2)], trader.Stock);
    }

    [Fact]
    public void AMerchantOnAGrassCellIsRefused()
    {
        var library = With("""
            { "id": "trader", "kind": "merchant", "name": "Trader",
              "squares": [ { "x": 2, "y": 1 } ], "standsAt": { "x": 2, "y": 1 } }
            """);

        Assert.Contains(library.Problems, problem => problem.Field == "features.trader" && problem.Message.Contains("Grass"));
    }

    [Fact]
    public void AMerchantStandsOnOneOfTheirOwnSquares()
    {
        var missing = With("""{ "id": "trader", "kind": "merchant", "name": "Trader", "squares": [ { "x": 3, "y": 1 } ] }""");
        var elsewhere = With("""
            { "id": "trader", "kind": "merchant", "name": "Trader",
              "squares": [ { "x": 3, "y": 1 } ], "standsAt": { "x": 2, "y": 2 } }
            """);

        Assert.Contains(missing.Problems, problem => problem.Field == "features.trader.standsAt");
        Assert.Contains(elsewhere.Problems, problem => problem.Field == "features.trader.standsAt");
    }

    [Fact]
    public void StockIsOfThingsThatExistAndCanBeCarried()
    {
        var library = With($$"""{ {{Good}}, "stock": [ "no-such-thing", "bite" ] }""");

        Assert.Contains(library.Problems, problem => problem.Field == "features.trader.stock" && problem.Message.Contains("no-such-thing"));
        Assert.Contains(library.Problems, problem => problem.Field == "features.trader.stock" && problem.Message.Contains("natural"));
    }

    [Fact]
    public void AMerchantOpensAfterARoomWithSomebodyInIt()
    {
        var unknown = With($$"""{ {{Good}}, "opensAfter": "nowhere" }""");
        var quiet = With($$"""{ {{Good}}, "opensAfter": "empty" }""");

        Assert.Contains(unknown.Problems, problem => problem.Field == "features.trader.opensAfter");
        Assert.Contains(quiet.Problems, problem => problem.Field == "features.trader.opensAfter" && problem.Message.Contains("nobody"));
    }

    [Fact]
    public void AMerchantHasNoLootOrCoinsAndNothingElseHasStock()
    {
        var looted = With($$"""{ {{Good}}, "loot": [ "dagger" ], "coins": { "gp": 5 } }""");
        var stocked = With("""
            { "id": "box", "kind": "container", "name": "box", "squares": [ { "x": 3, "y": 1 } ], "stock": [ "dagger" ] }
            """);

        Assert.Contains(looted.Problems, problem => problem.Field == "features.trader.loot");
        Assert.Contains(stocked.Problems, problem => problem.Field == "features.box.stock");
    }

    [Fact]
    public void SomebodyHasToBeAbleToStandBesideAMerchant()
    {
        var library = With(
            """{ "id": "trader", "kind": "merchant", "name": "Trader", "squares": [ { "x": 3, "y": 1 } ], "standsAt": { "x": 3, "y": 1 } }""",
            map: "\"#####\", \"#.#c#\", \"#.###\", \"#####\"");

        Assert.Contains(library.Problems, problem => problem.Field == "features.trader");
    }
}
