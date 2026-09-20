using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Combat;

public class ManeuverNumberTests
{
    [Fact]
    public void ManeuverBonusIsSkillStrengthAndSize()
    {
        var fighter = Brawler("Fighter", strength: 18, baseAttack: 6);

        // +6 base attack, +4 Strength, Medium contributes nothing.
        Assert.Equal(10, Maneuvers.Bonus(fighter).Total);
    }

    [Fact]
    public void ManeuverDefenceIsTenPlusSkillStrengthAndDexterity()
    {
        var fighter = Brawler("Fighter", strength: 18, baseAttack: 6);

        // 10 + 6 + 4 Strength + 2 Dexterity.
        Assert.Equal(22, Maneuvers.Defense(fighter));
    }

    [Theory]
    [InlineData(CreatureSize.Small, -1)]
    [InlineData(CreatureSize.Medium, 0)]
    [InlineData(CreatureSize.Large, 1)]
    [InlineData(CreatureSize.Huge, 2)]
    public void SizeRunsTheOppositeWayFromArmourClass(CreatureSize size, int modifier)
    {
        Assert.Equal(modifier, CreatureSizes.ManeuverModifier(size));

        // The whole point: small is harder to hit and easier to shove.
        Assert.Equal(-CreatureSizes.Modifier(size), CreatureSizes.ManeuverModifier(size));
    }

    [Fact]
    public void AGoblinIsEasierToShoveThanToHit()
    {
        var goblin = Brawler("Goblin", strength: 11, baseAttack: 1);
        goblin.Size = CreatureSize.Small;

        // 10 + 1 base attack + 0 Strength + 2 Dexterity - 1 size.
        Assert.Equal(12, Maneuvers.Defense(goblin));
    }

    [Fact]
    public void BeingShakenMakesYouWorseAtShovingPeople()
    {
        var fighter = Brawler("Fighter", strength: 18, baseAttack: 6);
        var before = Maneuvers.Bonus(fighter).Total;

        fighter.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(3)));

        Assert.Equal(before - 2, Maneuvers.Bonus(fighter).Total);
    }

    [Fact]
    public void SomebodyStunnedIsFarEasierToPutDown()
    {
        var fighter = Brawler("Fighter", strength: 18, baseAttack: 6);
        var before = Maneuvers.Defense(fighter);

        fighter.Effects.Apply(ConditionInfo.Effect(Condition.Stunned, Duration.Rounds(1)));

        // Loses the +2 Dexterity, exactly as armour class does.
        Assert.Equal(before - 2, Maneuvers.Defense(fighter));
    }

    [Fact]
    public void ArmourDoesNotHelpYouKeepYourFeetButADeflectionFieldDoes()
    {
        var fighter = Brawler("Fighter", strength: 18, baseAttack: 6);
        var bare = Maneuvers.Defense(fighter);

        fighter.ArmorClass.Modifiers.Add(8, BonusType.Armor, "Plate");
        Assert.Equal(bare, Maneuvers.Defense(fighter));

        fighter.ArmorClass.Modifiers.Add(2, BonusType.Deflection, "Ring");
        Assert.Equal(bare + 2, Maneuvers.Defense(fighter));
    }

    [Fact]
    public void TheCheckReadsLikeAnAttackRollAndSaysWhatItWasAgainst()
    {
        var attacker = Brawler("Karn", strength: 18, baseAttack: 6);
        var target = Brawler("Goblin", strength: 11, baseAttack: 1);

        var check = Maneuvers.Attempt(attacker, target, new SequenceRandom(true, 12), ManeuverKind.Trip);

        Assert.Equal(22, check.Total);        // 12 + 10
        Assert.True(check.Succeeded);
        Assert.Contains("vs CMD", check.ToString());
        Assert.Contains("trip", check.ToString());
    }

    [Fact]
    public void FailingByTenOrMoreIsCalledOutSeparately()
    {
        var weakling = Brawler("Weakling", strength: 6, baseAttack: 0);
        var giant = Brawler("Giant", strength: 20, baseAttack: 10);

        var check = Maneuvers.Attempt(weakling, giant, new SequenceRandom(true, 2), ManeuverKind.Trip);

        Assert.False(check.Succeeded);
        Assert.True(check.Backfired);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(12, 2)]
    public void EveryFivePointsOverIsAnotherFiveFeet(int margin, int extra)
    {
        var attacker = Brawler("Karn", strength: 18, baseAttack: 6);   // bonus +10
        var target = Brawler("Target", strength: 10, baseAttack: 0);   // CMD 12

        // A natural roll chosen so the margin lands exactly where the case wants it.
        var check = Maneuvers.Attempt(
            attacker, target, new SequenceRandom(true, 12 - 10 + margin), ManeuverKind.BullRush);

        Assert.Equal(margin, check.Margin);
        Assert.Equal(extra, check.ExtraIncrements);
    }

    internal static Creature Brawler(
        string name, int strength, int baseAttack, int allegiance = 1, int hitPoints = 60) =>
        new(name, new AbilityScores(strength, 14, 14, 10, 10, 10), hitPoints, 6)
        {
            Allegiance = allegiance,
            BaseAttackBonus = baseAttack,
        };
}

