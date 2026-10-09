using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Magic;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Maps;

public class ReachWeaponTests
{
    private static Creature Holding(string weapon, CreatureSize size = CreatureSize.Medium, string name = "Spearman")
    {
        var creature = new Creature(name, AbilityScores.All(12), 20, 2) { Size = size };
        creature.Attacks.Add(TestContent.Library.BuildWeapon(weapon, size: size)!);
        return creature;
    }

    [Fact]
    public void ALongspearThreatensTenFeetAndNotTheSquareBeside()
    {
        var spearman = Holding("longspear");
        var field = ClassKit.Field((spearman, 5, 5));

        var threatened = field.ThreatenedBy(spearman).ToHashSet();

        // The second ring, all sixteen squares of it, corners included.
        Assert.Equal(16, threatened.Count);
        Assert.Contains(new GridSquare(7, 5), threatened);
        Assert.Contains(new GridSquare(7, 7), threatened);
        Assert.DoesNotContain(new GridSquare(6, 5), threatened);
        Assert.DoesNotContain(new GridSquare(6, 6), threatened);
        Assert.Equal(new ReachBand(10, 10), Battlefield.ReachOf(spearman));
    }

    [Fact]
    public void TheSecondDiagonalIsTheOneExceptionToTheCount()
    {
        var band = new ReachBand(0, 10);

        // Two diagonals count as fifteen feet anywhere else.
        Assert.Equal(15, Distance.Between(new GridSquare(0, 0), new GridSquare(2, 2)));
        Assert.True(band.Covers(new GridSquare(0, 0), new GridSquare(2, 2)));
        Assert.False(new ReachBand(0, 15).Covers(new GridSquare(0, 0), new GridSquare(3, 3)));
        Assert.True(new ReachBand(0, 15).Covers(new GridSquare(0, 0), new GridSquare(2, 2)));
    }

    [Fact]
    public void ALargeWielderReachesFifteenToTwentyAndNothingNearer()
    {
        var giant = Holding("longspear", CreatureSize.Large, "Giant");
        var field = ClassKit.Field((giant, 5, 5));

        Assert.Equal(new ReachBand(15, 20), Battlefield.ReachOf(giant));
        Assert.True(field.Threatens(giant, new GridSquare(8, 5)));
        Assert.True(field.Threatens(giant, new GridSquare(9, 5)));
        Assert.False(field.Threatens(giant, new GridSquare(7, 5)));
        Assert.False(field.Threatens(giant, new GridSquare(6, 5)));
    }

    [Fact]
    public void NaturalReachStillCoversTheSquaresAround()
    {
        var ogre = TestContent.Library.BuildCreature("ogre")!;
        var field = ClassKit.Field((ogre, 5, 5));

        Assert.Equal(new ReachBand(0, 10), Battlefield.ReachOf(ogre));
        Assert.True(field.Threatens(ogre, new GridSquare(6, 5)));
        Assert.True(field.Threatens(ogre, new GridSquare(7, 7)));
    }

    [Fact]
    public void SomethingTinyReachesTheNextSquareWithAPolearm()
    {
        var sprite = Holding("longspear", CreatureSize.Tiny, "Sprite");

        Assert.Equal(new ReachBand(5, 5), Battlefield.ReachOf(sprite));
        Assert.Equal(new ReachBand(0, 0), ReachBand.Natural(sprite));
    }

    [Fact]
    public void TheBandSaysWhatItIs()
    {
        Assert.Equal("5 ft", new ReachBand(0, 5).ToString());
        Assert.Equal("10 ft, not adjacent", new ReachBand(10, 10).ToString());
        Assert.Equal("15-20 ft, not within 10 ft", new ReachBand(15, 20).ToString());
    }

    [Fact]
    public void ReachIsAskedOfTheWeaponInHand()
    {
        var spearman = Holding("longspear");
        var foe = Holding("dagger", name: "Foe");
        var field = ClassKit.Field((spearman, 5, 5), (foe, 6, 5));
        var dagger = TestContent.Library.BuildWeapon("dagger")!;

        Assert.False(field.IsWithinReach(spearman, foe));
        Assert.True(field.IsWithinReach(spearman, foe, dagger));
        Assert.True(field.IsWithinTouch(spearman, foe));
    }

