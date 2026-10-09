using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;

namespace Ironbound.Rules.Tests.Conditions;

public class SleepTests
{
    [Fact]
    public void ASleeperDoesNothingAndCannotDodge()
    {
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);

        Sleep.Fall(orc);

        Assert.True(orc.Has(Condition.Asleep));
        Assert.False(orc.CanAct);
        Assert.True(orc.DeniesDexterity);
    }

    [Fact]
    public void LeftAloneItSleepsForTwoRounds()
    {
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);

        Sleep.Fall(orc);

        Assert.Equal(Duration.Rounds(2), orc.Effects.Find("Asleep")!.Duration);
    }

    [Fact]
    public void AnUnhurtSleeperStaysAsleep()
    {
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);
        Sleep.Fall(orc);

        Assert.Null(Sleep.Sync(orc));
        Assert.True(orc.Has(Condition.Asleep));
    }

    [Fact]
    public void AHurtOneWakesAndSaysSo()
    {
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);
        Sleep.Fall(orc);
        orc.HitPoints.Take(1);

        var woken = Sleep.Sync(orc);

        Assert.NotNull(woken);
        Assert.Contains("wakes", woken.Value.Description);
        Assert.False(orc.Has(Condition.Asleep));
        Assert.True(orc.CanAct);
    }

    [Fact]
    public void SomebodyAwakeIsNotWokenAgain()
    {
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);
        orc.HitPoints.Take(1);

        Assert.Null(Sleep.Sync(orc));
    }

    [Fact]
    public void ASleeperLosesTheFirstTwoRoundsAndActsInTheThird()
    {
        var (encounter, _, orc) = Bedroom();
        Sleep.Fall(orc);

        var seen = new List<(int Round, bool CanAct)>();
        while (encounter.BeginNextTurn() is { } turn && turn.Round <= 3)
        {
            if (ReferenceEquals(turn.Actor, orc))
            {
                seen.Add((turn.Round, turn.Combatant.CanAct));
            }

            turn.End();
        }

        // Every hero has had two turns by the time it stirs, which is the whole point of
        // creeping in quietly.
        Assert.Equal([(1, false), (2, false), (3, true)], seen);
    }

    [Fact]
    public void HurtingASleeperWakesItBeforeItsNextTurn()
    {
        var (encounter, hero, orc) = Bedroom();
        Sleep.Fall(orc);

        // The hero rolled higher, so the first turn is theirs.
        var first = encounter.BeginNextTurn()!;
        Assert.Same(hero, first.Actor);
        orc.HitPoints.Take(2);
        first.End();

        var second = encounter.BeginNextTurn()!;

        Assert.Same(orc, second.Actor);
        Assert.True(second.Combatant.CanAct);
        Assert.Contains(second.Events, happened => happened.Description.Contains("wakes"));
    }

    [Fact]
    public void ASleeperTakesNoSwingAtSomebodyWalkingPast()
    {
        var (encounter, hero, orc) = Bedroom();
        Sleep.Fall(orc);

        var struck = Opportunities.Provoke(encounter, hero, new GridSquare(0, 0));

        Assert.Empty(struck);
        Assert.False(encounter.CombatantFor(orc)!.CanTakeOpportunity);
    }

    [Fact]
    public void AndASwingAtASleeperFindsItWithoutItsDexterity()
    {
        var (_, hero, orc) = Bedroom();
        Sleep.Fall(orc);

        var result = Strike.Resolve(hero, hero.PrimaryAttack!, orc, new SequenceRandom(true, 10, 4));

        Assert.Equal(orc.ArmorClass.FlatFooted, result.Attack.TargetArmorClass);
    }

    [Fact]
    public void SleepSurvivesASaveWithItsClockWhereItWas()
    {
        var (encounter, _, orc) = Bedroom();
        Sleep.Fall(orc);
        encounter.BeginNextTurn()!.End();

        var restored = GameSave.Restore(
            GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))),
            TestContent.Library);
        var sleeper = restored.Order.Single(c => c.Creature.Name == "Orc").Creature;

        Assert.True(sleeper.Has(Condition.Asleep));
        Assert.Equal(
            orc.Effects.Find("Asleep")!.Remaining,
            sleeper.Effects.Find("Asleep")!.Remaining);
    }

    /// <summary>A hero beside a sleeping orc, the hero certain to act first.</summary>
    private static (Encounter Encounter, Creature Hero, Creature Orc) Bedroom()
    {
        var field = new Battlefield(8, 4);
        var hero = ConditionRulesTests.Fighter("Hero");
        var orc = ConditionRulesTests.Fighter("Orc", allegiance: 2);

        hero.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        orc.Attacks.Add(WeaponAttack.Melee("shortspear", "1d6", DamageType.Piercing));

        field.Place(hero, 0, 0);
        field.Place(orc, 1, 0);

        var encounter = new Encounter(
            [hero, orc], new SequenceRandom(true, 20, 1, 12, 4), rules: null, battlefield: field);

        return (encounter, hero, orc);
    }
}
