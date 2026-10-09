using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Maps;

public class DistanceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 20)]
    [InlineData(4, 30)]
    [InlineData(5, 35)]
    [InlineData(6, 45)]
    public void DiagonalsAlternateFiveAndTen(int diagonals, int feet)
    {
        Assert.Equal(feet, Distance.DiagonalCost(diagonals));
    }

    [Theory]
    [InlineData(0, 0, 4, 0, 20)]   // four squares in a line
    [InlineData(0, 0, 0, 3, 15)]
    [InlineData(0, 0, 1, 1, 5)]    // one diagonal is free-ish
    [InlineData(0, 0, 2, 2, 15)]   // the second one costs double
    [InlineData(0, 0, 3, 3, 20)]
    [InlineData(0, 0, 4, 4, 30)]   // not 28.3, and not 20
    [InlineData(0, 0, 5, 3, 30)]   // three diagonals then two straights
    [InlineData(2, 2, 2, 2, 0)]
    public void MeasuresTheWayTheRulesMeasure(int ax, int ay, int bx, int by, int feet)
    {
        var a = new GridSquare(ax, ay);
        var b = new GridSquare(bx, by);

        Assert.Equal(feet, Distance.Between(a, b));
        Assert.Equal(feet, Distance.Between(b, a));
    }

    [Fact]
    public void AdjacencyIncludesTheCorners()
    {
        var centre = new GridSquare(3, 3);

        Assert.True(Distance.AreAdjacent(centre, new GridSquare(4, 4)));
        Assert.True(Distance.AreAdjacent(centre, new GridSquare(3, 2)));
        Assert.False(Distance.AreAdjacent(centre, centre));
        Assert.False(Distance.AreAdjacent(centre, new GridSquare(5, 3)));
    }

    [Fact]
    public void StepsCountADiagonalAsOne()
    {
        Assert.Equal(4, Distance.Steps(new GridSquare(0, 0), new GridSquare(4, 4)));
        Assert.Equal(4, Distance.Steps(new GridSquare(0, 0), new GridSquare(4, 1)));
    }
}

public class PositionTests
{
    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(12.4f, 7.9f, 2, 1)]
    [InlineData(4.999f, 0.001f, 0, 0)]
    [InlineData(-1f, -1f, -1, -1)]
    [InlineData(-5f, -5f, -1, -1)]
    public void APositionKnowsWhichSquareItIsIn(float x, float y, int squareX, int squareY)
    {
        Assert.Equal(new GridSquare(squareX, squareY), new Position(x, y).Square);
    }

    [Theory]
    [InlineData(10.01f, 5.01f)]
    [InlineData(12.5f, 7.5f)]
    [InlineData(14.99f, 9.99f)]
    public void EverywhereInsideASquareIsTheSameSquare(float x, float y)
    {
        // Sub-square precision never reaches a rule, so floating point can never move a replay.
        Assert.Equal(new GridSquare(2, 1), new Position(x, y).Square);
    }

    [Fact]
    public void ASquareRoundTripsThroughItsCentre()
    {
        var square = new GridSquare(3, 4);

        Assert.Equal(new Position(17.5f, 22.5f), square.Centre);
        Assert.Equal(square, square.Centre.Square);
        Assert.Equal(square, Position.Of(3, 4).Square);
    }
}

public class CreatureSizeTests
{
    [Theory]
    [InlineData(CreatureSize.Fine, 8, 0)]
    [InlineData(CreatureSize.Diminutive, 4, 0)]
    [InlineData(CreatureSize.Tiny, 2, 0)]
    [InlineData(CreatureSize.Small, 1, 5)]
    [InlineData(CreatureSize.Medium, 0, 5)]
    [InlineData(CreatureSize.Large, -1, 10)]
    [InlineData(CreatureSize.Huge, -2, 15)]
    [InlineData(CreatureSize.Gargantuan, -4, 20)]
    [InlineData(CreatureSize.Colossal, -8, 30)]
    public void TheTableIsTheTable(CreatureSize size, int modifier, int reach)
    {
        Assert.Equal(modifier, CreatureSizes.Modifier(size));
        Assert.Equal(reach, CreatureSizes.Reach(size));
    }

    [Fact]
    public void TinyThingsCannotReachOutOfTheirOwnSquare()
    {
        Assert.Equal(0, CreatureSizes.Reach(CreatureSize.Tiny));
    }

    [Fact]
    public void SizeMovesArmourClassAndAttackTogether()
    {
        var ogre = new Creature("Ogre", AbilityScores.All(10), 30, 4) { Size = CreatureSize.Large };
        var sword = Rules.Combat.WeaponAttack.Melee("club", "1d8", Rules.Combat.DamageType.Bludgeoning);

        Assert.Equal(9, ogre.ArmorClass.Total);
        Assert.Equal(-1, Rules.Combat.Strike.AttackBonus(ogre, sword).Total);
        Assert.Equal(10, ogre.Reach);
    }

