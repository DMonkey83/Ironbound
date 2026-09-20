using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Maps;

public class ThreatenedSquareTests
{
    private static Creature Someone(string name = "Someone") =>
        new(name, AbilityScores.All(10), 20, 2);

    [Fact]
    public void AManThreatensTheEightSquaresAroundHimAndNotHisOwn()
    {
        var field = new Battlefield(10, 5);
        var guard = Someone();
        field.Place(guard, 5, 2);

        var threatened = field.ThreatenedBy(guard).ToHashSet();

        Assert.Equal(8, threatened.Count);
        Assert.Contains(new GridSquare(4, 1), threatened);
        Assert.Contains(new GridSquare(6, 3), threatened);
        Assert.DoesNotContain(new GridSquare(5, 2), threatened);
        Assert.DoesNotContain(new GridSquare(7, 2), threatened);
    }

    [Fact]
    public void SomethingLargeThreatensTwoRings()
    {
        var field = new Battlefield(10, 5);
        var ogre = Someone("Ogre");
        ogre.Size = CreatureSize.Large;
        field.Place(ogre, 5, 2);

        var threatened = field.ThreatenedBy(ogre).ToHashSet();

        // Twenty, not twenty-four: the far corners of the second ring are two diagonals away,
        // which the alternating rule prices at fifteen feet — outside a ten-foot reach.
        Assert.Equal(20, threatened.Count);
        Assert.Contains(new GridSquare(7, 2), threatened);
        Assert.Contains(new GridSquare(6, 4), threatened);
        Assert.DoesNotContain(new GridSquare(7, 4), threatened);
        Assert.DoesNotContain(new GridSquare(5, 2), threatened);
    }

    [Fact]
    public void SomethingTinyThreatensNothingAtAll()
    {
        var field = new Battlefield(10, 5);
        var rat = Someone("Rat");
        rat.Size = CreatureSize.Tiny;
        field.Place(rat, 5, 2);

        Assert.Empty(field.ThreatenedBy(rat));
        Assert.False(field.Threatens(rat, new GridSquare(5, 3)));
    }

    [Fact]
    public void TheUnconsciousThreatenNothing()
    {
        var field = new Battlefield(10, 5);
        var guard = Someone();
        field.Place(guard, 5, 2);

        guard.HitPoints.Take(guard.HitPoints.Maximum + 1);

        Assert.Empty(field.ThreatenedBy(guard));
    }

    [Fact]
    public void SomeoneOffTheMapThreatensNothing()
    {
        Assert.Empty(new Battlefield(10, 5).ThreatenedBy(Someone()));
    }
}

