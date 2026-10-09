using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation.Tests;

/// <summary>
/// A room made for testing loot: a locked chest, a sack on the floor, a loose stone hidden in
/// the wall, and a walled-off den to win.
/// <code>
///   0123456789ABC
/// 0 #############
/// 1 #c..s.P.#...#     c = the chest (locked), s = the sack, P = Pip
/// 2 #.......#.g.#     den: x9-11, y1-3 (a goblin; final)
/// 3 n.....A.#...#     n = the loose stone (hidden), A = Aldric
/// 4 #############
/// </code>
/// </summary>
internal static class LootLevel
{
    public static ContentLibrary Library(int lockDc = 15, int breakDc = 16, int hiddenDc = 15)
    {
        var files = ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).ToList();

        files.Add(("loot-level.json", $$"""
            { "kind": "level", "id": "loot-level", "name": "Loot Level",
              "stone": "rocky", "grass": "woodland",
              "map": [
                "#############",
                "#c......#...#",
                "#.......#...#",
                "#.......#...#",
                "#############" ],
              "start": [
                { "creature": "pip", "x": 6, "y": 1 },
                { "creature": "aldric", "x": 6, "y": 3 } ],
              "areas": [
                { "id": "hall", "name": "The Hall", "x": 1, "y": 1, "width": 7, "height": 3 },
                { "id": "den", "name": "The Den", "x": 9, "y": 1, "width": 3, "height": 3, "final": true,
                  "loot": [ { "item": "dagger", "count": 2 } ],
                  "foes": [ { "creature": "goblin", "x": 10, "y": 2, "name": "Snitch" } ] } ],
              "features": [
                { "id": "chest", "kind": "container", "look": "chest", "name": "the chest",
                  "squares": [ { "x": 1, "y": 1 } ], "lockDc": {{lockDc}}, "breakDc": {{breakDc}},
                  "loot": [ "greatsword-plus-one", { "item": "bloodstone", "count": 3 } ],
                  "coins": { "gp": 5, "sp": 20 } },
                { "id": "sack", "kind": "cache", "look": "sack", "name": "the sack",
                  "squares": [ { "x": 4, "y": 1 } ],
                  "coins": { "sp": 400, "cp": 600 } },
                { "id": "stone", "kind": "container", "look": "pile", "name": "a loose stone",
                  "squares": [ { "x": 0, "y": 3 } ], "hiddenDc": {{hiddenDc}},
                  "loot": [ "gold-garnet-ring" ] } ] }
            """));

        files.Add(("loot-level-campaign.json", """
            { "kind": "campaign", "id": "loot-run", "name": "Loot Run", "level": "loot-level", "rests": 1 }
            """));

        var library = ContentLibrary.Load(files);
        Assert.Empty(library.Problems);
        return library;
    }

    public static Campaign Begin(ContentLibrary? library = null, ulong seed = 20260920) =>
        Campaign.Begin(library ?? Library(), "loot-run", seed);

    /// <summary>Everything the party owns or could pick up, by item id: the bag, what they wear, every container.</summary>
    public static Dictionary<string, int> Census(Campaign run)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(string id, int n) => counts[id] = counts.GetValueOrDefault(id) + n;

        foreach (var entry in run.Bag.Entries)
        {
            Count(entry.Id, entry.Count);
        }

        foreach (var worn in run.Party.SelectMany(member => member.Equipment.Worn).Where(worn => !worn.Item.IsNatural))
        {
            Count(worn.Item.Id, 1);
        }

        foreach (var entry in run.Containers.SelectMany(container => container.Contents))
        {
            Count(entry.Id, entry.Count);
        }

        return counts;
    }
}

public class PartyBagTests
{
    private static ItemDefinition Item(string id) => ContentFiles.Default.GetItem(id)!;

    [Fact]
    public void IdenticalPlainGearStacks()
    {
        var bag = new PartyBag();
        bag.Add(Item("dagger"));
        bag.Add(Item("dagger"));

        var line = Assert.Single(bag.Entries);
        Assert.Equal(2, line.Count);
        Assert.Equal("dagger ×2", line.ToString());
    }

    [Fact]
    public void ABrokenOneNeverStacksWithAWholeOne()
    {
        var bag = new PartyBag();
        bag.Add(Item("dagger"));
        bag.Add(Item("dagger"), broken: true);

        Assert.Equal(2, bag.Entries.Count);
        Assert.Contains(bag.Entries, entry => entry.IsBroken && entry.ToString() == "dagger (broken)");
        Assert.Equal(2, bag.CountOf("dagger"));
    }

