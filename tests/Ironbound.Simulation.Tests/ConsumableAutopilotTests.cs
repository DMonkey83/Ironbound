using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

/// <summary>What the autopilot does with what hangs on a belt: potions, flasks, glue and stones.</summary>
public class ConsumableAutopilotTests
{
    private const string Cure = "potion-of-cure-light-wounds";
    private const string Fire = "alchemists-fire";
    private const string Tanglefoot = "tanglefoot-bag";
    private const string Thunderstone = "thunderstone";

    private static Creature Build(string id, string? name = null) => ContentFiles.Default.BuildCreature(id, name: name)!;

    private static ItemDefinition Item(string id) => ContentFiles.Default.GetItem(id)!;

    private static Creature Hang(Creature creature, params string[] items)
    {
        foreach (var id in items)
        {
            Assert.True(ContentFiles.Default.Equip(creature, Item(id), EquipmentSlot.Carried));
        }

        return creature;
    }

    /// <summary>Down to so many hit points.</summary>
    private static Creature Hurt(Creature creature, int left)
    {
        creature.HitPoints.Take(creature.HitPoints.Current - left);
        return creature;
    }

    private static (Battle Battle, HeuristicActionSource Source) Fight(
        IReadOnlyList<(Creature Creature, int X, int Y)> party,
        IReadOnlyList<(Creature Creature, int X, int Y)> foes)
    {
        var field = new Battlefield(30, 30);
        foreach (var (creature, x, y) in party.Concat(foes))
        {
            field.Place(creature, x, y);
        }

        var battle = new Battle(
            party.Select(one => one.Creature), foes.Select(one => one.Creature), new SequenceRandom(true, 10), battlefield: field);

        return (battle, new HeuristicActionSource(battle, new SequenceRandom(true, 1)));
    }

    private static Turn TurnOf(Battle battle, Creature creature)
    {
        for (var tries = 0; tries < 20; tries++)
        {
            var turn = battle.Encounter.BeginNextTurn()!;
            if (ReferenceEquals(turn.Actor, creature))
            {
                return turn;
            }
        }

        throw new InvalidOperationException($"{creature.Name} never had a turn.");
    }

    // ---- H1: a potion for a friend bleeding out ----