public class FlankingTests
{
    private static Creature Fighter(string name, int allegiance)
    {
        var creature = new Creature(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6)
        {
            Allegiance = allegiance,
        };

        creature.Attacks.Add(WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing));
        return creature;
    }

    /// <summary>Target in the middle of a wide field, with room on every side.</summary>
    private static (Battlefield Field, Creature A, Creature B, Creature Target) Standoff()
    {
        var field = new Battlefield(12, 6);
        var a = Fighter("Valeria", 1);
        var b = Fighter("Karn", 1);
        var target = Fighter("Ogre", 2);

        field.Place(target, 5, 2);
        return (field, a, b, target);
    }

    [Fact]
    public void AlliesOnOppositeSidesAreFlanking()
    {
        var (field, a, b, target) = Standoff();
        field.Place(a, 4, 2);
        field.Place(b, 6, 2);

        Assert.True(field.AreFlanking(a, b, target));
        Assert.True(field.AreFlanking(b, a, target));
        Assert.Same(b, field.FindFlankingPartner(a, target));
    }

    [Fact]
    public void DiagonallyOppositeCountsToo()
    {
        var (field, a, b, target) = Standoff();
        field.Place(a, 4, 1);
        field.Place(b, 6, 3);

        Assert.True(field.AreFlanking(a, b, target));
    }

    [Fact]
    public void StandingBesideEachOtherIsNotFlanking()
    {
        var (field, a, b, target) = Standoff();
        field.Place(a, 4, 2);
        field.Place(b, 4, 1);

        Assert.False(field.AreFlanking(a, b, target));
        Assert.Null(field.FindFlankingPartner(a, target));
    }

    [Fact]
    public void AnEnemyOppositeYouIsNoHelp()
    {
        var (field, a, _, target) = Standoff();
        var stranger = Fighter("Stranger", 3);
        field.Place(a, 4, 2);
        field.Place(stranger, 6, 2);

        Assert.False(field.AreFlanking(a, stranger, target));
    }

    [Fact]
    public void ReachWeaponsFlankFromTwoSquaresOut()
    {
        var (field, a, b, target) = Standoff();
        a.Size = CreatureSize.Large;
        b.Size = CreatureSize.Large;
        field.Place(a, 3, 2);
        field.Place(b, 7, 2);

        Assert.True(field.AreFlanking(a, b, target));
    }

    [Fact]
    public void OutOfReachIsNotFlankingHoweverNeatlyLinedUp()
    {
        var (field, a, b, target) = Standoff();
        field.Place(a, 3, 2);
        field.Place(b, 7, 2);

        Assert.False(field.AreFlanking(a, b, target));
    }

    [Fact]
    public void FlankingIsWorthTwoOnTheAttackRoll()
    {
        var (field, a, b, target) = Standoff();
        var sword = a.PrimaryAttack!;

        field.Place(a, 4, 2);
        var alone = Strike.AttackBonus(a, sword, target, field).Total;

        field.Place(b, 6, 2);
        var flanked = Strike.AttackBonus(a, sword, target, field);

        Assert.Equal(alone + Strike.FlankingBonus, flanked.Total);
        Assert.Contains("Flanking with Karn", flanked.ToString());
    }

    [Fact]
    public void WithNoGroundThereIsNoFlanking()
    {
        var (field, a, b, target) = Standoff();
        field.Place(a, 4, 2);
        field.Place(b, 6, 2);

        var sword = a.PrimaryAttack!;

        Assert.Equal(
            Strike.AttackBonus(a, sword).Total,
            Strike.AttackBonus(a, sword, target, field: null).Total);
    }
}

public class OpportunityTests
{
    private static Creature Fighter(string name, int allegiance, int hitPoints = 40)
    {
        var creature = new Creature(name, new AbilityScores(18, 10, 14, 10, 10, 10), hitPoints, 6)
        {
            Allegiance = allegiance,
        };

        creature.Attacks.Add(WeaponAttack.Create("sword", 10, "1d6", DamageType.Slashing));
        return creature;
    }

    /// <summary>A guard at (5,2) with a mover placed wherever the test wants it, acting first.</summary>
    private static (Encounter Encounter, Battlefield Field, Creature Mover, Creature Guard) Watched(
        int moverX,
        int moverY,
        params int[] extraRolls)
    {
        var field = new Battlefield(10, 5);
        var mover = Fighter("Mover", 1);
        var guard = Fighter("Guard", 2);

        field.Place(mover, moverX, moverY);
        field.Place(guard, 5, 2);

        var encounter = new Encounter(
            [mover, guard], new SequenceRandom([20, 1, .. extraRolls]), rules: null, battlefield: field);

        return (encounter, field, mover, guard);
    }

    private static IReadOnlyList<GridSquare> Path(params (int X, int Y)[] squares) =>
        [.. squares.Select(s => new GridSquare(s.X, s.Y))];

    [Fact]
    public void LeavingAThreatenedSquareProvokes()
    {
        var (encounter, _, _, _) = Watched(4, 2, 15, 3);
        var turn = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(turn.Take(new MoveAction(Path((4, 2), (3, 2))))!);

        var opportunity = Assert.Single(moved.Opportunities);
        Assert.Equal("Guard", opportunity.Attacker.Name);
        Assert.Contains("provoking 1", moved.Description);
    }

    [Fact]
    public void WalkingIntoReachDoesNotProvoke()
    {
        var (encounter, _, _, _) = Watched(3, 2);
        var turn = encounter.BeginNextTurn()!;

        // (3,2) is ten feet out and safe; (4,2) is adjacent but only entered, never left.
        var moved = Assert.IsType<MoveActionResult>(turn.Take(new MoveAction(Path((3, 2), (4, 2))))!);

        Assert.Empty(moved.Opportunities);
    }