    [Fact]
    public void SomethingEnchantedKeepsALineOfItsOwn()
    {
        var bag = new PartyBag();
        bag.Add(Item("greatsword-plus-one"), 2);

        Assert.Equal(2, bag.Entries.Count);
    }

    [Fact]
    public void TeethNeverGoInABag()
    {
        var bag = new PartyBag();

        Assert.False(bag.Add(Item("bite")));
        Assert.Empty(bag.Entries);
    }

    [Fact]
    public void RemovingIsAllOrNothing()
    {
        var bag = new PartyBag();
        bag.Add(Item("dagger"), 2);

        Assert.False(bag.Remove(new BagEntry(Item("dagger")), 3));
        Assert.Equal(2, bag.CountOf("dagger"));
        Assert.True(bag.Remove(new BagEntry(Item("dagger")), 2));
        Assert.Empty(bag.Entries);
    }

    [Fact]
    public void ItWeighsWhatItHoldsCoinsIncluded()
    {
        var bag = new PartyBag();
        bag.Add(Item("longsword"));
        bag.AddCoins(new Money(0, 0, 400, 600));

        // A longsword's four pounds, and a thousand coins at fifty to the pound.
        Assert.Equal(24m, bag.Weight);
        Assert.Equal(4600, bag.Purse);
        Assert.Equal(1500, bag.Value);
    }
}

public class ContainerTests
{
    [Fact]
    public void OpeningOnlyOpens()
    {
        var run = LootLevel.Begin();
        run.Walk(run.Pip(), new GridSquare(4, 2));

        var result = run.Open("sack", run.Pip());

        Assert.True(result.Success);
        var sack = run.GetContainer("sack")!;
        Assert.True(sack.IsOpen);
        Assert.Equal(new Money(0, 0, 400, 600), sack.Money);
        Assert.Equal(0, run.Bag.Purse);
    }

    [Fact]
    public void TheOldSwapLoopCannotHappen()
    {
        var library = LootLevel.Library(lockDc: -10);
        var run = LootLevel.Begin(library);
        run.Walk(run.Pip(), new GridSquare(2, 1));
        run.Open("chest", run.Pip());
        var sword = run.GetContainer("chest")!.Contents.Single(entry => entry.Id == "greatsword-plus-one");

        Assert.True(run.Take("chest", sword));

        // The second take finds nothing there: it is in the bag now, and only once.
        var again = run.Take("chest", sword);
        Assert.False(again);
        Assert.Contains("no greatsword +1", again.Line);
        Assert.Equal(1, run.Bag.CountOf("greatsword-plus-one"));
    }

    [Fact]
    public void TakingAllTakesTheCoinsToo()
    {
        var library = LootLevel.Library(lockDc: -10);
        var run = LootLevel.Begin(library);
        run.Walk(run.Pip(), new GridSquare(2, 1));
        run.Open("chest", run.Pip());

        Assert.True(run.TakeAll("chest"));

        Assert.True(run.GetContainer("chest")!.IsEmpty);
        Assert.Equal(3, run.Bag.CountOf("bloodstone"));
        Assert.Single(run.Bag.Entries, entry => entry.Id == "bloodstone");
        Assert.Equal(new Money(0, 5, 20, 0), run.Bag.Money);
        Assert.True(run.IsUsed("chest"));
        Assert.False(run.TakeAll("chest"));
    }

    [Fact]
    public void NothingIsTakenFromAShutContainer()
    {
        var run = LootLevel.Begin();

        Assert.False(run.TakeAll("sack"));
        Assert.Equal(0, run.Bag.Purse);
    }

    [Fact]
    public void SomebodyHasToBeBesideIt()
    {
        var run = LootLevel.Begin();

        var result = run.Open("chest", run.Aldric());

        Assert.False(result.Success);
        Assert.Contains("not close enough", result.Lines[0]);
        Assert.False(run.CanUse("chest", run.Aldric()));
    }

    [Fact]
    public void ALockedChestIsPickedBySomebodyTrained()
    {
        var run = LootLevel.Begin(LootLevel.Library(lockDc: -10));
        run.Walk(run.Pip(), new GridSquare(2, 1));

        var result = run.Use("chest", run.Pip());

        Assert.True(result.Success);
        Assert.Contains("picks the lock", result.Lines[0]);
        Assert.Contains("Disable Device", result.Lines[1]);
        Assert.False(result.Noisy);
        Assert.False(run.GetContainer("chest")!.IsLocked);
    }