    [Fact]
    public void GrowingChangesBothImmediately()
    {
        var wizard = new Creature("Wizard", AbilityScores.All(10), 12, 3);
        var staff = Rules.Combat.WeaponAttack.Melee("staff", "1d6", Rules.Combat.DamageType.Bludgeoning);

        Assert.Equal(10, wizard.ArmorClass.Total);
        Assert.Equal(5, wizard.Reach);

        wizard.Size = CreatureSize.Large;   // Enlarge Person

        Assert.Equal(9, wizard.ArmorClass.Total);
        Assert.Equal(-1, Rules.Combat.Strike.AttackBonus(wizard, staff).Total);
        Assert.Equal(10, wizard.Reach);
    }

    [Fact]
    public void SizeIsNamedInBothBreakdowns()
    {
        var halfling = new Creature("Halfling", AbilityScores.All(10), 8, 1) { Size = CreatureSize.Small };
        var sling = Rules.Combat.WeaponAttack.Melee("sling", "1d4", Rules.Combat.DamageType.Bludgeoning);

        Assert.Contains("size (Size)", halfling.ArmorClass.Explain().ToString());
        Assert.Contains("size (Size)", Rules.Combat.Strike.AttackBonus(halfling, sling).ToString());
    }
}

public class BattlefieldTests
{
    private static Creature Someone(string name = "Someone") =>
        new(name, AbilityScores.All(10), 20, 2);

    [Fact]
    public void ItKnowsItsOwnEdges()
    {
        var field = new Battlefield(10, 8);

        Assert.True(field.Contains(new GridSquare(0, 0)));
        Assert.True(field.Contains(new GridSquare(9, 7)));
        Assert.False(field.Contains(new GridSquare(10, 7)));
        Assert.False(field.Contains(new GridSquare(-1, 0)));
    }

    [Fact]
    public void PlacingIsRecordedBothWays()
    {
        var field = new Battlefield(10, 10);
        var goblin = Someone("Goblin");

        field.Place(goblin, 4, 2);

        Assert.Equal(new GridSquare(4, 2), field.SquareOf(goblin));
        Assert.Same(goblin, field.OccupantOf(new GridSquare(4, 2)));
        Assert.Equal(new Position(22.5f, 12.5f), field.PositionOf(goblin));
    }

    [Fact]
    public void MovingSomeoneFreesTheSquareTheyLeft()
    {
        var field = new Battlefield(10, 10);
        var goblin = Someone("Goblin");
        field.Place(goblin, 4, 2);

        field.Place(goblin, 5, 2);

        Assert.Null(field.OccupantOf(new GridSquare(4, 2)));
        Assert.True(field.IsFree(new GridSquare(4, 2)));
    }

    [Fact]
    public void NobodyStandsInAWallOrOnSomeoneElse()
    {
        var field = new Battlefield(10, 10);
        field.Block(new GridSquare(1, 1));
        field.Place(Someone("First"), 2, 2);

        Assert.Throws<ArgumentException>(() => field.Place(Someone("Second"), 1, 1));
        Assert.Throws<ArgumentException>(() => field.Place(Someone("Third"), 2, 2));
        Assert.Throws<ArgumentException>(() => field.Place(Someone("Fourth"), 20, 20));
    }

    [Fact]
    public void RemovingClearsTheSquare()
    {
        var field = new Battlefield(10, 10);
        var goblin = Someone("Goblin");
        field.Place(goblin, 4, 2);

        Assert.True(field.Remove(goblin));
        Assert.False(field.Remove(goblin));
        Assert.Null(field.SquareOf(goblin));
        Assert.True(field.IsFree(new GridSquare(4, 2)));
    }

    [Fact]
    public void DistanceNeedsBothOfThemOnTheMap()
    {
        var field = new Battlefield(10, 10);
        var here = Someone("Here");
        var there = Someone("There");
        field.Place(here, 0, 0);

        Assert.Null(field.DistanceInFeet(here, there));

        field.Place(there, 3, 3);
        Assert.Equal(20, field.DistanceInFeet(here, there));
    }

    [Fact]
    public void ReachDependsOnSize()
    {
        var field = new Battlefield(10, 10);
        var human = Someone("Human");
        var ogre = new Creature("Ogre", AbilityScores.All(10), 30, 4) { Size = CreatureSize.Large };
        var target = Someone("Target");

        field.Place(human, 0, 0);
        field.Place(ogre, 1, 0);
        field.Place(target, 2, 0);

        // Ten feet away: the ogre can touch it, the human cannot.
        Assert.False(field.IsWithinReach(human, target));
        Assert.True(field.IsWithinReach(ogre, target));
        Assert.True(field.IsWithinReach(human, ogre));
    }

    [Fact]
    public void AnythingOffTheMapIsNotConstrainedByIt()
    {
        var field = new Battlefield(10, 10);
        var placed = Someone("Placed");
        field.Place(placed, 0, 0);

        Assert.True(field.IsWithinReach(placed, Someone("Absent")));
    }

    // ---- walking ----

    [Fact]
    public void AStraightWalkCostsFiveASquare()
    {
        var field = new Battlefield(10, 10);

        var path = new[] { new GridSquare(0, 0), new(1, 0), new(2, 0), new(3, 0) };

        Assert.Equal(15, field.PathCost(path));
    }

