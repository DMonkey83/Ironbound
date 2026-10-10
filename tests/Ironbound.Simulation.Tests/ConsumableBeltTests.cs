using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation.Tests;

/// <summary>Potions and flasks between fights: hung on the belt, drunk, and kept by a save.</summary>
public class ConsumableBeltTests
{
    private const string Cure = "potion-of-cure-light-wounds";

    private static readonly ContentLibrary Library = TestLevel.Library();

    private static ItemDefinition Item(string id) => Library.GetItem(id)!;

    /// <summary>The test level, with so many of one thing in the bag.</summary>
    private static Campaign WithInBag(string id, int count)
    {
        var run = TestLevel.Begin(Library);
        run.Bag.Add(Item(id), count);
        return run;
    }

    [Fact]
    public void HangingSoManyMovesThatManyAndThreePotionsAreOneStack()
    {
        var run = WithInBag(Cure, 4);
        var pip = run.Pip();

        var hung = run.Stow(pip, run.InBag(Cure), 3);

        Assert.True(hung);
        Assert.Equal("Pip hangs potion of cure light wounds ×3 on the belt.", hung.Line);
        Assert.Equal(1, run.Bag.CountOf(Cure));
        Assert.Equal(3, pip.Equipment.Worn.Count(entry => entry.Item.Id == Cure && entry.Slot == EquipmentSlot.Carried));
        Assert.Equal([new BeltStack(Item(Cure), 3)], Consumables.OnBelt(pip));
    }

    [Fact]
    public void HangingMoreThanTheBagHoldsMovesNothing()
    {
        var run = WithInBag(Cure, 2);
        var pip = run.Pip();

        Assert.False(run.Stow(pip, run.InBag(Cure), 3));
        Assert.False(run.Stow(pip, run.InBag(Cure), 0));

        Assert.Equal(2, run.Bag.CountOf(Cure));
        Assert.Empty(Consumables.OnBelt(pip));
    }

    [Fact]
    public void WhatHangsOnTheBeltCountsOnTheLoad()
    {
        var run = WithInBag("tanglefoot-bag", 3);
        var pip = run.Pip();
        var own = Encumbrance.Carried(pip);
        var party = PartyEncumbrance.PartyLoad(run).Weight;

        run.Stow(pip, run.InBag("tanglefoot-bag"), 3);

        // Pip is Small, and a tanglefoot bag weighs four pounds on anybody.
        Assert.Equal(own + 12, Encumbrance.Carried(pip));
        Assert.Equal(party, PartyEncumbrance.PartyLoad(run).Weight);
    }

    [Fact]
    public void DrinkingBetweenFightsHealsAndUsesOneFromTheBeltFirst()
    {
        var run = WithInBag(Cure, 3);
        var pip = run.Pip();
        run.Stow(pip, run.InBag(Cure), 2);
        pip.HitPoints.Take(5);
        var before = pip.HitPoints.Current;

        var drunk = run.Drink(pip, Item(Cure));

        Assert.True(drunk, drunk.Line);
        Assert.StartsWith("Pip drinks the potion of cure light wounds: healed ", drunk.Line);
        Assert.True(pip.HitPoints.Current > before);
        Assert.Equal(1, Consumables.Count(pip, Item(Cure)));
        Assert.Equal(1, run.Bag.CountOf(Cure));
    }

    [Fact]
    public void WithNoneOnTheBeltItComesFromTheBag()
    {
        var run = WithInBag(Cure, 1);
        var pip = run.Pip();
        pip.HitPoints.Take(5);

        var drunk = run.Drink(pip, Item(Cure));

        Assert.True(drunk, drunk.Line);
        Assert.Contains("from the bag", drunk.Line);
        Assert.Equal(0, run.Bag.CountOf(Cure));
        Assert.False(run.Drink(pip, Item(Cure)));
    }

    [Fact]
    public void ADyingFriendCanBeGivenOne()
    {
        var run = WithInBag(Cure, 1);
        var aldric = run.Aldric();
        aldric.HitPoints.Take(aldric.HitPoints.Current + 1);
        Assert.False(aldric.IsConscious);

        var given = run.Drink(aldric, Item(Cure));

        Assert.True(given, given.Line);
        Assert.StartsWith("Aldric is given the potion of cure light wounds from the bag: healed ", given.Line);
        Assert.True(aldric.IsConscious);
    }

    [Fact]
    public void NobodyDrinksInTheMiddleOfAFightOrDrinksAFlask()
    {
        var run = WithInBag(Cure, 1);
        run.Bag.Add(Item("acid-flask"));
        var pip = run.Pip();
        run.Stow(pip, run.InBag(Cure));
        pip.HitPoints.Take(5);

        Assert.False(run.Drink(pip, Item("acid-flask")));

        run.Engage("far");
        var refused = run.Drink(pip, Item(Cure));

        Assert.False(refused);
        Assert.Contains("in a fight", refused.Line);
        Assert.Equal(1, Consumables.Count(pip, Item(Cure)));
    }

    [Fact]
    public void ThreePotionsOnABeltSurviveASave()
    {
        var run = WithInBag(Cure, 3);
        run.Bag.Add(Item("alchemists-fire"));
        run.Stow(run.Pip(), run.InBag(Cure), 3);
        run.Stow(run.Pip(), run.InBag("alchemists-fire"));

        var json = run.ToJson();
        var restored = Campaign.FromJson(json, Library);

        // The belt needed no new field: it rides in the creature's items, whatever the version.
        Assert.Equal(SavedGame.CurrentVersion, GameSave.FromJson(json).Version);
        Assert.Equal(
            [new BeltStack(Item(Cure), 3), new BeltStack(Item("alchemists-fire"), 1)],
            Consumables.OnBelt(restored.Pip()));
        Assert.True(restored.Pip().Equipment.UseUp(Item(Cure)));
        Assert.Equal(2, Consumables.Count(restored.Pip(), Item(Cure)));
    }
}