    [Fact]
    public void AndForcedBySomebodyWhoIsNotAndThatIsNoisy()
    {
        var run = LootLevel.Begin(LootLevel.Library(breakDc: -10));
        run.Walk(run.Aldric(), new GridSquare(2, 2));

        var result = run.Open("chest", run.Aldric());

        Assert.True(result.Success);
        Assert.Contains("forces", result.Lines[0]);
        Assert.Contains("Strength", result.Lines[1]);
        Assert.True(result.Noisy);
    }

    [Fact]
    public void AFailedLockStaysLockedAndCanBeTriedAgain()
    {
        var run = LootLevel.Begin(LootLevel.Library(lockDc: 100));
        run.Walk(run.Pip(), new GridSquare(2, 1));

        var result = run.Open("chest", run.Pip());

        Assert.False(result.Success);
        Assert.True(run.GetContainer("chest")!.IsLocked);
        Assert.False(run.GetContainer("chest")!.IsOpen);
        Assert.True(run.CanUse("chest", run.Pip()));
    }

    [Fact]
    public void TakingEverythingNearbyEmptiesWhatIsOpenAndLeavesWhatIsShut()
    {
        var run = LootLevel.Begin(LootLevel.Library(lockDc: -10));
        run.Walk(run.Pip(), new GridSquare(4, 2));
        run.Open("sack", run.Pip());

        Assert.True(run.TakeAllNearby(new GridSquare(4, 2), 5));

        Assert.Equal(new Money(0, 0, 400, 600), run.Bag.Money);
        Assert.False(run.GetContainer("chest")!.IsEmpty);
    }

    [Fact]
    public void NothingIsHandledInAFight()
    {
        var run = LootLevel.Begin();
        run.Engage("den");

        Assert.False(run.Open("sack", run.Pip()).Success);
        Assert.False(run.TakeAll("sack"));
        Assert.False(run.Unequip(run.Aldric(), EquipmentSlot.MainHand));
    }
}

public class HiddenContainerTests
{
    [Fact]
    public void ItIsNotThereUntilSomebodyNoticesIt()
    {
        var run = LootLevel.Begin(LootLevel.Library(hiddenDc: 100));

        Assert.DoesNotContain(run.Containers, container => container.Id == "stone");
        Assert.Null(run.GetContainer("stone"));
        Assert.Null(run.FeatureAt(new GridSquare(0, 3)));
        Assert.False(run.Open("stone", run.Pip()).Success);
    }

    [Fact]
    public void ARollThatFailsLeavesItHiddenAndIsNeverRolledAgain()
    {
        var library = LootLevel.Library(hiddenDc: 100);
        var run = LootLevel.Begin(library);

        run.Walk(run.Pip(), new GridSquare(2, 3));
        var rolled = Dice(run);
        run.Walk(run.Pip(), new GridSquare(1, 3));
        run.Walk(run.Pip(), new GridSquare(2, 3));

        // Out of reach of a natural 20 and then some; and the walks back and forth roll nothing.
        Assert.DoesNotContain(run.Containers, container => container.Id == "stone");
        Assert.Empty(run.TakeNotices());
        Assert.Equal(rolled, Dice(run));
    }

    [Fact]
    public void ASuccessShowsItToEverybody()
    {
        var run = LootLevel.Begin(LootLevel.Library(hiddenDc: -100));

        run.Walk(run.Pip(), new GridSquare(1, 3));

        var notice = Assert.Single(run.TakeNotices());
        Assert.Equal("stone", notice.Container.Id);
        Assert.Same(run.Pip(), notice.Who);
        Assert.Contains("Pip notices a loose stone", notice.Line);
        Assert.Contains(run.Containers, container => container.Id == "stone");
        Assert.Equal("stone", run.FeatureAt(new GridSquare(0, 3))!.Id);
        Assert.True(run.Open("stone", run.Pip()).Success);
    }

    [Fact]
    public void NobodyRollsFromFurtherThanTenFeet()
    {
        var run = LootLevel.Begin(LootLevel.Library(hiddenDc: -100));

        run.Walk(run.Pip(), new GridSquare(3, 1));

        Assert.Empty(run.TakeNotices());
        Assert.Null(run.GetContainer("stone"));
    }

    [Fact]
    public void WhoHasLookedSurvivesASave()
    {
        var library = LootLevel.Library(hiddenDc: 100);
        var run = LootLevel.Begin(library);
        run.Walk(run.Pip(), new GridSquare(2, 3));

        var restored = Campaign.FromJson(run.ToJson(), library);
        var before = Dice(restored);
        restored.Walk(restored.Pip(), new GridSquare(1, 3));

        Assert.Equal(before, Dice(restored));
    }

