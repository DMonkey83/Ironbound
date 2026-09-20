using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Combat;

public class RangeIncrementTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 0)]
    [InlineData(60, 0)]      // exactly one increment is still the first
    [InlineData(65, -2)]
    [InlineData(120, -2)]
    [InlineData(125, -4)]
    [InlineData(600, -18)]   // the tenth and last
    public void AccuracyFallsOffEveryIncrement(int feet, int penalty) =>
        Assert.Equal(penalty, Shortbow().RangePenalty(feet));

    [Theory]
    [InlineData(600, true)]
    [InlineData(605, false)]
    public void TenIncrementsIsAsFarAsAnArrowGoes(int feet, bool reaches) =>
        Assert.Equal(reaches, Shortbow().IsWithinRange(feet));

    [Fact]
    public void AThrownWeaponGivesUpAtFive()
    {
        var axe = WeaponAttack.Ranged(
            "throwing axe", "1d6", DamageType.Slashing, 10,
            maximumIncrements: WeaponAttack.ThrownIncrements);

        Assert.Equal(50, axe.MaximumRange);
        Assert.False(axe.IsWithinRange(55));
    }

    [Fact]
    public void AMeleeWeaponHasNoRangeToSpeakOf()
    {
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);

        Assert.False(sword.IsRanged);
        Assert.Null(sword.MaximumRange);
        Assert.Equal(0, sword.RangePenalty(500));
        Assert.True(sword.IsWithinRange(500));
    }

    [Fact]
    public void ABowIsAimedWithDexterityAndNotPushedWithStrength()
    {
        var bow = Shortbow();

        Assert.Equal(Ability.Dexterity, bow.AttackAbility);
        Assert.Equal(AbilityDamageScale.None, bow.DamageScale);
        Assert.Equal(0, bow.ScaleDamage(4));
    }

    [Fact]
    public void ARangeIncrementOfNothingIsNotAWeapon() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WeaponAttack.Ranged("bow", "1d6", DamageType.Piercing, 0));

    internal static WeaponAttack Shortbow() => WeaponAttack.Ranged(
        "shortbow", "1d6", DamageType.Piercing, 60, new CriticalProfile(20, 3));
}

public class ShootingAcrossTheFieldTests
{
    [Fact]
    public void DistanceShowsUpInTheAttackBreakdown()
    {
        var (field, archer, target) = Range(apart: 5);   // 25 ft: well inside the first increment
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var close = Strike.AttackBonus(archer, bow, target, field);

        field.Remove(target);
        field.Place(target, 26, 0);   // 130 ft: into the third increment
        var far = Strike.AttackBonus(archer, bow, target, field);

        Assert.Equal(close.Total - 4, far.Total);
        Assert.Contains("Range", far.ToString());
    }

    [Fact]
    public void ShootingIntoAMeleeYourOwnSideIsInCostsFour()
    {
        var (field, archer, goblin) = Range(apart: 6);
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var alone = Strike.AttackBonus(archer, bow, goblin, field);

        var friend = Fighter("Karn", allegiance: 1);
        friend.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        field.Place(friend, 5, 0);   // right beside the goblin

        var crowded = Strike.AttackBonus(archer, bow, goblin, field);

        Assert.Equal(alone.Total - 4, crowded.Total);
        Assert.Contains("Firing into melee", crowded.ToString());
    }

    [Fact]
    public void YourOwnAdjacencyIsNotFiringIntoMelee()
    {
        // The archer standing next to the target is a different problem, and it is not this one.
        var (field, archer, goblin) = Range(apart: 1);
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        Assert.DoesNotContain("Firing into melee", Strike.AttackBonus(archer, bow, goblin, field).ToString());
    }

    [Fact]
    public void APillarInTheWayIsWorthFourArmourClass()
    {
        var field = new Battlefield(20, 20);
        var archer = Fighter("Archer", allegiance: 1);
        var goblin = Fighter("Goblin", allegiance: 2);
        field.Place(archer, 0, 0);
        field.Place(goblin, 4, 0);

        Assert.Equal(0, Strike.CoverFor(archer, goblin, field));

        field.Block(new GridSquare(2, 0));

        Assert.Equal(Strike.CoverBonus, Strike.CoverFor(archer, goblin, field));
    }

    [Fact]
    public void CoverRaisesTheArmourClassTheAttackIsMeasuredAgainst()
    {
        var field = new Battlefield(20, 20).Block(new GridSquare(2, 0));
        var archer = Fighter("Archer", allegiance: 1);
        var goblin = Fighter("Goblin", allegiance: 2);
        field.Place(archer, 0, 0);
        field.Place(goblin, 4, 0);

        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var bare = goblin.ArmorClass.Total;
        var shot = Strike.Resolve(archer, bow, goblin, new SequenceRandom(11, 3), field: field);

        Assert.Equal(bare + Strike.CoverBonus, shot.Attack.TargetArmorClass);
        Assert.Equal(Strike.CoverBonus, shot.Attack.Cover);
        Assert.Contains("+4 cover", shot.Attack.ToString());
    }

    private static (Battlefield Field, Creature Archer, Creature Target) Range(int apart)
    {
        var field = new Battlefield(40, 10);
        var archer = Fighter("Archer", allegiance: 1);
        var target = Fighter("Goblin", allegiance: 2);

        field.Place(archer, 0, 0);
        field.Place(target, apart, 0);

        return (field, archer, target);
    }

