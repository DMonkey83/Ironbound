using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Saves;

public class SaveInfoTests
{
    [Theory]
    [InlineData(Save.Fortitude, Ability.Constitution)]
    [InlineData(Save.Reflex, Ability.Dexterity)]
    [InlineData(Save.Will, Ability.Wisdom)]
    public void EachSaveHasItsAbility(Save save, Ability ability)
    {
        Assert.Equal(ability, SaveInfo.AbilityFor(save));
    }

    [Fact]
    public void NamesReadForACharacterSheet()
    {
        Assert.Equal("Fortitude", SaveInfo.Name(Save.Fortitude));
        Assert.Equal("Ref", SaveInfo.Abbreviate(Save.Reflex));
        Assert.Equal(3, SaveInfo.All.Count);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 2, 0)]
    [InlineData(2, 3, 0)]
    [InlineData(3, 3, 1)]
    [InlineData(6, 5, 2)]
    [InlineData(10, 7, 3)]
    [InlineData(20, 12, 6)]
    public void ProgressionsMatchTheTables(int level, int good, int poor)
    {
        Assert.Equal(good, SaveProgression.Good(level));
        Assert.Equal(poor, SaveProgression.Poor(level));
    }

    [Fact]
    public void ProgressionsRejectNegativeLevels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveProgression.Good(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaveProgression.Poor(-1));
    }
}