    /// <summary>The exploring dice's state, read off a save: unchanged means nothing was rolled.</summary>
    private static (ulong, ulong) Dice(Campaign run)
    {
        var level = GameSave.FromJson(run.ToJson()).Campaign!.Level!;
        return (level.RandomA, level.RandomB);
    }
}

public class EquippingTests
{
    private static Campaign Looted()
    {
        var run = LootLevel.Begin(LootLevel.Library(lockDc: -10));
        run.Walk(run.Pip(), new GridSquare(2, 1));
        run.Open("chest", run.Pip());
        run.TakeAll("chest");
        return run;
    }

    [Fact]
    public void EquippingSwapsAndTheSwapIsInPlainSight()
    {
        var run = Looted();
        var aldric = run.Aldric();

        var result = run.Equip(aldric, run.InBag("greatsword-plus-one"));

        Assert.True(result);
        Assert.Equal("Aldric takes up the greatsword +1 and puts the greatsword in the bag.", result.Line);
        Assert.Equal("greatsword +1", aldric.PrimaryAttack!.Name);
        Assert.True(run.Bag.Contains("greatsword"));
        Assert.False(run.Bag.Contains("greatsword-plus-one"));
    }

    [Fact]
    public void NoItemIsEverMadeOrLostOnTheWay()
    {
        var run = Looted();
        var aldric = run.Aldric();
        var pip = run.Pip();
        var before = LootLevel.Census(run);

        run.Equip(aldric, run.InBag("greatsword-plus-one"));
        run.Equip(aldric, run.InBag("greatsword"));
        run.Unequip(aldric, EquipmentSlot.MainHand);
        run.Stow(pip, run.InBag("greatsword-plus-one"));
        run.Unstow(pip, pip.Equipment.Worn.Single(worn => worn.Item.Id == "greatsword-plus-one"));
        run.Drop(run.InBag("bloodstone"), 2);
        run.Equip(aldric, run.InBag("greatsword"));
        run.TakeAll(run.Containers.Single(container => container.Look == ContainerLook.Pile).Id);

        // Taking off and putting on again twice over, a belt, the ground and back.
        Assert.Equal(before, LootLevel.Census(run));
    }

    [Fact]
    public void ATwoHandedWeaponWaitsForTheShieldToComeOff()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        SleepingPlacementTests.Win(run);
        run.Collect();
        run.TakeEverything();
        var bag = run.Bag;
        bag.Add(ContentFiles.Default.GetItem("greatsword")!);
        var valeria = run.Party.Single(one => one.Name == "Valeria");

        var refused = run.Equip(valeria, run.InBag("greatsword"));

        // Said why, and the shield left where it was rather than quietly taken off.
        Assert.False(refused);
        Assert.Contains("heavy shield", refused.Line);
        Assert.True(valeria.Equipment.HasShield);
        Assert.Equal("longsword", valeria.PrimaryAttack!.Name);

        run.Unequip(valeria, EquipmentSlot.Shield);
        Assert.True(run.Equip(valeria, run.InBag("greatsword")));
    }

    [Fact]
    public void SomethingThatDoesNotGoThereIsRefused()
    {
        var run = Looted();

        Assert.False(run.Equip(run.Aldric(), run.InBag("bloodstone")));
        Assert.False(run.Equip(run.Aldric(), run.InBag("greatsword-plus-one"), EquipmentSlot.Head));
        Assert.False(run.Equip(run.Aldric(), run.InBag("greatsword-plus-one"), EquipmentSlot.OffHand));
    }

    [Fact]
    public void NobodyOutsideThePartyIsDressed()
    {
        var run = Looted();
        var stranger = ContentFiles.Default.BuildCreature("goblin")!;

        Assert.False(run.Equip(stranger, run.InBag("greatsword-plus-one")));
        Assert.True(run.Bag.Contains("greatsword-plus-one"));
    }

    [Fact]
    public void ABrokenBladeStaysBrokenInTheBagAndInTheHand()
    {
        var run = Looted();
        var aldric = run.Aldric();
        aldric.Equipment.Break(aldric.PrimaryAttack!);

        run.Unequip(aldric, EquipmentSlot.MainHand);
        var broken = Assert.Single(run.Bag.Entries, entry => entry.Id == "greatsword");
        Assert.True(broken.IsBroken);

        run.Equip(aldric, broken);
        Assert.True(aldric.Equipment.IsBroken(aldric.PrimaryAttack!));
    }

    [Fact]
    public void ABeltWeaponIsStillAWeapon()
    {
        var run = Looted();
        var pip = run.Pip();

        Assert.True(run.Stow(pip, run.InBag("greatsword-plus-one")));

        Assert.Contains(pip.Attacks, attack => attack.Name == "greatsword +1");
        Assert.Equal(EquipmentSlot.Carried, pip.Equipment.Worn.Single(worn => worn.Item.Id == "greatsword-plus-one").Slot);
    }

    [Fact]
    public void DroppingLeavesAPileAtTheLeadersFeet()
    {
        var run = Looted();

        Assert.True(run.Drop(run.InBag("bloodstone"), 2));

        var pile = run.ContainerAt(run.Field.SquareOf(run.Pip())!.Value)!;
        Assert.Equal(ContainerLook.Pile, pile.Look);
        Assert.Equal(2, pile.Contents.Single().Count);
        Assert.Equal(1, run.Bag.CountOf("bloodstone"));

        Assert.True(run.DropCoins(new Money(0, 5, 0, 0)));
        Assert.Equal(new Money(0, 5, 0, 0), pile.Money);
    }
}

