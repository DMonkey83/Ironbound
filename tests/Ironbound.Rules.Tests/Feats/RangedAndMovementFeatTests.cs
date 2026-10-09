using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>Deadly Aim, Manyshot, Far Shot and the rest of the archer's feats.</summary>
public class RangedFeatTests
{
    private static Creature Archer(string feats, int level = 6, string bow = "shortbow") =>
        ClassKit.Make("fighter", level, $"\"items\": [\"{bow}\", \"dagger\"], \"feats\": [{feats}]", [12, 19, 12, 10, 10, 10]);

    [Fact]
    public void ImprovedPreciseShotSeesPastCover()
    {
        var archer = Archer("\"point-blank-shot\", \"precise-shot\", \"improved-precise-shot\"", 11);
        var target = ClassKit.Dummy();
        var field = ClassKit.Field((archer, 0, 0), (target, 6, 0));
        field.Block(new GridSquare(3, 0));
        field.Block(new GridSquare(3, 1));

        Assert.True(field.HasCover(archer, target));
        Assert.Equal(0, Strike.CoverFor(archer, target, field, archer.PrimaryAttack!));

        // The same shot from the same square without the feat.
        var plain = Archer("\"point-blank-shot\", \"precise-shot\"", 11);
        field.Remove(archer);
        field.Place(plain, 0, 0);
        Assert.Equal(Strike.CoverBonus, Strike.CoverFor(plain, target, field, plain.PrimaryAttack!));
    }

    [Fact]
    public void DeadlyAimTradesRangedAccuracyForDamageAndLeavesTheBladeAlone()
    {
        var archer = Archer("\"deadly-aim\"");
        var bow = archer.PrimaryAttack!;
        var dagger = archer.MeleeAttack!;
        var shot = Strike.AttackBonus(archer, bow).Total;
        var stab = Strike.AttackBonus(archer, dagger).Total;
        var damage = Strike.DamageBonus(archer, bow).Total;

        Assert.True(archer.Stances.Adopt(Stance.DeadlyAim));

        // Base attack six: two off, four on.
        Assert.Equal(shot - 2, Strike.AttackBonus(archer, bow).Total);
        Assert.Equal(damage + 4, Strike.DamageBonus(archer, bow).Total);
        Assert.Equal(stab, Strike.AttackBonus(archer, dagger).Total);
        Assert.False(Archer(string.Empty).Stances.CanAdopt(Stance.DeadlyAim));
    }

    [Fact]
    public void ManyshotsFirstArrowIsTwo()
    {
        var archer = Archer("\"point-blank-shot\", \"rapid-shot\", \"manyshot\"");
        var target = ClassKit.Dummy("Target", hitPoints: 200);
        var field = ClassKit.Field((archer, 0, 0), (target, 4, 0));

        // Three shots with Rapid Shot; the first hits and is two arrows.
        var encounter = ClassKit.Fight([archer], [target], field, 15, 3, 3, 1, 1);
        var result = Assert.IsType<FullAttackResult>(encounter.BeginNextTurn()!.Take(new FullAttackAction(target, archer.PrimaryAttack)));

        Assert.Contains(result.Strikes[0].Notes, note => note.StartsWith("a second arrow", StringComparison.Ordinal));
        Assert.True(FullAttackAction.IsManyshot(archer, archer.PrimaryAttack!));
        Assert.False(FullAttackAction.IsManyshot(archer, archer.MeleeAttack!));

        var bonus = Strike.DamageBonus(archer, archer.PrimaryAttack!, 20).Total;
        Assert.Equal(2 * (3 + bonus), target.HitPoints.Damage);
    }

    [Fact]
    public void ManyshotIsForBowsAndNotCrossbows()
    {
        var archer = Archer("\"point-blank-shot\", \"rapid-shot\", \"manyshot\"", bow: "light-crossbow");

        Assert.False(FullAttackAction.IsManyshot(archer, archer.PrimaryAttack!));
    }