    [Fact]
    public void OneOpportunityARoundHoweverManySquaresYouLeave()
    {
        var (encounter, _, _, _) = Watched(4, 1, 15, 3);
        var turn = encounter.BeginNextTurn()!;

        // Both (4,1) and (4,2) are threatened, so this would be two provocations.
        var moved = Assert.IsType<MoveActionResult>(
            turn.Take(new MoveAction(Path((4, 1), (4, 2), (4, 3))))!);

        Assert.Single(moved.Opportunities);
    }

    [Fact]
    public void CombatReflexesBuysMoreOfThem()
    {
        var (encounter, _, _, guard) = Watched(4, 1, 15, 3, 16, 2);
        guard.BaseAttacksOfOpportunity = 3;
        var turn = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(
            turn.Take(new MoveAction(Path((4, 1), (4, 2), (4, 3))))!);

        Assert.Equal(2, moved.Opportunities.Count);
    }

    [Fact]
    public void TheAllotmentComesBackOnYourOwnTurn()
    {
        var (encounter, _, _, _) = Watched(4, 2, 15, 3, 1, 12, 4);
        encounter.BeginNextTurn()!.Take(new MoveAction(Path((4, 2), (3, 2))));

        encounter.BeginNextTurn();   // the guard's turn refreshes its allotment
        var back = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(
            back.Take(new MoveAction(Path((3, 2), (4, 2), (5, 1))))!);

        Assert.Single(moved.Opportunities);
    }

