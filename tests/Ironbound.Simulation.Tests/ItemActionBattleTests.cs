using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Items;
using Ironbound.Rules.Maps;

namespace Ironbound.Simulation.Tests;

/// <summary>Potions and flasks used in a fight: what the log says, and what is left afterwards.</summary>
public class ItemActionBattleTests
{
    private const string Acid = "acid-flask";
    private const string Cure = "potion-of-cure-light-wounds";

    private static readonly ContentLibrary Library = TestLevel.Library();

    private static ItemDefinition Item(string id) => Library.GetItem(id)!;

    private static Creature Dummy(string name, int dexterity = 10, int hitPoints = 60, params string[] belt)
    {
        var creature = new Creature(name, new AbilityScores(10, dexterity, 10, 10, 10, 10), hitPoints, 1);
        creature.Attacks.Add(WeaponAttack.Melee("club", "1d6", DamageType.Bludgeoning));

        foreach (var id in belt)
        {
            Assert.True(Library.Equip(creature, Item(id), EquipmentSlot.Carried));
        }

        return creature;
    }

    private static Battlefield Field(params (Creature Creature, int X, int Y)[] placed)
    {
        var field = new Battlefield(12, 12);
        foreach (var (creature, x, y) in placed)
        {
            field.Place(creature, x, y);
        }

        return field;
    }

    [Fact]
    public void AThrowIsLoggedWithEverybodyItReached()
    {
        var thrower = Dummy("Thrower", dexterity: 14, belt: Acid);
        var friend = Dummy("Friend");
        var orc = Dummy("Orc");
        // Initiative; 15, with Dexterity's 2, less 2 for fifteen feet and 4 for throwing into the
        // melee Friend is in; 4 acid.
        var battle = new Battle(
            [thrower, friend], [orc], new SequenceRandom(20, 20, 1, 15, 4),
            battlefield: Field((thrower, 0, 0), (orc, 3, 0), (friend, 4, 0)));
        battle.BeginTurn();

        var lines = battle.Act(ThrowItemAction.At(Item(Acid), orc));

        Assert.Equal(
            [
                "Thrower throws the flask of acid at Orc: d20 [15] -4 = 11 vs touch flat-footed AC 10 — hit",
                "  Orc: 4 acid damage (56/60 hp)",
                "  Friend: 1 acid splash (59/60 hp)",
            ],
            lines);
        Assert.IsType<ThrowResult>(battle.LastResult);
    }

    [Fact]
    public void ADrinkIsLoggedWithTheSwingItDrewFirstAndThenWhatItDid()
    {
        var drinker = Dummy("Drinker", hitPoints: 30, belt: Cure);
        drinker.HitPoints.Take(20);
        var orc = Dummy("Orc");

        // Initiative; the orc's swing, a 2; the potion's d8, 5.
        var battle = new Battle(
            [drinker], [orc], new SequenceRandom(20, 1, 2, 5), battlefield: Field((drinker, 4, 4), (orc, 5, 4)));
        battle.BeginTurn();

        var lines = battle.Act(new DrinkPotionAction(Item(Cure)));

        Assert.Equal(3, lines.Count);
        Assert.Equal("Drinker drinks the potion of cure light wounds, provoking 1", lines[0]);
        Assert.StartsWith("  Orc", lines[1]);
        Assert.Equal("  Drinker: healed 6 (16/30 hp)", lines[2]);
    }

    [Fact]
    public void BreakingFreeAndPuttingOutTheFlamesShowTheirRolls()
    {
        var stuck = Dummy("Stuck");
        var burning = Dummy("Burning");
        stuck.Effects.Apply(ConditionInfo.Effect(Condition.Entangled, Duration.Rounds(3), ThrowItemAction.TangledName));
        stuck.Effects.Apply(ConditionInfo.Effect(Condition.Anchored, Duration.Rounds(3), ThrowItemAction.StuckName));
        burning.Effects.Apply(new DamageOverTimeEffect("Alchemist's fire", Duration.Rounds(1), "1d6", DamageType.Fire));

        // Initiative, where the goo's -4 Dexterity puts Stuck second; Burning's Reflex, 13 and 2
        // for rolling; Stuck's Strength check, 17.
        var orc = Dummy("Orc");
        var battle = new Battle(
            [stuck, burning], [orc], new SequenceRandom(20, 20, 1, 13, 17),
            battlefield: Field((stuck, 0, 0), (burning, 3, 3), (orc, 10, 10)));
        battle.BeginTurn();
        var doused = battle.Act(new PutOutFlamesAction());
        battle.EndTurn();
        battle.BeginTurn();
        var freed = battle.Act(new BreakFreeAction());

        Assert.Equal(["Stuck tears free of the goo", "  Stuck Strength: d20 [17] +0 = 17 vs DC 17 — success"], freed);
        Assert.Equal(
            ["Burning rolls on the ground and puts the flames out", "  Burning: Reflex save: d20 [13] +2 = 15 vs DC 15 — success"],
            doused);
    }

    [Fact]
    public void AThrownFlaskIsGoneOnceTheFightIsOverAndEverythingIsPickedUp()
    {
        var run = TestLevel.Begin(Library);
        run.Bag.Add(Item(Acid));
        var aldric = run.Aldric();
        Assert.True(run.Stow(aldric, run.InBag(Acid), 1));

        run.Engage("far");
        run.Field.Place(aldric, new GridSquare(8, 5));
        var sleeper = run.Battle.Foes.Single();

        var turns = 0;
        while (run.Battle.BeginTurn() is { } turn && !ReferenceEquals(turn.Actor, aldric) && turns++ < 10)
        {
            run.Battle.EndTurn();
        }

        Assert.NotEmpty(run.Battle.Act(ThrowItemAction.At(Item(Acid), sleeper)));
        Assert.Equal(0, Consumables.Count(aldric, Item(Acid)));

        TestLevel.Defeat(run.Battle.Foes);
        run.Collect();

        Assert.NotEqual(CampaignState.Fighting, run.State);
        Assert.Equal(0, Consumables.Count(aldric, Item(Acid)));
        Assert.Equal(0, aldric.Equipment.Recover());
        Assert.Equal(0, run.Bag.CountOf(Acid));
        Assert.DoesNotContain(run.Lying(), entry => entry.Id == Acid);
    }
}