    [Fact]
    public void FarShotHalvesTheRangePenalty()
    {
        var far = Archer("\"point-blank-shot\", \"far-shot\"");
        var plain = Archer("\"point-blank-shot\"");
        var target = ClassKit.Dummy();
        var field = new Battlefield(40, 4);
        field.Place(far, 0, 0);
        field.Place(plain, 0, 1);
        field.Place(target, 37, 0);

        // A shortbow's increment is sixty feet; 185 feet is the fourth, so -6 plainly and -3 with the feat.
        var range = (Creature who) => Strike.AttackBonus(who, who.PrimaryAttack!, target, field).Entries
            .Single(entry => entry.Modifier.Source.StartsWith("Range", StringComparison.Ordinal)).Modifier.Value;

        Assert.Equal(-6, range(plain));
        Assert.Equal(-3, range(far));
    }

    [Fact]
    public void ShotOnTheRunWalksShootsAndWalksOn()
    {
        var archer = ClassKit.Make(
            "fighter", 4, "\"items\": [\"shortbow\"], \"feats\": [\"dodge\", \"mobility\", \"point-blank-shot\", \"shot-on-the-run\"]",
            [12, 16, 12, 10, 10, 10]);
        var target = ClassKit.Dummy("Target");
        var field = ClassKit.Field((archer, 0, 0), (target, 8, 0));
        var encounter = ClassKit.Fight([archer], [target], field, 15, 3);
        var path = new[] { new GridSquare(0, 0), new GridSquare(1, 0), new GridSquare(2, 0), new GridSquare(2, 1), new GridSquare(1, 2) };

        var result = Assert.IsType<MoveAndAttackResult>(encounter.BeginNextTurn()!.Take(new ShotOnTheRunAction(path, 2, target)));

        Assert.True(result.Attack!.Strike!.IsHit);
        Assert.Equal(new GridSquare(1, 2), field.SquareOf(archer));
    }

    [Fact]
    public void PinpointTargetingShootsPastArmourAndNotAfterMoving()
    {
        var archer = Archer("\"point-blank-shot\", \"precise-shot\", \"improved-precise-shot\", \"pinpoint-targeting\"", 16);
        var knight = ClassKit.Dummy("Knight", armour: 30);
        var field = ClassKit.Field((archer, 0, 0), (knight, 4, 0));
        var encounter = ClassKit.Fight([archer], [knight], field, 10, 3, 3);
        var turn = encounter.BeginNextTurn()!;

        var result = Assert.IsType<AttackActionResult>(turn.Take(new PinpointTargetingAction(archer.PrimaryAttack!, knight)));
        Assert.True(result.Strike!.IsHit);
        Assert.Equal(knight.ArmorClass.Touch, result.Strike.Attack.TargetArmorClass);

        var moved = ClassKit.Fight([archer], [knight], field);
        var second = moved.BeginNextTurn()!;
        second.Take(FiveFootStepAction.To(new GridSquare(0, 0), new GridSquare(0, 1)));
        Assert.False(second.CanTake(new PinpointTargetingAction(archer.PrimaryAttack!, knight)));
    }
}

/// <summary>Fleet, Run, Nimble Moves and Acrobatic Steps.</summary>
public class MovementFeatTests
{
    [Fact]
    public void FleetIsFiveFeetATimeOutOfHeavierArmour()
    {
        var light = ClassKit.Make("rogue", 1, "\"items\": [\"leather-armour\"], \"feats\": [\"fleet\"]");
        var twice = ClassKit.Make("rogue", 1, "\"items\": [\"leather-armour\"], \"feats\": [\"fleet\", \"fleet\"]");
        var heavy = ClassKit.Make("fighter", 1, "\"items\": [\"scale-mail\"], \"feats\": [\"fleet\"]");

        Assert.Equal(35, light.CurrentSpeed);
        Assert.Equal(40, twice.CurrentSpeed);
        Assert.Equal(20, heavy.CurrentSpeed);
    }

    private static IReadOnlyList<GridSquare> Line(int from, int to) =>
        [.. Enumerable.Range(from, to - from + 1).Select(x => new GridSquare(x, 1))];