    [Fact]
    public void BeingCutDownMidMoveStopsYouWhereYouStood()
    {
        var field = new Battlefield(10, 5);

        // One hit die, no Constitution to speak of: a single hit puts it down.
        var frail = new Creature("Frail", AbilityScores.All(10), 1, 1) { Allegiance = 1 };
        var guard = Fighter("Guard", 2);
        field.Place(frail, 4, 2);
        field.Place(guard, 5, 2);

        var encounter = new Encounter(
            [frail, guard], new SequenceRandom(20, 1, 15, 6), rules: null, battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(
            turn.Take(new MoveAction(Path((4, 2), (3, 2), (2, 2))))!);

        Assert.False(frail.IsConscious);
        Assert.True(moved.WasInterrupted);
        Assert.Equal(new GridSquare(4, 2), field.SquareOf(frail));
        Assert.Contains("stopped short", moved.Description);
    }

    [Fact]
    public void AlliesDoNotTakeSwingsAtEachOther()
    {
        var field = new Battlefield(10, 5);
        var mover = Fighter("Mover", 1);
        var friend = Fighter("Friend", 1);
        field.Place(mover, 4, 2);
        field.Place(friend, 5, 2);

        var encounter = new Encounter(
            [mover, friend], new SequenceRandom(20, 1), rules: null, battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(turn.Take(new MoveAction(Path((4, 2), (3, 2))))!);

        Assert.Empty(moved.Opportunities);
    }

    [Fact]
    public void SomethingUnarmedTakesNoOpportunity()
    {
        var field = new Battlefield(10, 5);
        var mover = Fighter("Mover", 1);
        var bystander = new Creature("Bystander", AbilityScores.All(10), 20, 2) { Allegiance = 2 };
        field.Place(mover, 4, 2);
        field.Place(bystander, 5, 2);

        var encounter = new Encounter(
            [mover, bystander], new SequenceRandom(20, 1), rules: null, battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        var moved = Assert.IsType<MoveActionResult>(turn.Take(new MoveAction(Path((4, 2), (3, 2))))!);

        Assert.Empty(moved.Opportunities);
    }
}

public class CounterplayTests
{
    private static Creature Fighter(string name, int allegiance)
    {
        var creature = new Creature(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6)
        {
            Allegiance = allegiance,
        };

        creature.Attacks.Add(WeaponAttack.Create("sword", 10, "1d6", DamageType.Slashing));
        return creature;
    }

    private static (Encounter Encounter, Battlefield Field, Creature Mover, Creature Guard) Watched(
        params int[] extraRolls)
    {
        var field = new Battlefield(12, 6);
        var mover = Fighter("Mover", 1);
        var guard = Fighter("Guard", 2);

        field.Place(mover, 4, 2);
        field.Place(guard, 5, 2);

        var encounter = new Encounter(
            [mover, guard], new SequenceRandom([20, 1, .. extraRolls]), rules: null, battlefield: field);

        return (encounter, field, mover, guard);
    }

    [Fact]
    public void AFiveFootStepCostsNothingAndProvokesNothing()
    {
        var (encounter, field, mover, _) = Watched();
        var turn = encounter.BeginNextTurn()!;

        var stepped = Assert.IsType<MoveActionResult>(
            turn.Take(FiveFootStepAction.To(new GridSquare(4, 2), new GridSquare(4, 3)))!);

        Assert.Empty(stepped.Opportunities);
        Assert.Equal(new GridSquare(4, 3), field.SquareOf(mover));

        // It is free, so the whole turn is still ahead of it.
        Assert.True(turn.Budget.HasStandard);
        Assert.True(turn.Budget.HasMove);
    }

    [Fact]
    public void YouCannotStepAfterWalkingOrWalkAfterStepping()
    {
        var (encounter, _, _, _) = Watched(15, 3);
        var turn = encounter.BeginNextTurn()!;

        turn.Take(new MoveAction([new GridSquare(4, 2), new GridSquare(3, 2)]));

        Assert.Null(turn.Take(FiveFootStepAction.To(new GridSquare(3, 2), new GridSquare(3, 3))));

        var (second, _, _, _) = Watched();
        var other = second.BeginNextTurn()!;
        other.Take(FiveFootStepAction.To(new GridSquare(4, 2), new GridSquare(4, 3)));

        Assert.Null(other.Take(new MoveAction([new GridSquare(4, 3), new GridSquare(3, 3)])));
    }

    [Fact]
    public void YouCannotStepIntoDifficultGround()
    {
        var (encounter, field, _, _) = Watched();
        field.MakeDifficult(new GridSquare(4, 3));
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(FiveFootStepAction.To(new GridSquare(4, 2), new GridSquare(4, 3))));
    }

    [Fact]
    public void AStepIsOnlyEverOneSquare()
    {
        var (encounter, _, _, _) = Watched();
        var turn = encounter.BeginNextTurn()!;

        var tooFar = new FiveFootStepAction(
            [new GridSquare(4, 2), new GridSquare(3, 2), new GridSquare(2, 2)]);

        Assert.Null(turn.Take(tooFar));
    }

    [Fact]
    public void WithdrawingMakesTheSquareYouLeaveSafeAndNoOthers()
    {
        var field = new Battlefield(12, 6);
        var mover = Fighter("Mover", 1);
        var guard = Fighter("Guard", 2);
        var watcher = Fighter("Watcher", 2);

        field.Place(mover, 4, 2);
        field.Place(guard, 5, 2);
        field.Place(watcher, 2, 1);

        var encounter = new Encounter(
            [mover, guard, watcher],
            new SequenceRandom(20, 2, 1, 14, 4),
            rules: null,
            battlefield: field);

        var turn = encounter.BeginNextTurn()!;
        var withdrawn = Assert.IsType<MoveActionResult>(turn.Take(new WithdrawAction(
            [new GridSquare(4, 2), new GridSquare(3, 2), new GridSquare(2, 2)]))!);

        // The guard, whose reach the mover started in, gets nothing. The watcher, whose reach it
        // crosses on the way out, does.
        var taken = Assert.Single(withdrawn.Opportunities);
        Assert.Equal("Watcher", taken.Attacker.Name);
    }

    [Fact]
    public void WithdrawingCoversTwiceTheGround()
    {
        var (encounter, _, mover, _) = Watched(15, 3);
        var turn = encounter.BeginNextTurn()!;

        // Eight squares is forty feet, past a speed of thirty but inside a withdraw.
        var straightLine = Enumerable.Range(0, 9).Select(i => new GridSquare(i, 5)).ToArray();
        encounter.Battlefield!.Place(mover, 0, 5);
        Assert.Null(turn.Take(new MoveAction(straightLine)));
        Assert.NotNull(turn.Take(new WithdrawAction(straightLine)));
        Assert.Equal(new GridSquare(8, 5), encounter.Battlefield.SquareOf(mover));
    }

    [Fact]
    public void WithdrawingTakesTheWholeRound()
    {
        var (encounter, _, _, _) = Watched();
        var turn = encounter.BeginNextTurn()!;

        turn.Take(new WithdrawAction([new GridSquare(4, 2), new GridSquare(3, 2)]));

        Assert.False(turn.Budget.HasStandard);
        Assert.False(turn.Budget.HasMove);
    }
}
