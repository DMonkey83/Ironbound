using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Combat;

public class IterativeTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]     // the step that makes levelling feel like something
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(20, 4)]
    [InlineData(30, 4)]    // nothing past four from base attack alone
    public void ASecondSwingArrivesAtSixAndAThirdAtEleven(int baseAttack, int attacks) =>
        Assert.Equal(attacks, Iteratives.Count(baseAttack));

    [Theory]
    [InlineData(5, new[] { 0 })]
    [InlineData(6, new[] { 0, -5 })]
    [InlineData(11, new[] { 0, -5, -10 })]
    [InlineData(16, new[] { 0, -5, -10, -15 })]
    public void EachSwingAfterTheFirstCostsFive(int baseAttack, int[] penalties) =>
        Assert.Equal(penalties, Iteratives.Penalties(baseAttack));

    [Fact]
    public void ACreatureKnowsHowOftenItSwings()
    {
        var fighter = Fighter("Fighter", baseAttack: 11);

        Assert.Equal(3, fighter.AttacksPerFullAttack);
    }

    [Fact]
    public void TheIterativePenaltyLandsOnTheAttackRoll()
    {
        var fighter = Fighter("Fighter", baseAttack: 6);
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);

        var first = Strike.AttackBonus(fighter, sword);
        var second = Strike.AttackBonus(fighter, sword, iterativePenalty: -5);

        Assert.Equal(first.Total - 5, second.Total);
        Assert.Contains("Iterative attack", second.ToString());
    }

    [Fact]
    public void BaseAttackBonusReachesTheRollWithoutBeingAModifier()
    {
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);
        var novice = Fighter("Novice", baseAttack: 0);
        var veteran = Fighter("Veteran", baseAttack: 6);

        Assert.Equal(
            Strike.AttackBonus(novice, sword).Total + 6,
            Strike.AttackBonus(veteran, sword).Total);
        Assert.Contains("Base Attack Bonus", Strike.AttackBonus(veteran, sword).ToString());
    }

    internal static Creature Fighter(string name, int baseAttack, int allegiance = 1, int hitPoints = 60) =>
        new(name, new AbilityScores(16, 14, 14, 10, 10, 10), hitPoints, 6)
        {
            Allegiance = allegiance,
            BaseAttackBonus = baseAttack,
        };
}