public class BodyTests
{
    [Fact]
    public void EachOfTheFallenIsABodyWhereItFell()
    {
        var run = LootLevel.Begin();
        run.Engage("den");
        var snitch = run.Battle.Foes.Single();
        var square = run.Field.SquareOf(snitch)!.Value;
        SleepingPlacementTests.Win(run);

        Assert.True(run.Collect() > 0);

        var body = Assert.Single(run.Containers, container => container.Look == ContainerLook.Body);
        Assert.Same(snitch, body.Body);
        Assert.Equal([square], body.Squares);
        Assert.True(body.IsOpen);
        Assert.Contains(body.Contents, entry => entry.Id == "scimitar");
        Assert.Contains(body.Contents, entry => entry.Id == "studded-leather");
        Assert.Empty(snitch.Equipment.Worn);

        // And the room's own loot as a pile, counts and all.
        var pile = Assert.Single(run.Containers, container => container.Id == "loot:den");
        Assert.Equal(2, pile.Contents.Single(entry => entry.Id == "dagger").Count);
    }

    [Fact]
    public void ThePursesAreRolledFromTheSeedSoAReplayFindsTheSameCoins()
    {
        static Money Purse(ulong seed)
        {
            var run = LootLevel.Begin(seed: seed);
            run.Engage("den");
            SleepingPlacementTests.Win(run);
            run.Collect();
            return run.Containers.Single(container => container.Look == ContainerLook.Body).Money;
        }

        Assert.Equal(Purse(7), Purse(7));
        Assert.False(Purse(7).IsEmpty);
        Assert.Contains(Enumerable.Range(1, 10), seed => Purse((ulong)seed) != Purse(7));
    }

    [Fact]
    public void TeethStayOnTheWerewolf()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        SleepingPlacementTests.Win(run);
        run.Advance();
        var werewolf = run.Battle.Foes.Single();
        SleepingPlacementTests.Win(run);

        run.Collect();
        Assert.False(run.Containers.Single(container => container.Body == werewolf).Money.IsEmpty);
        run.TakeEverything();

        // The old sweep put the werewolf's bite in the sack. A body only holds what can be carried.
        Assert.False(run.Bag.Contains("bite"));
        Assert.DoesNotContain(run.Lying(), entry => entry.Id == "bite");
        Assert.True(werewolf.Equipment.Has("bite"));
        Assert.True(run.Bag.Purse > 0);
    }

    [Fact]
    public void ADireRatLeavesNothingButABody()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        for (var chapter = 1; chapter < 3; chapter++)
        {
            SleepingPlacementTests.Win(run);
            run.Advance();
        }

        SleepingPlacementTests.Win(run);
        run.Collect();

        var rat = run.Containers.Single(container => container.Body?.DefinitionId == "dire-rat");
        Assert.True(rat.IsEmpty);
    }

    [Fact]
    public void BodiesComeBackFromASaveDrawableAndAsTheyWereLeft()
    {
        var library = LootLevel.Library();
        var run = LootLevel.Begin(library);
        run.Engage("den");
        SleepingPlacementTests.Win(run);
        run.Collect();
        var body = run.Containers.Single(container => container.Look == ContainerLook.Body);
        run.Take(body.Id, body.Contents.Single(entry => entry.Id == "scimitar"));

        var restored = Campaign.FromJson(run.ToJson(), library);
        var back = restored.Containers.Single(container => container.Look == ContainerLook.Body);

        Assert.Equal(body.Id, back.Id);
        Assert.Equal(body.Squares, back.Squares);
        Assert.Equal(body.Money, back.Money);
        Assert.Equal(body.Contents, back.Contents);
        Assert.Equal("Snitch", back.Body!.Name);
        Assert.Equal("goblin", back.Body.DefinitionId);
        Assert.False(back.Body.IsConscious);
        Assert.Empty(back.Body.Equipment.Worn);
        Assert.True(restored.Bag.Contains("scimitar"));
    }
}