    [Fact]
    public void AFriendBleedingOutNextDoorIsGivenThePotionRatherThanABandage()
    {
        var aldric = Hang(Build("aldric"), Cure);
        var hale = Hurt(Build("hale"), -3);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5), (hale, 6, 5)], [(orc, 20, 5)]);

        var action = Assert.IsType<AdministerPotionAction>(source.NextAction(TurnOf(battle, aldric)));

        Assert.Same(hale, action.Patient);
        Assert.Equal(Cure, action.Item.Id);
    }

    [Fact]
    public void WithoutAPotionItIsTheBandageAsBefore()
    {
        var aldric = Build("aldric");
        var hale = Hurt(Build("hale"), -3);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5), (hale, 6, 5)], [(orc, 20, 5)]);

        Assert.IsType<StabiliseAction>(source.NextAction(TurnOf(battle, aldric)));
    }

    // ---- H2: drinking when badly hurt ----

    [Fact]
    public void BadlyHurtAndOutOfReachItDrinks()
    {
        var aldric = Hurt(Hang(Build("aldric"), Cure), 2);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 10, 5)]);

        var drink = Assert.IsType<DrinkPotionAction>(source.NextAction(TurnOf(battle, aldric)));

        Assert.Equal(Cure, drink.Item.Id);
    }

    [Fact]
    public void WithABladeAtItsThroatItStepsClearFirstAndThenDrinks()
    {
        var aldric = Hurt(Hang(Build("aldric"), Cure), 2);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 6, 5)]);
        var turn = TurnOf(battle, aldric);

        var step = Assert.IsType<FiveFootStepAction>(source.NextAction(turn));
        Assert.False(battle.Battlefield!.Threatens(orc, step.Destination!.Value));

        battle.Act(step);

        Assert.IsType<DrinkPotionAction>(source.NextAction(battle.Encounter.Current!));
    }

    [Fact]
    public void WithNowhereToStepItCoversUpAsBefore()
    {
        var aldric = Hurt(Hang(Build("aldric"), Cure), 2);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 6, 5)]);
        var turn = TurnOf(battle, aldric);
        battle.Act(FiveFootStepAction.To(new GridSquare(5, 5), new GridSquare(5, 6)));

        // The free step is spent, and the orc still reaches the square it led to.
        Assert.IsType<TotalDefenseAction>(source.NextAction(turn));
    }

    [Fact]
    public void AClericCloseByWithHealingLeftIsLeftToDoIt()
    {
        var aldric = Hurt(Hang(Build("aldric"), Cure), 2);
        var hale = Build("hale");
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5), (hale, 5, 9)], [(orc, 10, 5)]);

        Assert.IsType<TotalDefenseAction>(source.NextAction(TurnOf(battle, aldric)));
    }

    [Fact]
    public void AnEnemyWithAPotionDrinksIt()
    {
        var grask = Build("hobgoblin-sergeant", "Sergeant Grask");
        Hurt(grask, grask.HitPoints.Maximum / 5);
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 2, 5)], [(grask, 12, 5)]);

        var drink = Assert.IsType<DrinkPotionAction>(source.NextAction(TurnOf(battle, grask)));

        Assert.Equal(Cure, drink.Item.Id);
    }

    // ---- H3: stuck fast ----

    [Fact]
    public void StuckFastWithNobodyInReachItTearsFree()
    {
        var orc = Build("orc");
        orc.Effects.Apply(ConditionInfo.Effect(Condition.Anchored, Duration.Rounds(4), ThrowItemAction.StuckName));
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 2, 5)], [(orc, 12, 5)]);

        Assert.IsType<BreakFreeAction>(source.NextAction(TurnOf(battle, orc)));
    }

    [Fact]
    public void StuckFastWithSomebodyInReachItStillSwings()
    {
        var orc = Build("orc");
        orc.Effects.Apply(ConditionInfo.Effect(Condition.Anchored, Duration.Rounds(4), ThrowItemAction.StuckName));
        var valeria = Build("valeria");
        var (battle, source) = Fight([(valeria, 5, 5)], [(orc, 6, 5)]);

        var action = source.NextAction(TurnOf(battle, orc));

        Assert.True(action is FullAttackAction or AttackAction, $"{action?.GetType().Name}");
    }

    // ---- H3b: on fire ----

    [Fact]
    public void BurningLowEnoughToDropItRollsTheFlamesOut()
    {
        var aldric = Hurt(Build("aldric"), 4);
        aldric.Effects.Apply(new DamageOverTimeEffect("Alchemist's fire", Duration.Rounds(1), "1d6", DamageType.Fire));
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 12, 5)]);

        Assert.IsType<PutOutFlamesAction>(source.NextAction(TurnOf(battle, aldric)));
    }

    [Fact]
    public void BurningWithHitPointsToSpareItGetsOnWithTheFight()
    {
        var aldric = Build("aldric");
        aldric.Effects.Apply(new DamageOverTimeEffect("Alchemist's fire", Duration.Rounds(1), "1d6", DamageType.Fire));
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 12, 5)]);

        Assert.IsNotType<PutOutFlamesAction>(source.NextAction(TurnOf(battle, aldric)));
    }

    // ---- H4: flasks ----

    [Fact]
    public void AFlaskBeatsAWalkAndAPoorSwing()
    {
        var pip = Hang(Build("pip"), Fire);
        var orc = Build("orc");
        var (battle, source) = Fight([(pip, 5, 5)], [(orc, 9, 5)]);

        var thrown = Assert.IsType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));

        Assert.Equal(Fire, thrown.Item.Id);
        Assert.Same(orc, thrown.Target);
    }

    [Fact]
    public void NotWhenTheSplashWouldCatchAFriendBleedingOut()
    {
        var pip = Hang(Build("pip"), Fire);
        var hale = Hurt(Build("hale"), -3);
        var orc = Build("orc");
        var (battle, source) = Fight([(pip, 5, 5), (hale, 10, 5)], [(orc, 9, 5)]);

        Assert.IsNotType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));
    }

    [Fact]
    public void NorWhenItsOwnBladeWouldDoMore()
    {
        var aldric = Hang(Build("aldric"), Fire);
        var orc = Build("orc");
        var (battle, source) = Fight([(aldric, 5, 5)], [(orc, 7, 5)]);
        var turn = TurnOf(battle, aldric);

        // A step in and a full swing of a greatsword, against a flask's d6 and its burn.
        Assert.IsNotType<ThrowItemAction>(source.NextAction(turn));
    }

    [Fact]
    public void NorWithSomebodyCloseEnoughToPunishTheThrow()
    {
        var pip = Hang(Build("pip"), Fire);
        var orc = Build("orc");
        var (battle, source) = Fight([(pip, 5, 5)], [(orc, 6, 5)]);

        Assert.IsNotType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));
    }

    // ---- H5: tanglefoot ----

    [Fact]
    public void TheBagGoesAtTheStrongestEnemyInRange()
    {
        var pip = Hang(Build("pip"), Tanglefoot);
        var orc = Build("orc");
        var ogre = Build("ogre", "Gorrum");
        var (battle, source) = Fight([(pip, 5, 5)], [(orc, 8, 5), (ogre, 8, 8)]);

        var thrown = Assert.IsType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));

        Assert.Equal(Tanglefoot, thrown.Item.Id);
        Assert.Same(ogre, thrown.Target);
    }

    [Fact]
    public void OnlyOneBagASideAFight()
    {
        var pip = Hang(Build("pip"), Tanglefoot);
        var other = Hang(Build("pip", "Pip's Cousin"), Tanglefoot);
        var orc = Build("orc");
        var ogre = Build("ogre", "Gorrum");
        var (battle, source) = Fight([(pip, 5, 5), (other, 5, 9)], [(orc, 8, 5), (ogre, 8, 8)]);

        Assert.IsType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));
        Assert.IsNotType<ThrowItemAction>(source.NextAction(TurnOf(battle, other)));
    }

    [Fact]
    public void NeverAtAnythingAlreadyCaught()
    {
        var pip = Hang(Build("pip"), Tanglefoot);
        var ogre = Build("ogre", "Gorrum");
        ogre.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(3), ThrowItemAction.TangledName));
        var (battle, source) = Fight([(pip, 5, 5)], [(ogre, 8, 8)]);

        Assert.IsNotType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));
    }

    // ---- H6: thunderstones ----

    [Fact]
    public void AThunderstoneGoesOffAmongTheirCasters()
    {
        var pip = Hang(Build("pip"), Thunderstone);
        var wizard = Build("sylwen", "Hostile Wizard");
        var (battle, source) = Fight([(pip, 5, 5)], [(wizard, 10, 5)]);

        var thrown = Assert.IsType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));

        Assert.Equal(Thunderstone, thrown.Item.Id);
        Assert.Equal(new GridSquare(10, 5), thrown.Square);
    }

    [Fact]
    public void ButNotWhereItWouldDeafenAFriend()
    {
        var pip = Hang(Build("pip"), Thunderstone);
        var aldric = Build("aldric");
        var wizard = Build("sylwen", "Hostile Wizard");
        var (battle, source) = Fight([(pip, 5, 5), (aldric, 12, 5)], [(wizard, 10, 5)]);

        Assert.IsNotType<ThrowItemAction>(source.NextAction(TurnOf(battle, pip)));
    }
}

