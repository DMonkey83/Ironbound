using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Maps;

public class LineOfSightTests
{
    [Fact]
    public void AnEmptyFieldHidesNothing()
    {
        var field = new Battlefield(12, 12);

        Assert.True(field.HasLineOfSight(new GridSquare(0, 0), new GridSquare(11, 11)));
        Assert.False(field.HasCover(new GridSquare(0, 0), new GridSquare(11, 11)));
    }

    [Fact]
    public void ASquareAlwaysSeesItself()
    {
        var field = new Battlefield(6, 6).Block(new GridSquare(2, 2));

        Assert.True(field.HasLineOfSight(new GridSquare(2, 3), new GridSquare(2, 3)));
        Assert.False(field.HasCover(new GridSquare(2, 3), new GridSquare(2, 3)));
    }

    [Fact]
    public void APillarDirectlyInLineGivesCoverWithoutBlockingTheShot()
    {
        // (0,0) --- [wall] --- (4,0), all three in a row.
        var field = new Battlefield(8, 8).Block(new GridSquare(2, 0));

        // The shot still exists: the rules draw from corners, and the line along the wall's edge
        // slides past it. But no single corner gets a clean look at all four of the target's, so
        // the target is behind something — which is exactly what cover means.
        Assert.True(field.HasLineOfSight(new GridSquare(0, 0), new GridSquare(4, 0)));
        Assert.True(field.HasCover(new GridSquare(0, 0), new GridSquare(4, 0)));
    }

    [Fact]
    public void CoverIsMutual()
    {
        var field = new Battlefield(8, 8).Block(new GridSquare(2, 0));

        Assert.Equal(
            field.HasCover(new GridSquare(0, 0), new GridSquare(4, 0)),
            field.HasCover(new GridSquare(4, 0), new GridSquare(0, 0)));
    }

    [Fact]
    public void AWallOffToOneSideObstructsNothing()
    {
        var field = new Battlefield(10, 10).Block(new GridSquare(2, 6));

        Assert.True(field.HasLineOfSight(new GridSquare(0, 0), new GridSquare(6, 0)));
        Assert.False(field.HasCover(new GridSquare(0, 0), new GridSquare(6, 0)));
    }

    [Fact]
    public void AnArrowCanBeShotDownTheSeamBesideAWall()
    {
        // Three walls stacked in a column, and a shooter lined up exactly on the seam between
        // two of them. Counter-intuitive but correct: corner-to-corner, the line runs along the
        // boundary and a line along an edge is not a line through the wall.
        var field = new Battlefield(10, 10)
            .Block(new GridSquare(2, 1))
            .Block(new GridSquare(2, 2))
            .Block(new GridSquare(2, 3));

        Assert.True(field.HasLineOfSight(new GridSquare(0, 2), new GridSquare(5, 2)));
        Assert.True(field.HasCover(new GridSquare(0, 2), new GridSquare(5, 2)));
    }

    [Fact]
    public void SomethingWalledInOnEverySideCannotBeSeenAtAll()
    {
        var field = new Battlefield(10, 10);
        foreach (var (x, y) in new[]
        {
            (1, 1), (2, 1), (3, 1),
            (1, 2),         (3, 2),
            (1, 3), (2, 3), (3, 3),
        })
        {
            field.Block(new GridSquare(x, y));
        }

        Assert.False(field.HasLineOfSight(new GridSquare(0, 0), new GridSquare(2, 2)));
        Assert.True(field.HasCover(new GridSquare(0, 0), new GridSquare(2, 2)));
    }

    [Fact]
    public void ACreatureTheGroundDoesNotKnowAboutIsNotConstrainedByIt()
    {
        var field = new Battlefield(6, 6).Block(new GridSquare(2, 2));
        var seen = Goblin();

        Assert.True(field.HasLineOfSight(Goblin(), seen));
        Assert.False(field.HasCover(Goblin(), seen));
    }

    private static Creature Goblin() =>
        new("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 5, 1);

    [Theory]
    [InlineData(0, 0, 5, 5, 0, 0, true)]     // straight through the middle
    [InlineData(0, 2.5f, 25, 2.5f, 2, 0, true)]  // along the row, through the interior
    [InlineData(0, 0, 25, 0, 2, 0, false)]   // along the top edge, grazing it
    [InlineData(0, 5, 25, 5, 2, 0, false)]   // along the bottom edge
    public void ALineAlongAnEdgeDoesNotCountAsPassingThrough(
        float fromX, float fromY, float toX, float toY, int squareX, int squareY, bool crosses) =>
        Assert.Equal(crosses, LineOfSight.CrossesInterior(
            new Position(fromX, fromY), new Position(toX, toY), new GridSquare(squareX, squareY)));
}
