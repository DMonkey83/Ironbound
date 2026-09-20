using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Maps;

public class MovementTests
{
    private static Creature Fighter(string name) =>
        new(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);

    private static WeaponAttack Sword() =>
        WeaponAttack.Create("sword", 10, "1d6", DamageType.Slashing);

    /// <summary>A corridor twenty squares long. The hero rolls 20 for initiative and acts first.</summary>
    private static (Encounter Encounter, Battlefield Field, Creature Hero, Creature Goblin) Corridor(
        int goblinX = 10,
        params int[] extraRolls)
    {
        var field = new Battlefield(20, 5);
        var hero = Fighter("Hero");
        var goblin = Fighter("Goblin");

        field.Place(hero, 0, 0);
        field.Place(goblin, goblinX, 0);

        var encounter = new Encounter(
            [hero, goblin],
            new SequenceRandom([20, 1, .. extraRolls]),
            rules: null,
            battlefield: field);

        return (encounter, field, hero, goblin);
    }

    private static IReadOnlyList<GridSquare> Line(int fromX, int toX) =>
        [.. Enumerable.Range(fromX, toX - fromX + 1).Select(x => new GridSquare(x, 0))];

    // ---- reach ----

    [Fact]
    public void YouCannotSwingAtSomethingTenSquaresAway()
    {
        var (encounter, _, _, goblin) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(new AttackAction(Sword(), goblin)));
        Assert.True(turn.Budget.HasStandard);
    }

    [Fact]
    public void AdjacentIsCloseEnough()
    {
        var (encounter, _, _, goblin) = Corridor(goblinX: 1, extraRolls: [15, 4]);
        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new AttackAction(Sword(), goblin)));
    }

    [Fact]
    public void TenFeetIsCloseEnoughForSomethingLarge()
    {
        var (encounter, _, hero, goblin) = Corridor(goblinX: 2, extraRolls: [15, 4]);
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(new AttackAction(Sword(), goblin)));

        hero.Size = CreatureSize.Large;

        Assert.NotNull(turn.Take(new AttackAction(Sword(), goblin)));
    }

    [Fact]
    public void WithoutAMapNothingIsEverOutOfReach()
    {
        var hero = Fighter("Hero");
        var goblin = Fighter("Goblin");
        var encounter = new Encounter([hero, goblin], new SequenceRandom(20, 1, 15, 4));

        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new AttackAction(Sword(), goblin)));
    }

    // ---- moving ----

    [Fact]
    public void AMoveSpendsGroundAndTheMoveAction()
    {
        var (encounter, field, hero, _) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        var result = turn.Take(new MoveAction(Line(0, 4)));

        Assert.NotNull(result);
        Assert.Equal(new GridSquare(4, 0), field.SquareOf(hero));
        Assert.False(turn.Budget.HasMove);
        Assert.True(turn.Budget.HasStandard);
        Assert.Contains("20 ft of 30", result.Description);
    }

    [Fact]
    public void YouCannotWalkFurtherThanYourSpeed()
    {
        var (encounter, field, hero, _) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        // Seven squares is 35 feet against a speed of 30.
        Assert.Null(turn.Take(new MoveAction(Line(0, 7))));

        Assert.Equal(new GridSquare(0, 0), field.SquareOf(hero));
        Assert.True(turn.Budget.HasMove);
    }

    [Fact]
    public void HasteMakesTheSameWalkPossible()
    {
        var (encounter, field, hero, _) = Corridor();
        var turn = encounter.BeginNextTurn()!;
        Assert.Null(turn.Take(new MoveAction(Line(0, 7))));

        hero.SpeedModifiers.Add(30, BonusType.Enhancement, "Haste");

        Assert.Equal(60, hero.CurrentSpeed);
        Assert.NotNull(turn.Take(new MoveAction(Line(0, 7))));
        Assert.Equal(new GridSquare(7, 0), field.SquareOf(hero));
    }

    [Fact]
    public void TwoMovesAreAllowedBecauseTheStandardPaysForTheSecond()
    {
        var (encounter, field, hero, _) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new MoveAction(Line(0, 4))));
        Assert.NotNull(turn.Take(new MoveAction(Line(4, 8))));

        Assert.Equal(new GridSquare(8, 0), field.SquareOf(hero));
        Assert.False(turn.Budget.HasMove);
        Assert.False(turn.Budget.HasStandard);

        // The swift action survives: it was never what paid for either move.
        Assert.True(turn.Budget.HasSwift);
    }

    [Fact]
    public void APathHasToStartWhereYouAreAndJoinUp()
    {
        var (encounter, _, _, _) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        // Starts somewhere else.
        Assert.Null(turn.Take(new MoveAction(Line(3, 5))));

        // Teleports across a gap.
        Assert.Null(turn.Take(new MoveAction([new GridSquare(0, 0), new GridSquare(4, 0)])));

        Assert.True(turn.Budget.HasMove);
    }

    [Fact]
    public void YouCannotFinishAMoveOnSomeoneElse()
    {
        var (encounter, _, _, _) = Corridor(goblinX: 4);
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(new MoveAction(Line(0, 4))));
    }

    [Fact]
    public void ClosingTheDistanceTakesTwoRoundsOfWalking()
    {
        var (encounter, field, hero, goblin) = Corridor();
        var turn = encounter.BeginNextTurn()!;

        var approach = MoveAction.Towards(field, hero, goblin);
        Assert.NotNull(approach);
        turn.Take(approach);

        // Thirty feet of a fifty-foot gap: still short of the goblin.
        Assert.Equal(new GridSquare(6, 0), field.SquareOf(hero));
        Assert.False(field.IsWithinReach(hero, goblin));
    }

    [Fact]
    public void ThereIsNowhereToGoWhenAlreadyInReach()
    {
        var (_, field, hero, goblin) = Corridor(goblinX: 1);

        Assert.Null(MoveAction.Towards(field, hero, goblin));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new MoveAction(null!));
    }

}
