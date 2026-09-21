using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Saves;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Classes;

public class ExperienceTableTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1_999, 1)]
    [InlineData(2_000, 2)]
    [InlineData(22_999, 5)]
    [InlineData(23_000, 6)]
    [InlineData(34_999, 6)]
    [InlineData(35_000, 7)]
    [InlineData(99_999_999, 20)]
    public void ExperienceBuysLevelsAtTheWrittenPace(int experience, int level) =>
        Assert.Equal(level, Levelling.LevelFor(experience));

    [Fact]
    public void EachLevelCostsMoreThanTheLastByAWideningMargin()
    {
        var steps = Enumerable.Range(1, 8)
            .Select(level => Levelling.ThresholdFor(level + 1) - Levelling.ThresholdFor(level))
            .ToList();

        // Not merely rising: rising faster. That is what makes "come back when you are
        // stronger" an answer rather than a rudeness.
        Assert.Equal(steps.OrderBy(step => step), steps);
        Assert.True(steps[^1] > steps[0] * 5);
    }

    [Fact]
    public void ThereIsNothingPastTheTopOfTheTable()
    {
        Assert.Null(Levelling.NextThreshold(Levelling.ThresholdFor(Levelling.Maximum)));
        Assert.Equal(35_000, Levelling.NextThreshold(23_000));
    }

    [Fact]
    public void SomethingToughIsWorthMoreThanSomethingWeak()
    {
        var goblin = TestContent.Library.BuildCreature("goblin")!;
        var sergeant = TestContent.Library.BuildCreature("hobgoblin-sergeant")!;

        Assert.True(Levelling.Award(sergeant) > Levelling.Award(goblin) * 10);
    }
}

public class GainingALevelTests
{
    [Fact]
    public void ABaseAttackGoesUpAndASecondSwingArrives()
    {
        var fighter = TestContent.Library.BuildCreature("valeria")!;

        Assert.Equal(6, fighter.BaseAttackBonus);

        for (var level = 0; level < 5; level++)
        {
            Levelling.Gain(fighter, Fighter);
        }

        Assert.Equal(11, fighter.BaseAttackBonus);
        Assert.Equal(3, fighter.AttacksPerFullAttack);   // the step at +11
    }

    [Fact]
    public void SavesAreRecomputedRatherThanNudged()
    {
        var fighter = TestContent.Library.BuildCreature("valeria")!;

        Levelling.Gain(fighter, Fighter);
        Levelling.Gain(fighter, Fighter);

        // Fighter 8: good Fortitude is 6, the poor ones 2. Adding deltas would have drifted.
        Assert.Equal(8, fighter.Level);
        Assert.Equal(SaveProgression.Good(8), fighter.Saves[Save.Fortitude].Base);
        Assert.Equal(SaveProgression.Poor(8), fighter.Saves[Save.Will].Base);
    }

    [Fact]
    public void HitPointsRiseButWoundsDoNotClose()
    {
        var fighter = TestContent.Library.BuildCreature("valeria")!;
        fighter.HitPoints.Take(20);

        var before = fighter.HitPoints.Maximum;
        Levelling.Gain(fighter, Fighter);

        // A d10 at average is six, and Constitution 14 adds two more for the new die.
        Assert.Equal(before + 8, fighter.HitPoints.Maximum);
        Assert.Equal(20, fighter.HitPoints.Damage);
    }

    [Fact]
    public void TakingALevelInSomethingElseMakesYouTwoThings()
    {
        var fighter = TestContent.Library.BuildCreature("valeria")!;

        Levelling.Gain(fighter, Wizard);

        Assert.Equal("Fighter 6 / Wizard 1", fighter.Description);
        Assert.Equal(7, fighter.Level);

        // Each class contributes at its own level: six plus nothing, not seven.
        Assert.Equal(6, fighter.BaseAttackBonus);
        Assert.Equal(1, fighter.Spells.CasterLevel);
    }

    [Fact]
    public void ACasterLevelFollowsTheClassThatHasIt()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.Equal(5, merrin.Spells.CasterLevel);

        Levelling.Gain(merrin, Wizard);