public class LootSaveTests
{
    [Fact]
    public void EverythingAboutTheLootSurvivesASave()
    {
        var library = LootLevel.Library(lockDc: -10, hiddenDc: -100);
        var run = LootLevel.Begin(library);
        run.Walk(run.Pip(), new GridSquare(2, 2));
        run.Open("chest", run.Pip());
        run.Take("chest", run.GetContainer("chest")!.Contents.Single(entry => entry.Id == "bloodstone"), 2);
        run.TakeCoins("chest");
        run.Bag.Add(library.GetItem("dagger")!, 1, broken: true);
        run.Drop(run.InBag("bloodstone"));

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.Equal(run.Bag.Entries, restored.Bag.Entries);
        Assert.Equal(run.Bag.Money, restored.Bag.Money);
        Assert.Equal(
            run.Containers.Select(Describe),
            restored.Containers.Select(Describe));
        Assert.Contains(restored.Containers, container => container.Id == "stone");
    }

    [Fact]
    public void ALockedChestStaysLockedAndAShutOneShut()
    {
        var library = LootLevel.Library();
        var run = LootLevel.Begin(library);

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.True(restored.GetContainer("chest")!.IsLocked);
        Assert.False(restored.GetContainer("sack")!.IsOpen);
        Assert.Null(restored.GetContainer("stone"));
    }

    [Fact]
    public void ASaveFromVersionFifteenLoads()
    {
        var library = LootLevel.Library(lockDc: -10);
        var run = LootLevel.Begin(library);
        run.Walk(run.Pip(), new GridSquare(2, 1));
        run.Open("chest", run.Pip());

        // Written as fifteen wrote it: no bag, no containers, the sack as ids, and the chest
        // among the features already seen to.
        var save = GameSave.FromJson(run.ToJson());
        var old = save with
        {
            Version = 15,
            Campaign = save.Campaign! with
            {
                Stash = ["light-crossbow", "dagger", "dagger", "bite"],
                Bag = null,
                Purse = null,
                Containers = null,
                Level = save.Campaign.Level! with { Used = ["chest"] },
            },
        };

        var restored = Campaign.FromJson(GameSave.ToJson(old), library);

        // The sack poured into the bag, teeth left out; the searched chest open and empty, as
        // its contents were in the sack already; the rest as the level file fills it.
        Assert.Equal(2, restored.Bag.CountOf("dagger"));
        Assert.True(restored.Bag.Contains("light-crossbow"));
        Assert.False(restored.Bag.Contains("bite"));
        Assert.True(restored.GetContainer("chest")!.IsOpen);
        Assert.True(restored.GetContainer("chest")!.IsEmpty);
        Assert.True(restored.IsUsed("chest"));
        Assert.Equal(new Money(0, 0, 400, 600), restored.GetContainer("sack")!.Money);
        Assert.DoesNotContain(restored.Containers, container => container.Look == ContainerLook.Body);
    }

    [Fact]
    public void AChapterRunFromVersionFifteenLoadsItsSackIntoTheBag()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var save = GameSave.FromJson(run.ToJson());
        var old = save with
        {
            Version = 15,
            Campaign = save.Campaign! with { Stash = ["silvered-longsword"], Bag = null, Purse = null, Containers = null },
        };

        var restored = Campaign.FromJson(GameSave.ToJson(old), ChainContent.Library);

        Assert.True(restored.Bag.Contains("silvered-longsword"));
        Assert.Empty(restored.Containers);
    }

    private static string Describe(Container container) =>
        $"{container.Id}|{container.Name}|{container.Look}|{string.Join(";", container.Squares)}|{container}|"
        + $"{container.IsOpen}|{container.IsLocked}|{container.IsHidden}|{container.Body?.Name}";
}

public class PartyLoadTests
{
    private static ItemDefinition Item(string id) => ContentFiles.Default.GetItem(id)!;