public class TrippingTests
{
    [Fact]
    public void ASuccessfulTripPutsThemOnTheFloor()
    {
        var (turn, _, goblin) = Skirmish(roll: 18);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new TripAction(goblin)));

        Assert.True(result.Check!.Succeeded);
        Assert.True(goblin.IsProne);
        Assert.Contains("knocked prone", result.Description);

        // What that is actually worth is proven next door, in ProneTests: standing up spends
        // the move action, and a creature that has spent its move cannot full attack.
    }

    [Fact]
    public void AnOrdinaryFailureCostsNothingButTheAction()
    {
        var (turn, karn, goblin) = Skirmish(roll: 8, targetStrength: 18, targetBaseAttack: 8);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new TripAction(goblin)));

        Assert.False(result.Check!.Succeeded);
        Assert.False(goblin.IsProne);
        Assert.False(karn.IsProne);
        Assert.False(turn.Budget.HasStandard);
    }

    [Fact]
    public void OverreachingPutsYouOnTheFloorInstead()
    {
        // A weak attacker against a very solid target: the margin is bound to be ten or worse.
        var (turn, karn, goblin) = Skirmish(roll: 1, attackerStrength: 6, targetStrength: 20, targetBaseAttack: 10);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new TripAction(goblin)));

        Assert.True(result.Check!.Backfired);
        Assert.True(karn.IsProne);
        Assert.False(goblin.IsProne);
        Assert.Contains("overbalances", result.Description);
    }

    [Fact]
    public void ReachingInProvokes()
    {
        var (turn, _, goblin) = Skirmish(roll: 18, armTarget: true);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new TripAction(goblin)));

        Assert.Single(result.Opportunities);
        Assert.Contains("provoking 1", result.Description);
    }

    [Fact]
    public void YouCannotTripSomebodyAlreadyDown()
    {
        var (turn, _, goblin) = Skirmish(roll: 18);
        goblin.Effects.Apply(ConditionInfo.Effect(Condition.Prone, Duration.Permanent));

        Assert.False(turn.CanTake(new TripAction(goblin)));
    }

    [Fact]
    public void ItWillNotReachAcrossTheRoom()
    {
        var (turn, _, goblin) = Skirmish(roll: 18, apart: 4);

        Assert.False(turn.CanTake(new TripAction(goblin)));
    }

    private static (Turn Turn, Creature Karn, Creature Goblin) Skirmish(
        int roll,
        int apart = 1,
        int attackerStrength = 18,
        int targetStrength = 11,
        int targetBaseAttack = 1,
        bool armTarget = false)
    {
        var field = new Battlefield(10, 4);
        var karn = ManeuverNumberTests.Brawler("Karn", attackerStrength, baseAttack: 6);
        var goblin = ManeuverNumberTests.Brawler(
            "Goblin", targetStrength, targetBaseAttack, allegiance: 2);

        karn.Attacks.Add(WeaponAttack.Melee("greataxe", "1d12", DamageType.Slashing));
        if (armTarget)
        {
            goblin.Attacks.Add(WeaponAttack.Melee("scimitar", "1d6", DamageType.Slashing));
        }

        field.Place(karn, 0, 0);
        field.Place(goblin, apart, 0);

        // Initiative to Karn, then — if the goblin is armed — its free swing and its d6 of
        // damage, and only then the maneuver check. The order matters: the provocation is
        // resolved before the check, so it draws from the stream first.
        int[] scripted = armTarget ? [20, 1, 12, 4, roll] : [20, 1, roll];

        var encounter = new Encounter(
            [karn, goblin], new SequenceRandom(true, scripted), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, karn, goblin);
    }
}

public class BullRushTests
{
    [Fact]
    public void ASuccessfulShoveDrivesThemBackFiveFeet()
    {
        var (turn, field, goblin) = Line(roll: 5);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new BullRushAction(goblin)));

        Assert.True(result.Check!.Succeeded);
        Assert.Equal(new GridSquare(2, 0), field.SquareOf(goblin));
        Assert.Contains("driven back", result.Description);
    }

    [Fact]
    public void EveryFivePointsOverIsAnotherFiveFeet()
    {
        var (turn, field, goblin) = Line(roll: 20);

        turn.Take(new BullRushAction(goblin));

        // Pushed further than one square, and still in a straight line away.
        Assert.True(field.SquareOf(goblin)!.Value.X > 2);
        Assert.Equal(0, field.SquareOf(goblin)!.Value.Y);
    }

    [Fact]
    public void AWallStopsThemDead()
    {
        var (turn, field, goblin) = Line(roll: 20, wallAt: 2);

        var result = Assert.IsType<ManeuverActionResult>(turn.Take(new BullRushAction(goblin)));

        Assert.True(result.Check!.Succeeded);
        Assert.Equal(new GridSquare(1, 0), field.SquareOf(goblin));
        Assert.Contains("holds its ground", result.Description);
    }

    [Fact]
    public void AFailedShoveMovesNobody()
    {
        var (turn, field, goblin) = Line(roll: 1);

        turn.Take(new BullRushAction(goblin));

        Assert.Equal(new GridSquare(1, 0), field.SquareOf(goblin));
    }

    private static (Turn Turn, Battlefield Field, Creature Goblin) Line(int roll, int? wallAt = null)
    {
        var field = new Battlefield(12, 4);
        var karn = ManeuverNumberTests.Brawler("Karn", strength: 18, baseAttack: 6);
        var goblin = ManeuverNumberTests.Brawler("Goblin", strength: 11, baseAttack: 1, allegiance: 2);

        if (wallAt is { } wall)
        {
            field.Block(new GridSquare(wall, 0));
        }

        field.Place(karn, 0, 0);
        field.Place(goblin, 1, 0);

        var encounter = new Encounter(
            [karn, goblin], new SequenceRandom(true, 20, 1, roll), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, field, goblin);
    }
}