        Assert.Equal(6, merrin.Spells.CasterLevel);
        Assert.Equal(3, merrin.BaseAttackBonus);   // half progression: six halved
    }

    [Fact]
    public void ALevelledCharacterMatchesOneBuiltThatWayFromTheStart()
    {
        var grown = TestContent.Library.BuildCreature("valeria")!;
        Levelling.Gain(grown, Fighter);
        Levelling.Gain(grown, Fighter);

        // The same person, if the content file had said "fighter 8" all along.
        Assert.Equal(8, grown.Level);
        Assert.Equal(Progression.BaseAttack([new ClassLevel(Fighter, 8)]), grown.BaseAttackBonus);
        Assert.Equal(
            Progression.HitPointsBase([new ClassLevel(Fighter, 8)]) + (2 * 8),
            grown.HitPoints.Maximum);
    }

    [Fact]
    public void NobodyClimbsPastTheTopOfTheTable()
    {
        var fighter = new Creature("Epic", AbilityScores.All(10), 200, 20);
        fighter.Levels.Add(new ClassLevel(Fighter, Levelling.Maximum));

        Assert.False(Levelling.Gain(fighter, Fighter));
    }

    private static ClassDefinition Fighter => TestContent.Library.GetClass("fighter")!;

    private static ClassDefinition Wizard => TestContent.Library.GetClass("wizard")!;
}

public class SpellSlotTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(4, 1, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(4, 5, 0)]     // the bonus stops where the modifier does
    [InlineData(8, 1, 2)]     // and another for every four points past it
    [InlineData(8, 4, 2)]
    [InlineData(8, 5, 1)]
    public void AHighAbilityIsWorthExtraSpells(int modifier, int spellLevel, int bonus) =>
        Assert.Equal(bonus, Progression.BonusSlots(modifier, spellLevel));

    [Fact]
    public void TheClassTableDecidesTheRest()
    {
        var wizard = TestContent.Library.GetClass("wizard")!;

        Assert.Equal([1], wizard.SlotsAt(1));
        Assert.Equal([2, 1], wizard.SlotsAt(3));      // the first second-level slot
        Assert.Equal([3, 2, 1], wizard.SlotsAt(5));
        Assert.Empty(wizard.SlotsAt(0));
        Assert.Empty(wizard.SlotsAt(99));
    }

    [Fact]
    public void AFighterHasNoTableAndThereforeNoSlots()
    {
        var fighter = TestContent.Library.GetClass("fighter")!;

        Assert.Empty(fighter.SpellSlots);
        Assert.Empty(Progression.SlotsFor([new ClassLevel(fighter, 20)], 5));
    }

    [Fact]
    public void MerrinsSlotsComeFromHerClassRatherThanHerFile()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        // Base 3/2/1 at wizard 5, plus one of each from Intelligence 18.
        Assert.Equal(4, merrin.Spells.SlotsMaximum(1));
        Assert.Equal(3, merrin.Spells.SlotsMaximum(2));
        Assert.Equal(2, merrin.Spells.SlotsMaximum(3));
        Assert.Equal(0, merrin.Spells.SlotsMaximum(4));
    }

    [Fact]
    public void LevellingAWizardGivesNewSlotsWithoutRefillingTheSpentOnes()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;
        merrin.Spells.Spend(merrin.Spells.Prepared.First(spell => spell.Level == 3));

        Assert.Equal(1, merrin.Spells.SlotsRemaining(3));

        Levelling.Gain(merrin, TestContent.Library.GetClass("wizard")!);

        // Wizard 6 is 3/3/2 before the bonus, so the third level goes from two slots to three.
        Assert.Equal(3, merrin.Spells.SlotsMaximum(3));

        // And the one she cast this morning is still gone.
        Assert.Equal(2, merrin.Spells.SlotsRemaining(3));
    }

    [Fact]
    public void AWizardReachingThirdLevelGainsTheirFirstSecondLevelSlot()
    {
        var apprentice = new Creature("Apprentice", new AbilityScores(8, 12, 12, 14, 10, 10), 6, 1);
        var wizard = TestContent.Library.GetClass("wizard")!;

        apprentice.Levels.Add(new ClassLevel(wizard, 1));
        Levelling.Gain(apprentice, wizard);

        Assert.Equal(0, apprentice.Spells.SlotsMaximum(2));

        Levelling.Gain(apprentice, wizard);

        Assert.Equal(1 + Progression.BonusSlots(2, 2), apprentice.Spells.SlotsMaximum(2));
    }
}

public class LevelsOnTheCreatureTests
{
    [Fact]
    public void ACreatureBuiltFromContentKnowsWhatItIs()
    {
        Assert.Equal("Fighter 6", TestContent.Library.BuildCreature("valeria")!.Description);
        Assert.Equal("Barbarian 6", TestContent.Library.BuildCreature("karn")!.Description);
        Assert.Equal("Wizard 5", TestContent.Library.BuildCreature("merrin")!.Description);
    }

    [Fact]
    public void SomethingWithNoClassesFallsBackOnItsHitDice()
    {
        var ooze = new Creature("Ooze", AbilityScores.All(10), 30, 4);

        Assert.Equal(4, ooze.Level);
        Assert.Empty(ooze.Levels);
    }
}