public class SavingThrowTests
{
    /// <summary>A sixth-level fighter: Fort +7, Ref +4, Will +3.</summary>
    private static Creature Fighter()
    {
        var fighter = new Creature("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);
        fighter.Saves.Fortitude.Base = SaveProgression.Good(6);
        fighter.Saves.Reflex.Base = SaveProgression.Poor(6);
        fighter.Saves.Will.Base = SaveProgression.Poor(6);
        return fighter;
    }

    [Fact]
    public void TotalIsBasePlusAbilityPlusModifiers()
    {
        var fighter = Fighter();

        Assert.Equal(7, fighter.Saves.Fortitude.Total);
        Assert.Equal(4, fighter.Saves.Reflex.Total);
        Assert.Equal(3, fighter.Saves.Will.Total);
    }

    [Fact]
    public void ACloakOfResistanceHelpsEverySave()
    {
        var fighter = Fighter();

        foreach (var save in SaveInfo.All)
        {
            fighter.Saves[save].Modifiers.Add(2, BonusType.Resistance, "Cloak of Resistance");
        }

        Assert.Equal(9, fighter.Saves.Fortitude.Total);
        Assert.Equal(6, fighter.Saves.Reflex.Total);
        Assert.Equal(5, fighter.Saves.Will.Total);
    }

    [Fact]
    public void TwoResistanceBonusesDoNotStack()
    {
        var fighter = Fighter();
        fighter.Saves.Will.Modifiers.Add(2, BonusType.Resistance, "Cloak of Resistance");
        fighter.Saves.Will.Modifiers.Add(1, BonusType.Resistance, "Resistance");

        Assert.Equal(5, fighter.Saves.Will.Total);
    }

    [Fact]
    public void TheAbilityIsReadLiveSoABuffLandsImmediately()
    {
        var fighter = Fighter();

        fighter.Effects.Apply(new ModifierEffect("Bear's Endurance", Duration.Minutes(1))
            .GrantsToAbility(Ability.Constitution, 4, BonusType.Enhancement));

        Assert.Equal(9, fighter.Saves.Fortitude.Total);

        fighter.Effects.Advance(Duration.Minutes(1), new SequenceRandom(1));

        Assert.Equal(7, fighter.Saves.Fortitude.Total);
    }

    [Fact]
    public void ExplainAccountsForTheWholeNumber()
    {
        var fighter = Fighter();
        fighter.Saves.Fortitude.Modifiers.Add(2, BonusType.Resistance, "Cloak of Resistance");

        var breakdown = fighter.Saves.Fortitude.Explain();

        Assert.Equal(fighter.Saves.Fortitude.Total, breakdown.Total);
        Assert.Equal(
            "+5 untyped (Base Save) +2 untyped (Con) +2 resistance (Cloak of Resistance) = 9",
            breakdown.ToString());
    }

    // ---- rolling ----

    [Fact]
    public void MeetingTheDifficultyClassIsEnough()
    {
        var fighter = Fighter();

        var result = fighter.Saves.Attempt(Save.Fortitude, 17, new SequenceRandom(10));

        Assert.True(result.Succeeded);
        Assert.Equal(17, result.Total);
        Assert.Equal(0, result.Margin);
    }

    [Fact]
    public void OneShortIsAFailure()
    {
        var result = Fighter().Saves.Attempt(Save.Fortitude, 17, new SequenceRandom(9));

        Assert.True(result.Failed);
        Assert.Equal(-1, result.Margin);
    }

    [Fact]
    public void ANaturalOneFailsWhateverTheBonus()
    {
        var fighter = Fighter();
        fighter.Saves.Will.Modifiers.Add(50, BonusType.Untyped, "Absurd");

        var result = fighter.Saves.Attempt(Save.Will, 5, new SequenceRandom(1));

        Assert.True(result.Failed);
        Assert.True(result.DecidedByNaturalRoll);
    }

    [Fact]
    public void ANaturalTwentySucceedsWhateverTheDifficulty()
    {
        var result = Fighter().Saves.Attempt(Save.Will, 40, new SequenceRandom(20));

        Assert.True(result.Succeeded);
        Assert.True(result.DecidedByNaturalRoll);
    }

    [Fact]
    public void AHarsherRulesetCanTakeThoseFloorsAway()
    {
        var fighter = Fighter();
        var brutal = new RuleOptions
        {
            NaturalTwentyAlwaysSaves = false,
            NaturalOneAlwaysFailsSaves = false,
        };

        Assert.True(fighter.Saves.Attempt(Save.Will, 40, new SequenceRandom(20), brutal).Failed);

        fighter.Saves.Will.Modifiers.Add(50, BonusType.Untyped, "Absurd");
        var lucky = fighter.Saves.Attempt(Save.Will, 5, new SequenceRandom(1), brutal);

        Assert.True(lucky.Succeeded);
        Assert.False(lucky.DecidedByNaturalRoll);
    }

    [Fact]
    public void SaveFloorsAreIndependentOfAttackFloors()
    {
        // Armour class absolute, saves still merciful.
        var mixed = new RuleOptions { NaturalTwentyAlwaysHits = false };

        Assert.True(mixed.NaturalTwentyAlwaysSaves);
        Assert.True(Fighter().Saves.Attempt(Save.Reflex, 40, new SequenceRandom(20), mixed).Succeeded);
    }

    [Fact]
    public void EachSaveRollsItsOwnNumbers()
    {
        var fighter = Fighter();

        // The same natural 12 against DC 18: Fortitude clears it, Will does not.
        Assert.True(fighter.Saves.Attempt(Save.Fortitude, 18, new SequenceRandom(12)).Succeeded);
        Assert.True(fighter.Saves.Attempt(Save.Will, 18, new SequenceRandom(12)).Failed);
    }

    [Fact]
    public void FormatsForTheLog()
    {
        var fighter = Fighter();

        Assert.Equal(
            "Fortitude save: d20 [12] +7 = 19 vs DC 17 — success",
            fighter.Saves.Attempt(Save.Fortitude, 17, new SequenceRandom(12)).ToString());

        Assert.Equal(
            "Will save: d20 [1] — automatic failure",
            fighter.Saves.Attempt(Save.Will, 5, new SequenceRandom(1)).ToString());

        Assert.Equal(
            "Reflex save: d20 [20] — automatic success",
            fighter.Saves.Attempt(Save.Reflex, 40, new SequenceRandom(20)).ToString());

        Assert.Equal("Fort +7, Ref +4, Will +3", fighter.Saves.ToString());
    }

    [Fact]
    public void ACreatureWithNoConstitutionGetsNothingFromIt()
    {
        var skeleton = new Creature(
            "Skeleton",
            new AbilityScores(
            [
                new AbilityScore(Ability.Strength, 15),
                new AbilityScore(Ability.Dexterity, 14),
                AbilityScore.NonAbility(Ability.Constitution),
                AbilityScore.NonAbility(Ability.Intelligence),
                new AbilityScore(Ability.Wisdom, 10),
                new AbilityScore(Ability.Charisma, 10),
            ]),
            baseHitPoints: 6,
            hitDice: 1);

        skeleton.Saves.Fortitude.Base = SaveProgression.Poor(1);

        Assert.Equal(0, skeleton.Saves.Fortitude.Total);
    }

    [Fact]
    public void RejectsImpossibleConstruction()
    {
        var abilities = AbilityScores.All(10);

        Assert.Throws<ArgumentNullException>(() => new SavingThrow(Save.Will, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SavingThrow(Save.Will, abilities.Wisdom, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SavingThrows(abilities).Will.Base = -1);
        Assert.Throws<ArgumentNullException>(() => new SavingThrows(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new SavingThrows(abilities).Will.Roll(10, null!));
    }

    // ---- the shape spells will use ----

    [Fact]
    public void APoisonThatAllowsASaveDoesNothingWhenTheSaveHolds()
    {
        var victim = Fighter();

        // 12 + 7 = 19 against DC 17.
        Assert.True(victim.Saves.Attempt(Save.Fortitude, 17, new SequenceRandom(12)).Succeeded);

        Assert.False(victim.Effects.Has("Spider Venom"));
        Assert.Equal(52, victim.HitPoints.Maximum);
    }

    [Fact]
    public void AndLandsWhenItDoesNot()
    {
        var victim = Fighter();

        // 9 + 7 = 16 against DC 17.
        var save = victim.Saves.Attempt(Save.Fortitude, 17, new SequenceRandom(9));
        Assert.True(save.Failed);

        victim.Effects.Apply(new ModifierEffect("Spider Venom", Duration.Rounds(6))
            .GrantsToAbility(Ability.Constitution, -4, BonusType.Untyped));

        // Constitution 14 drops to 10, costing the +2 per hit die across six of them.
        Assert.Equal(40, victim.HitPoints.Maximum);

        victim.Effects.Advance(Duration.Rounds(6), new SequenceRandom(1));
        Assert.Equal(52, victim.HitPoints.Maximum);
    }
}
