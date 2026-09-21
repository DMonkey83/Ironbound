using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Maps;

public class ProneMovementTests
{
    [Fact]
    public void SomebodyOnTheFloorCannotWalkAnywhere()
    {
        var (encounter, _, mover, _) = Floored(guardAdjacent: false);
        var turn = encounter.BeginNextTurn()!;

        // Thirty feet of speed, and none of it is any use from down there.
        Assert.Null(turn.Take(new MoveAction(
            [new GridSquare(4, 2), new GridSquare(4, 3), new GridSquare(4, 4)])));
        Assert.True(mover.IsProne);
    }

    [Fact]
    public void ButTheyCanCrawlOneSquareForTheirMoveAction()
    {
        var (encounter, field, mover, _) = Floored(guardAdjacent: false);
        var turn = encounter.BeginNextTurn()!;

        var crawled = Assert.IsType<MoveActionResult>(
            turn.Take(new MoveAction([new GridSquare(4, 2), new GridSquare(4, 3)]))!);

        Assert.Equal(new GridSquare(4, 3), field.SquareOf(mover));
        Assert.Contains("crawls", crawled.Description);
        Assert.False(turn.Budget.HasMove);

        // Crawling is not getting up.
        Assert.True(mover.IsProne);
    }

    [Fact]
    public void CrawlingAwayFromSomebodyProvokes()
    {
        // The guard's attack roll, then the damage die it earns by hitting somebody on the floor.
        var (encounter, _, _, _) = Floored(guardAdjacent: true, 15, 4);
        var turn = encounter.BeginNextTurn()!;

        var crawled = Assert.IsType<MoveActionResult>(
            turn.Take(new MoveAction([new GridSquare(4, 2), new GridSquare(3, 2)]))!);

        Assert.Single(crawled.Opportunities);
    }

    [Fact]
    public void ThereIsNoCarefulStepFromFlatOnYourBack()
    {
        var (encounter, _, _, _) = Floored(guardAdjacent: true);
        var turn = encounter.BeginNextTurn()!;

        // Otherwise the free step that provokes nothing is simply a better crawl.
        Assert.Null(turn.Take(FiveFootStepAction.To(new GridSquare(4, 2), new GridSquare(3, 2))));
    }

    [Fact]
    public void OnceStoodUpTheyMoveAsTheyAlwaysDid()
    {
        var (encounter, field, mover, _) = Floored(guardAdjacent: false);
        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new StandUpAction()));
        Assert.False(mover.IsProne);

        // Standing cost the move action, so the walk waits for next turn — but the step is back.
        Assert.NotNull(turn.Take(FiveFootStepAction.To(new GridSquare(4, 2), new GridSquare(4, 3))));
        Assert.Equal(new GridSquare(4, 3), field.SquareOf(mover));
    }

    private static (Encounter Encounter, Battlefield Field, Creature Mover, Creature Guard) Floored(
        bool guardAdjacent, params int[] extraRolls)
    {
        var field = new Battlefield(12, 6);
        var mover = Fighter("Mover", 1);
        var guard = Fighter("Guard", 2);

        field.Place(mover, 4, 2);
        field.Place(guard, guardAdjacent ? 5 : 10, 2);

        mover.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        var encounter = new Encounter(
            [mover, guard], new SequenceRandom([20, 1, .. extraRolls]), rules: null, battlefield: field);

        return (encounter, field, mover, guard);
    }

    private static Creature Fighter(string name, int allegiance)
    {
        var creature = new Creature(name, new AbilityScores(14, 12, 12, 10, 10, 10), 30, 3)
        {
            Allegiance = allegiance,
            BaseAttackBonus = 3,
        };

        creature.Attacks.Add(WeaponAttack.Create("sword", 10, "1d6", DamageType.Slashing));
        return creature;
    }
}