    [Fact]
    public void APolearmCannotStrikeTheManStandingOnYourToes()
    {
        var spearman = Holding("longspear");
        var near = Holding("dagger", name: "Near");
        var far = Holding("dagger", name: "Far");
        var field = ClassKit.Field((spearman, 5, 5), (near, 6, 5), (far, 5, 7));
        var encounter = ClassKit.Fight([spearman], [near, far], field);
        var turn = encounter.BeginNextTurn()!;
        var spear = spearman.MeleeAttack!;

        Assert.False(turn.CanTake(new AttackAction(spear, near)));
        Assert.True(turn.CanTake(new AttackAction(spear, far)));
    }

    [Fact]
    public void WalkingAwayFromTheEndOfAPolearmProvokesAndWalkingPastItsFootDoesNot()
    {
        var spearman = Holding("longspear");
        var close = Holding("dagger", name: "Close");
        var field = ClassKit.Field((spearman, 5, 5), (close, 6, 5));
        var encounter = ClassKit.Fight([spearman], [close], field, Enumerable.Repeat(1, 20).ToArray());

        Assert.Empty(Opportunities.Provoke(encounter, close, new GridSquare(6, 5)));
        Assert.Single(Opportunities.Provoke(encounter, close, new GridSquare(7, 5)));
    }

    [Fact]
    public void TwoPolearmsFlankFromTenFeet()
    {
        var first = Holding("longspear", name: "First");
        var second = Holding("longspear", name: "Second");
        var target = Holding("dagger", name: "Target");
        var field = ClassKit.Field((first, 3, 5), (second, 7, 5), (target, 5, 5));
        ClassKit.Fight([first, second], [target], field);

        Assert.True(field.AreFlanking(first, second, target));
        Assert.True(field.WouldFlankFrom(new GridSquare(3, 5), first, target));
        Assert.False(field.WouldFlankFrom(new GridSquare(4, 5), first, target));
    }

    [Fact]
    public void ApproachingWithAPolearmStopsAtTheEndOfIt()
    {
        var spearman = Holding("longspear");
        var foe = Holding("dagger", name: "Foe");
        var field = ClassKit.Field((spearman, 1, 5), (foe, 8, 5));
        ClassKit.Fight([spearman], [foe], field);

        var path = field.FindApproach(spearman, foe, 30);

        Assert.Equal(10, Battlefield.ReachOf(spearman).FeetBetween(path[^1], new GridSquare(8, 5)));
    }

    [Fact]
    public void KneelingByTheDyingIsDoneWithAHandNotAPolearm()
    {
        var spearman = Holding("longspear");
        spearman.Skills.SetRanks(Ironbound.Rules.Skills.Skill.Heal, 1);
        var friend = Holding("dagger", name: "Friend");
        var field = ClassKit.Field((spearman, 5, 5), (friend, 6, 5));
        var encounter = ClassKit.Fight([spearman, friend], [Holding("dagger", name: "Foe")], field);
        friend.HitPoints.Take(friend.HitPoints.Maximum + 3);
        Ironbound.Rules.Effects.Bleeding.Sync(friend);

        Assert.True(encounter.BeginNextTurn()!.CanTake(new StabiliseAction(friend)));
    }

    [Fact]
    public void ATouchSpellReachesAsFarAsTheCastersArm() =>
        Assert.Equal(5, SpellRange.Touch.InFeet(Holding("longspear")));

    [Fact]
    public void ATripGoesAsFarAsTheWeaponAndAShoveOnlyAsFarAsTheBody()
    {
        var spearman = Holding("guisarme");
        var foe = Holding("dagger", name: "Foe");
        var field = ClassKit.Field((spearman, 5, 5), (foe, 7, 5));
        var turn = ClassKit.Fight([spearman], [foe], field).BeginNextTurn()!;

        Assert.True(turn.CanTake(new TripAction(foe)));
        Assert.False(turn.CanTake(new BullRushAction(foe)));
    }
}