/// <summary>What the outfitter hangs on belts between fights, and what it drinks.</summary>
public class OutfitterBeltTests
{
    private const string Cure = "potion-of-cure-light-wounds";
    private const string Fire = "alchemists-fire";

    private static ItemDefinition Item(string id) => ChainContent.Library.GetItem(id)!;

    /// <summary>The Caves party, the first fight won, and so many of each thing in the bag.</summary>
    private static Campaign Between(params (string Id, int Count)[] bagged)
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        SleepingPlacementTests.Win(run);
        Assert.Equal(CampaignState.Between, run.State);

        foreach (var (id, count) in bagged)
        {
            run.Bag.Add(Item(id), count);
        }

        return run;
    }

    private static Creature Member(Campaign run, string name) => run.Party.Single(one => one.Name == name);

    private static int OnBelt(Creature creature, string id) => Consumables.Count(creature, Item(id));

    [Fact]
    public void OneHealingPotionEachAndTheClericLast()
    {
        var run = Between((Cure, 3));

        foreach (var one in run.Party)
        {
            Outfitter.HangConsumables(run, one);
        }

        Assert.Equal(1, OnBelt(Member(run, "Aldric"), Cure));
        Assert.Equal(1, OnBelt(Member(run, "Pip"), Cure));
        Assert.Equal(1, OnBelt(Member(run, "Sylwen"), Cure));
        Assert.Equal(0, OnBelt(Member(run, "Hale"), Cure));
        Assert.Equal(0, run.Bag.CountOf(Cure));
    }

    [Fact]
    public void ItComesOutTheSameWhicheverOrderThePartyIsAskedIn()
    {
        var forward = Between((Cure, 2), (Fire, 2));
        var backward = Between((Cure, 2), (Fire, 2));

        foreach (var one in forward.Party)
        {
            Outfitter.HangConsumables(forward, one);
        }

        foreach (var one in backward.Party.Reverse())
        {
            Outfitter.HangConsumables(backward, one);
        }

        Assert.Equal(
            forward.Party.Select(one => string.Join(", ", Consumables.OnBelt(one))),
            backward.Party.Select(one => string.Join(", ", Consumables.OnBelt(one))));
    }

    [Fact]
    public void FlasksGoToThoseWhoseOwnWeaponIsWeak()
    {
        var run = Between((Fire, 2));

        foreach (var one in run.Party)
        {
            Outfitter.HangConsumables(run, one);
        }

        Assert.False(Outfitter.IsWeakInMelee(Member(run, "Aldric")));
        Assert.Equal(0, OnBelt(Member(run, "Aldric"), Fire));
        Assert.Equal(1, OnBelt(Member(run, "Pip"), Fire));
        Assert.Equal(1, OnBelt(Member(run, "Sylwen"), Fire));
        Assert.Equal(0, run.Bag.CountOf(Fire));
    }

    [Fact]
    public void NothingIsHungInTheMiddleOfAFight()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        run.Bag.Add(Item(Cure), 4);

        Assert.Empty(Outfitter.HangConsumables(run, run.Party[0]));
        Assert.Equal(4, run.Bag.CountOf(Cure));
    }

    [Fact]
    public void WhileARestRemainsNoPotionIsDrunk()
    {
        var run = Between((Cure, 1));
        var aldric = Member(run, "Aldric");
        aldric.HitPoints.Take(aldric.HitPoints.Maximum - 1);

        Assert.Empty(Outfitter.PatchUp(run));
        Assert.Equal(1, aldric.HitPoints.Current);
        Assert.Equal(1, run.Bag.CountOf(Cure));
    }

    [Fact]
    public void WithNoRestLeftTheHurtDrinkAndTheBeltIsFilledAgain()
    {
        var run = Between((Cure, 2));
        var aldric = Member(run, "Aldric");
        Outfitter.HangConsumables(run, aldric);
        Assert.True(run.Rest());
        Assert.False(run.CanRest);

        // Just under half: one draught, at least two points, puts him back over it.
        aldric.HitPoints.Take(aldric.HitPoints.Maximum - ((aldric.HitPoints.Maximum - 1) / 2));
        var before = aldric.HitPoints.Current;

        var done = Outfitter.PatchUp(run);

        Assert.Contains(done, line => line.StartsWith("Aldric drinks the potion of cure light wounds", StringComparison.Ordinal));
        Assert.True(aldric.HitPoints.Current > before);
        Assert.True(aldric.HitPoints.Current * 2 >= aldric.HitPoints.Maximum);
        Assert.Equal(1, OnBelt(aldric, Cure));
        Assert.Equal(0, run.Bag.CountOf(Cure));
    }

    [Fact]
    public void AFriendsDraughtIsPassedOverWhenTheBagIsEmpty()
    {
        var run = Between((Cure, 1));
        var aldric = Member(run, "Aldric");
        var pip = Member(run, "Pip");
        Outfitter.HangConsumables(run, aldric);
        Assert.True(run.Rest());

        pip.HitPoints.Take(pip.HitPoints.Maximum - 1);

        Outfitter.PatchUp(run);

        Assert.True(pip.HitPoints.Current > 1);
        Assert.Equal(0, OnBelt(aldric, Cure));
    }
}