    internal static Creature Fighter(string name, int allegiance) =>
        new(name, new AbilityScores(14, 16, 12, 10, 10, 10), 20, 3) { Allegiance = allegiance };
}

public class ShootingInSomebodysFaceTests
{
    [Fact]
    public void LoosingAnArrowInAThreatenedSquareProvokes()
    {
        var (turn, archer, goblin) = Skirmish(apart: 1);
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(bow, goblin)));

        Assert.Single(result.Opportunities);
        Assert.Same(goblin, result.Opportunities[0].Attacker);
    }

    [Fact]
    public void SwingingASwordInAThreatenedSquareDoesNot()
    {
        var (turn, fighter, goblin) = Skirmish(apart: 1);
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);
        fighter.Attacks.Add(sword);

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(sword, goblin)));

        Assert.Empty(result.Opportunities);
    }

    [Fact]
    public void ShootingFromWellBackProvokesNothing()
    {
        var (turn, archer, goblin) = Skirmish(apart: 5);
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var result = Assert.IsType<AttackActionResult>(turn.Take(new AttackAction(bow, goblin)));

        Assert.Empty(result.Opportunities);
        Assert.NotNull(result.Strike);
    }

    [Fact]
    public void ACreatureHoldingOnlyABowTakesNoOpportunities()
    {
        var archer = ShootingAcrossTheFieldTests.Fighter("Archer", allegiance: 1);
        archer.Attacks.Add(RangeIncrementTests.Shortbow());

        // It threatens the ground either way — but it has nothing to swing, so nobody gets shot
        // for walking past.
        Assert.Null(archer.MeleeAttack);
        Assert.NotNull(archer.PrimaryAttack);
    }

    [Fact]
    public void AnArcherWithABladeReachesForTheBladeUpClose()
    {
        var archer = ShootingAcrossTheFieldTests.Fighter("Archer", allegiance: 1);
        var bow = RangeIncrementTests.Shortbow();
        var scimitar = WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing);
        archer.Attacks.Add(bow);
        archer.Attacks.Add(scimitar);

        Assert.Same(bow, archer.PrimaryAttack);
        Assert.Same(scimitar, archer.MeleeAttack);
    }

    private static (Turn Turn, Creature Actor, Creature Goblin) Skirmish(int apart)
    {
        var field = new Battlefield(40, 10);
        var actor = ShootingAcrossTheFieldTests.Fighter("Archer", allegiance: 1);
        var goblin = ShootingAcrossTheFieldTests.Fighter("Goblin", allegiance: 2);
        goblin.Attacks.Add(WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing));

        field.Place(actor, 0, 0);
        field.Place(goblin, apart, 0);

        // Initiative is rigged so the archer goes first; the rest feeds the swings.
        var encounter = new Encounter(
            [actor, goblin],
            new SequenceRandom([20, 1, 12, 4, 12, 4, 12, 4, 12, 4]),
            rules: null,
            battlefield: field);

        return (encounter.BeginNextTurn()!, actor, goblin);
    }
}

public class ShootingWhatYouCannotSeeTests
{
    [Fact]
    public void AnArrowWillNotCarryPastItsLastIncrement()
    {
        var (turn, archer, goblin) = Shot(apart: 121);   // 605 ft
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        Assert.Null(turn.Take(new AttackAction(bow, goblin)));
    }

    [Fact]
    public void WithinTheLastIncrementItStillCarries()
    {
        var (turn, archer, goblin) = Shot(apart: 120);   // 600 ft exactly
        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        Assert.NotNull(turn.Take(new AttackAction(bow, goblin)));
    }

    [Fact]
    public void YouCannotShootWhatIsWalledIn()
    {
        var field = new Battlefield(20, 20);
        var archer = ShootingAcrossTheFieldTests.Fighter("Archer", allegiance: 1);
        var goblin = ShootingAcrossTheFieldTests.Fighter("Goblin", allegiance: 2);

        foreach (var (x, y) in new[]
        {
            (1, 1), (2, 1), (3, 1), (1, 2), (3, 2), (1, 3), (2, 3), (3, 3),
        })
        {
            field.Block(new GridSquare(x, y));
        }

        field.Place(archer, 0, 0);
        field.Place(goblin, 2, 2);

        var bow = RangeIncrementTests.Shortbow();
        archer.Attacks.Add(bow);

        var encounter = new Encounter(
            [archer, goblin], new SequenceRandom([20, 1, 12, 4]), rules: null, battlefield: field);

        Assert.Null(encounter.BeginNextTurn()!.Take(new AttackAction(bow, goblin)));
    }

    [Fact]
    public void AMeleeWeaponStillHasToReach()
    {
        var (turn, fighter, goblin) = Shot(apart: 3);
        var sword = WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);
        fighter.Attacks.Add(sword);

        Assert.Null(turn.Take(new AttackAction(sword, goblin)));
    }

    private static (Turn Turn, Creature Actor, Creature Goblin) Shot(int apart)
    {
        var field = new Battlefield(apart + 2, 4);
        var actor = ShootingAcrossTheFieldTests.Fighter("Archer", allegiance: 1);
        var goblin = ShootingAcrossTheFieldTests.Fighter("Goblin", allegiance: 2);

        field.Place(actor, 0, 0);
        field.Place(goblin, apart, 0);

        var encounter = new Encounter(
            [actor, goblin], new SequenceRandom([20, 1, 12, 4]), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, actor, goblin);
    }
}