public class FullAttackTests
{
    [Fact]
    public void SixBaseAttackSwingsTwice()
    {
        var (turn, fighter, dummy) = Duel(baseAttack: 6);

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy)));

        Assert.Equal(2, result.Strikes.Count);

        // The second is five worse than the first, which is the entire point of the trade.
        Assert.Equal(
            result.Strikes[0].Attack.Bonus.Total - 5,
            result.Strikes[1].Attack.Bonus.Total);
        Assert.Equal("Fighter attacks 2 times with longsword", result.Description);
    }

    [Fact]
    public void EachSwingReportsTheHitPointsAsTheyStoodWhenItLanded()
    {
        var (turn, _, dummy) = Duel(baseAttack: 6);

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy)));
        var landed = result.Strikes.Where(strike => strike.Attack.IsHit).ToList();

        Assert.Equal(2, landed.Count);

        // Read live, both swings reported the creature's final total and the second looked as
        // though it had done nothing at all.
        Assert.NotEqual(landed[0].TargetAfter, landed[1].TargetAfter);
        Assert.Contains(landed[0].TargetAfter, landed[0].ToString());
    }

    [Fact]
    public void FiveBaseAttackSwingsOnceAndSaysSo()
    {
        var (turn, _, dummy) = Duel(baseAttack: 5);

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy)));

        Assert.Single(result.Strikes);
        Assert.Equal("Fighter attacks once with longsword", result.Description);
    }

    [Fact]
    public void ItCostsTheWholeRound()
    {
        var (turn, _, dummy) = Duel(baseAttack: 6);

        turn.Take(new FullAttackAction(dummy));

        Assert.False(turn.Budget.HasStandard);
        Assert.False(turn.Budget.HasMove);

        // The swift survives; so, crucially, does the five-foot step.
        Assert.True(turn.Budget.HasSwift);
        Assert.False(turn.Combatant.HasTakenFiveFootStep);
    }

    [Fact]
    public void HavingMovedYouCannotThenFullAttack()
    {
        var (turn, fighter, dummy) = Duel(baseAttack: 6);

        Assert.True(turn.Budget.Spend(ActionCost.Move));

        // Not "refused because out of reach" — refused because the round is already half spent.
        Assert.False(turn.CanTake(new FullAttackAction(dummy)));
        Assert.True(turn.CanTake(new AttackAction(fighter.PrimaryAttack!, dummy)));
    }

    [Fact]
    public void AFiveFootStepStillLeavesRoomForEverything()
    {
        var (turn, _, dummy) = Duel(baseAttack: 6, apart: 2);

        var step = turn.Take(FiveFootStepAction.To(new GridSquare(0, 0), new GridSquare(1, 0)));

        Assert.NotNull(step);
        Assert.Equal(2, Assert.IsType<FullAttackResult>(
            turn.Take(new FullAttackAction(dummy))).Strikes.Count);
    }

    [Fact]
    public void SwingingStopsOnceTheTargetIsDown()
    {
        var (turn, _, dummy) = Duel(baseAttack: 16, dummyHitPoints: 6);

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy)));

        // Four attacks were available; hacking at a body is not one of the rules.
        Assert.False(dummy.IsConscious);
        Assert.InRange(result.Strikes.Count, 1, 3);
    }

    [Fact]
    public void ItWillNotStartIfNothingIsInReach()
    {
        var (turn, _, dummy) = Duel(baseAttack: 6, apart: 5);

        Assert.False(turn.CanTake(new FullAttackAction(dummy)));
    }

    [Fact]
    public void ABowFullAttacksToo()
    {
        var (turn, fighter, dummy) = Duel(baseAttack: 6, apart: 4);
        var bow = WeaponAttack.Ranged("shortbow", "1d6", DamageType.Piercing, 60);
        fighter.Attacks.Insert(0, bow);

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy, bow)));

        Assert.Equal(2, result.Strikes.Count);
        Assert.Same(bow, result.Weapon);
    }

    [Fact]
    public void ShootingEverythingProvokesOnceRatherThanOncePerArrow()
    {
        var (turn, fighter, dummy) = Duel(baseAttack: 6, apart: 1);
        var bow = WeaponAttack.Ranged("shortbow", "1d6", DamageType.Piercing, 60);
        fighter.Attacks.Insert(0, bow);
        dummy.Attacks.Add(WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing));

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy, bow)));

        Assert.Single(result.Opportunities);
    }

    [Fact]
    public void LeftUnaskedItReachesForABladeBeforeABow()
    {
        var (turn, fighter, dummy) = Duel(baseAttack: 6);
        fighter.Attacks.Insert(0, WeaponAttack.Ranged("shortbow", "1d6", DamageType.Piercing, 60));

        var result = Assert.IsType<FullAttackResult>(turn.Take(new FullAttackAction(dummy)));

        Assert.Equal("longsword", result.Weapon.Name);
    }

    private static (Turn Turn, Creature Fighter, Creature Dummy) Duel(
        int baseAttack, int apart = 1, int dummyHitPoints = 200)
    {
        var field = new Battlefield(12, 4);
        var fighter = IterativeTests.Fighter("Fighter", baseAttack);
        var dummy = IterativeTests.Fighter("Dummy", 0, allegiance: 2, hitPoints: dummyHitPoints);

        fighter.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        field.Place(fighter, 0, 0);
        field.Place(dummy, apart, 0);

        // Initiative rigged to the fighter, then a long tail of middling rolls for the swings.
        var encounter = new Encounter(
            [fighter, dummy],
            new SequenceRandom(true, 20, 1, 14, 5, 14, 5, 14, 5, 14, 5, 14, 5),
            rules: null,
            battlefield: field);

        return (encounter.BeginNextTurn()!, fighter, dummy);
    }
}