    [Fact]
    public void TheBagIsSharedOutByStrength()
    {
        var run = LootLevel.Begin();
        var load = Encumbrance.PartyLoad(run);

        // Aldric's 76 light and Pip's 24.75 (a halfling's three quarters of 33); their own gear,
        // Aldric's 38 lb and Pip's half-weight 8.5.
        Assert.Equal(100.75m, load.Capacity.Light);
        Assert.Equal(46.5m, load.Weight);
        Assert.Equal(LoadCategory.Light, load.Category);

        run.Bag.AddCoins(new Money(0, 0, 3000, 0));

        load = Encumbrance.PartyLoad(run);
        Assert.Equal(106.5m, load.Weight);
        Assert.Equal(LoadCategory.Medium, load.Category);
        Assert.Equal(LoadCategory.Medium, Encumbrance.Effective(run.Aldric(), run));
        Assert.Equal(LoadCategory.Medium, Encumbrance.Effective(run.Aldric()));
        Assert.Equal(LoadCategory.Light, Encumbrance.Load(run.Aldric()).Category);
    }

    [Fact]
    public void AHeavierBagSlowsEverybodyAndCapsTheirDexterity()
    {
        var run = LootLevel.Begin();
        var pip = run.Pip();
        var speed = pip.CurrentSpeed;
        var armour = pip.ArmorClass.Total;

        run.Bag.AddCoins(new Money(0, 0, 3000, 0));

        // Medium: a halfling's twenty feet to fifteen. Pip's +3 is still under a medium cap.
        Assert.Equal(20, speed);
        Assert.Equal(15, pip.CurrentSpeed);
        Assert.Equal(armour, pip.ArmorClass.Total);
        Assert.Contains("Medium load check penalty", pip.Skills.Explain(Rules.Skills.Skill.Climb).ToString());

        // Heavy: Dexterity to armour class at +1, and the sheet says why.
        run.Bag.AddCoins(new Money(0, 0, 5000, 0));

        Assert.Equal(LoadCategory.Heavy, Encumbrance.Effective(pip));
        Assert.Equal(armour - 2, pip.ArmorClass.Total);
        Assert.Contains("heavy load: max Dex +1", pip.ArmorClass.Explain().ToString());
        Assert.Equal(15, pip.CurrentSpeed);
    }

    [Fact]
    public void SomebodyDownCarriesNothingAndTheRestCarryTheirGear()
    {
        var run = LootLevel.Begin();
        run.Aldric().HitPoints.Take(run.Aldric().HitPoints.Current + 2);

        var load = Encumbrance.PartyLoad(run);

        // Only Pip's limits are left, and Aldric's scale mail and greatsword land on him.
        Assert.Equal(24.75m, load.Capacity.Light);
        Assert.Equal(46.5m, load.Weight);
        Assert.Equal(LoadCategory.Medium, load.Category);
        Assert.Equal([run.Aldric()], load.NotCarrying);
        Assert.Contains("Aldric is down and carrying nothing", load.Line);
    }

    [Fact]
    public void AnOverloadedPartyCannotWalkUntilSomethingIsPutDown()
    {
        var run = LootLevel.Begin();
        run.Bag.Add(Item("scale-mail"), 10);
        run.Bag.Add(Item("bloodstone"));

        Assert.True(run.IsOverloaded(run.Pip()));
        Assert.False(run.Walk(run.Pip(), new GridSquare(5, 2)));
        Assert.Contains("Nobody can walk", Encumbrance.PartyLoad(run).Line);

        Assert.True(run.Drop(run.InBag("scale-mail"), 10));
        Assert.True(run.Walk(run.Pip(), new GridSquare(5, 2)));
    }

    [Fact]
    public void TheSheetSaysWhoseLoadItIs()
    {
        var run = LootLevel.Begin();
        run.Bag.AddCoins(new Money(0, 0, 3000, 0));

        var who = CharacterSheet.Of(run.Aldric()).Single(section => section.Heading == "Who");

        Assert.Contains(who.Lines, line => line.StartsWith("Carrying 38 lb: light load") && line.EndsWith("the party's share makes it medium"));
    }
}

public class OutfitterTests
{
    [Fact]
    public void ABetterBladeForTheFighterIsAnUpgrade()
    {
        var run = Fought();
        var item = ContentFiles.Default.GetItem("greatsword-plus-one")!;

        var aldric = Outfitter.Compare(run, run.Aldric(), item);

        Assert.Equal(GearVerdict.Upgrade, aldric.Verdict);
        Assert.Equal("+1 to hit, +1 damage, gets past magic", aldric.Reason);
        Assert.Equal("greatsword", aldric.Current!.Id);
    }

    [Fact]
    public void AndADowngradeForTheRogueWhoNeverLearnedToSwingOne()
    {
        var run = Fought();
        var item = ContentFiles.Default.GetItem("greatsword-plus-one")!;

        var pip = Outfitter.Compare(run, run.Pip(), item);

        Assert.Equal(GearVerdict.Downgrade, pip.Verdict);
        Assert.Contains("not proficient: -4", pip.Reason);
    }