    [Fact]
    public void RunningIsFourTimesSpeedInAStraightLineAndCostsDexterity()
    {
        var runner = ClassKit.Make("fighter", 1);
        var foe = ClassKit.Dummy("Foe");
        var field = new Battlefield(40, 4);
        field.Place(runner, 0, 1);
        field.Place(foe, 39, 3);
        var encounter = ClassKit.Fight([runner], [foe], field);
        var turn = encounter.BeginNextTurn()!;

        Assert.False(turn.CanTake(new RunAction(Line(0, 25))));
        Assert.False(turn.CanTake(new RunAction([new GridSquare(0, 1), new GridSquare(1, 1), new GridSquare(2, 2)])));
        Assert.NotNull(turn.Take(new RunAction(Line(0, 24))));

        // Dexterity gone until its next turn, as if flat-footed.
        Assert.True(encounter.IsFlatFooted(runner));
    }

    [Fact]
    public void TheRunFeatIsFiveTimesAndKeepsDexterity()
    {
        var runner = ClassKit.Make("fighter", 1, "\"feats\": [\"run\"]");
        var foe = ClassKit.Dummy("Foe");
        var field = new Battlefield(40, 4);
        field.Place(runner, 0, 1);
        field.Place(foe, 39, 3);
        var encounter = ClassKit.Fight([runner], [foe], field);

        encounter.BeginNextTurn()!.End();
        encounter.BeginNextTurn()!.End();
        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new RunAction(Line(0, 30))));
        Assert.False(encounter.IsFlatFooted(runner));
    }

    [Fact]
    public void NobodyFatiguedRuns()
    {
        var runner = ClassKit.Make("fighter", 1);
        runner.Effects.Apply(ConditionInfo.Effect(Condition.Fatigued, Duration.Permanent));
        var field = new Battlefield(40, 4);
        field.Place(runner, 0, 1);
        var encounter = ClassKit.Fight([runner], [], field);

        Assert.False(encounter.BeginNextTurn()!.CanTake(new RunAction(Line(0, 5))));
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("\"nimble-moves\"", 5)]
    [InlineData("\"nimble-moves\", \"acrobatic-steps\"", 6)]
    public void NimbleFeetCrossSomeDifficultGroundAsIfItWereClear(string feats, int squares)
    {
        var walker = ClassKit.Make("rogue", 1, $"\"feats\": [{feats}]", [10, 16, 10, 10, 10, 10]);
        var field = new Battlefield(12, 3);
        field.Place(walker, 0, 1);
        field.MakeDifficult(new GridSquare(1, 1));
        field.MakeDifficult(new GridSquare(2, 1));

        var encounter = ClassKit.Fight([walker], [], field);
        var turn = encounter.BeginNextTurn()!;

        // Thirty feet, the first two squares of it difficult: ten each plainly, so four squares
        // in all; Nimble Moves makes the first of them clear, and Acrobatic Steps both.
        var furthest = Enumerable.Range(1, 8).Last(end => turn.CanTake(new MoveAction(Line(0, end))));
        Assert.Equal(squares, furthest);
    }

    [Fact]
    public void NimbleMovesStepsIntoDifficultGround()
    {
        var nimble = ClassKit.Make("rogue", 1, "\"feats\": [\"nimble-moves\"]", [10, 16, 10, 10, 10, 10]);
        var plain = ClassKit.Make("rogue", 1, abilities: [10, 16, 10, 10, 10, 10]);
        var field = new Battlefield(12, 4);
        field.Place(nimble, 0, 1);
        field.Place(plain, 0, 3);
        field.MakeDifficult(new GridSquare(1, 1));
        field.MakeDifficult(new GridSquare(1, 3));
        var encounter = ClassKit.Fight([nimble, plain], [], field);

        Assert.True(encounter.BeginNextTurn()!.CanTake(FiveFootStepAction.To(new GridSquare(0, 1), new GridSquare(1, 1))));
        Assert.False(encounter.BeginNextTurn()!.CanTake(FiveFootStepAction.To(new GridSquare(0, 3), new GridSquare(1, 3))));
    }
}