    [Fact]
    public void DiagonalsArePricedByAlternationNotByCount()
    {
        var field = new Battlefield(10, 10);

        var path = new[] { new GridSquare(0, 0), new(1, 1), new(2, 2), new(3, 3), new(4, 4) };

        // 5 + 10 + 5 + 10, not four lots of five and not four lots of seven.
        Assert.Equal(30, field.PathCost(path));
    }

    [Fact]
    public void TheDiagonalCountCarriesAcrossStraightSteps()
    {
        var field = new Battlefield(10, 10);

        // One diagonal, a straight, then another diagonal: the second diagonal is still the
        // expensive one even though a straight step came between them.
        var path = new[] { new GridSquare(0, 0), new(1, 1), new(2, 1), new(3, 2) };

        Assert.Equal(20, field.PathCost(path));
    }

    [Fact]
    public void DifficultGroundCostsDouble()
    {
        var field = new Battlefield(10, 10);
        field.MakeDifficult(new GridSquare(2, 0));

        var path = new[] { new GridSquare(0, 0), new(1, 0), new(2, 0) };

        Assert.Equal(15, field.PathCost(path));
    }

    [Fact]
    public void APathIsFoundAndStartsWhereYouAre()
    {
        var field = new Battlefield(10, 10);

        var path = field.FindPath(new GridSquare(0, 0), new GridSquare(3, 0));

        Assert.Equal(new GridSquare(0, 0), path[0]);
        Assert.Equal(new GridSquare(3, 0), path[^1]);
        Assert.Equal(15, field.PathCost(path));
    }

    [Fact]
    public void AWallIsRoutedAround()
    {
        var field = new Battlefield(10, 10);
        for (var y = 0; y <= 3; y++)
        {
            field.Block(new GridSquare(2, y));
        }

        var path = field.FindPath(new GridSquare(0, 0), new GridSquare(4, 0));

        Assert.NotEmpty(path);
        Assert.DoesNotContain(path, square => field.IsBlocked(square));
        Assert.True(field.PathCost(path) > 20);
    }

    [Fact]
    public void NoWayThroughReturnsNothingRatherThanThrowing()
    {
        var field = new Battlefield(5, 3);
        for (var y = 0; y < 3; y++)
        {
            field.Block(new GridSquare(2, y));
        }

        Assert.Empty(field.FindPath(new GridSquare(0, 0), new GridSquare(4, 0)));
    }

    [Fact]
    public void YouMayWalkThroughSomeoneButNotStopOnThem()
    {
        var field = new Battlefield(5, 1);
        field.Place(Someone("InTheWay"), 2, 0);

        // A one-square corridor with someone standing in the middle of it.
        var through = field.FindPath(new GridSquare(0, 0), new GridSquare(4, 0));
        Assert.Contains(new GridSquare(2, 0), through);

        Assert.Empty(field.FindPath(new GridSquare(0, 0), new GridSquare(2, 0)));
    }

    [Fact]
    public void ApproachingStopsWithinReachAndWithinBudget()
    {
        var field = new Battlefield(20, 5);
        var hero = Someone("Hero");
        var goblin = Someone("Goblin");
        field.Place(hero, 0, 0);
        field.Place(goblin, 10, 0);

        var path = field.FindApproach(hero, goblin, feetAvailable: 30);

        Assert.Equal(new GridSquare(0, 0), path[0]);
        Assert.True(field.PathCost(path) <= 30);
        Assert.True(field.IsFree(path[^1]));
    }

    [Fact]
    public void ApproachingCloseEnoughStopsAdjacent()
    {
        var field = new Battlefield(20, 5);
        var hero = Someone("Hero");
        var goblin = Someone("Goblin");
        field.Place(hero, 0, 0);
        field.Place(goblin, 4, 0);

        var path = field.FindApproach(hero, goblin, feetAvailable: 30);

        Assert.Equal(new GridSquare(3, 0), path[^1]);
        Assert.Equal(5, Distance.Between(path[^1], new GridSquare(4, 0)));
    }

    [Fact]
    public void RejectsImpossibleGround()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Battlefield(0, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Battlefield(5, 0));
        Assert.Throws<ArgumentNullException>(() => new Battlefield(5, 5).Place(null!, 0, 0));
    }
}

public class UnblockTests
{
    [Fact]
    public void AnOpenedSquareCanBeWalkedThrough()
    {
        var field = new Battlefield(3, 1).Block(new GridSquare(1, 0));

        Assert.Empty(field.FindPath(new GridSquare(0, 0), new GridSquare(2, 0)));

        field.Unblock(new GridSquare(1, 0));

        Assert.True(field.IsPassable(new GridSquare(1, 0)));
        Assert.Equal(3, field.FindPath(new GridSquare(0, 0), new GridSquare(2, 0)).Count);
    }

    [Fact]
    public void OpeningWhatWasNeverShutDoesNothing()
    {
        var field = new Battlefield(2, 2);

        field.Unblock(new GridSquare(0, 0));

        Assert.True(field.IsPassable(new GridSquare(0, 0)));
    }
}