    [Fact]
    public void ArmourIsWeighedOnWhatItGivesThisCreature()
    {
        var run = Fought();
        var studded = ContentFiles.Default.GetItem("studded-leather")!;

        // A point better than Pip's leather, his Dexterity still under the cap; and armour the
        // wizard was never taught to wear is no upgrade however much it gives.
        Assert.Equal(GearVerdict.Upgrade, Outfitter.Compare(run, run.Pip(), studded).Verdict);
        Assert.Equal(GearVerdict.Downgrade, Outfitter.Compare(run, run.Party.Single(one => one.Name == "Sylwen"), studded).Verdict);
    }

    [Fact]
    public void SilverIsWorthHavingOnTheSameNumbers()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var valeria = run.Party.Single(one => one.Name == "Valeria");

        var silver = Outfitter.Compare(run, valeria, ContentFiles.Default.GetItem("silvered-longsword")!);

        Assert.Equal(GearVerdict.Upgrade, silver.Verdict);
        Assert.Equal("gets past silver", silver.Reason);
    }

    [Fact]
    public void TheAutopilotPutsTheSilverInSomebodysHand()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        SleepingPlacementTests.Win(run);
        run.Collect();
        run.TakeEverything();

        foreach (var member in run.Party)
        {
            Outfitter.EquipBest(run, member);
        }

        var valeria = run.Party.Single(one => one.Name == "Valeria");
        Assert.Equal("silvered longsword", valeria.PrimaryAttack!.Name);

        // And nobody traded a shield for a two-hander, or the other way about.
        Assert.True(valeria.Equipment.HasShield);
        Assert.Equal("greataxe", run.Party.Single(one => one.Name == "Karn").PrimaryAttack!.Name);
    }

    [Fact]
    public void EquipBestLeavesWellAloneWhenNothingIsBetter()
    {
        var run = Fought();
        run.Bag.Add(ContentFiles.Default.GetItem("chain-shirt")!);

        Assert.Empty(Outfitter.EquipBest(run, run.Party.Single(one => one.Name == "Sylwen")));
        Assert.True(run.Bag.Contains("chain-shirt"));
    }

    private static Campaign Fought()
    {
        var run = Campaign.Begin(ContentFiles.Default, "caves-of-shadow");
        return run;
    }
}

public class ShippedLootTests
{
    [Fact]
    public void TheCavesHaveTheirContainersWhereTheTextPutsThem()
    {
        var run = Campaign.Begin(ContentFiles.Default, "caves-of-shadow");

        Assert.Equal(ContainerLook.Cart, run.GetContainer("tobins-cart")!.Look);
        Assert.Contains(run.GetContainer("tobins-cart")!.Contents, entry => entry.Id == "greatsword-plus-one");
        Assert.Contains(run.GetContainer("storeroom-crates")!.Contents, entry => entry.Id == "light-crossbow");
        Assert.Equal(20m, run.GetContainer("storeroom-sack")!.Weight);
        Assert.True(run.GetContainer("gorrums-strongbox")!.IsLocked);

        // The loose stone is there, and nobody knows it yet.
        Assert.Null(run.GetContainer("junction-niche"));
        Assert.Equal(5, run.Level!.Features.Count(feature => feature.Kind == FeatureKind.Container));
    }

    [Fact]
    public void TheRoadHasACartAndTheGoblinsSack()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        Assert.Equal(ContainerLook.Cart, run.GetContainer("travellers-cart")!.Look);
        Assert.Equal(ContainerLook.Sack, run.GetContainer("goblin-sack")!.Look);
        Assert.False(run.GetContainer("travellers-cart")!.Money.IsEmpty);
    }

    [Fact]
    public void GorrumsHoardCanStillBeOpenedOnceTheLevelIsWon()
    {
        var run = Campaign.Begin(ContentFiles.Default, "caves-of-shadow");
        run.Engage("ogre-den");
        var gorrum = run.Battle.Foes.Single(foe => foe.Name == "Gorrum");
        SleepingPlacementTests.Win(run);
        run.Collect();

        Assert.Equal(CampaignState.Won, run.State);

        // His amber on his body, and the strongbox in the corner for whoever can open it.
        Assert.Contains(run.Containers.Single(container => container.Body == gorrum).Contents, entry => entry.Id == "gorrums-amber");

        var pip = run.Party.Single(one => one.Name == "Pip");
        Assert.True(run.Walk(pip, new GridSquare(2, 2)));
        var result = run.Open("gorrums-strongbox", pip);

        Assert.Contains("lock of Gorrum's strongbox", result.Lines[0]);
    }
}
