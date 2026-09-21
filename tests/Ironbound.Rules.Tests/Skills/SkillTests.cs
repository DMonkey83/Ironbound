using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Skills;

public class SkillTableTests
{
    [Theory]
    [InlineData(Skill.Stealth, Ability.Dexterity)]
    [InlineData(Skill.Climb, Ability.Strength)]
    [InlineData(Skill.Perception, Ability.Wisdom)]
    [InlineData(Skill.Spellcraft, Ability.Intelligence)]
    [InlineData(Skill.Diplomacy, Ability.Charisma)]
    public void EachSkillRunsOnItsOwnAbility(Skill skill, Ability ability) =>
        Assert.Equal(ability, SkillInfo.AbilityFor(skill));

    [Theory]
    [InlineData(Skill.DisableDevice, true)]
    [InlineData(Skill.Spellcraft, true)]
    [InlineData(Skill.Perception, false)]
    [InlineData(Skill.Stealth, false)]
    public void SomeThingsCannotBeTriedAtAllUntrained(Skill skill, bool trainedOnly) =>
        Assert.Equal(trainedOnly, SkillInfo.TrainedOnly(skill));

    [Fact]
    public void EverySkillHasAName() =>
        Assert.All(SkillInfo.All, skill => Assert.NotEmpty(SkillInfo.Name(skill)));
}

public class SkillBonusTests
{
    [Fact]
    public void RanksAndTheAbilityAddUp()
    {
        var rogue = Scout();
        rogue.Skills.SetRanks(Skill.Stealth, 5);

        // Five ranks and Dexterity 18, with no class to call it their own.
        Assert.Equal(9, rogue.Skills.Total(Skill.Stealth));
    }

    [Fact]
    public void ClassTrainingIsWorthThreeMoreOnTopOfTheRank()
    {
        var rogue = Scout();
        rogue.Levels.Add(new ClassLevel(TestContent.Library.GetClass("rogue")!, 5));
        rogue.Skills.SetRanks(Skill.Stealth, 5);

        Assert.True(rogue.Skills.IsClassSkill(Skill.Stealth));
        Assert.Equal(12, rogue.Skills.Total(Skill.Stealth));
    }

    [Fact]
    public void TheThreeIsForBeingTrainedNotForTheClassAlone()
    {
        var rogue = Scout();
        rogue.Levels.Add(new ClassLevel(TestContent.Library.GetClass("rogue")!, 5));

        // No ranks spent: a rogue who never practised is just a nimble person.
        Assert.Equal(4, rogue.Skills.Total(Skill.Stealth));
        Assert.DoesNotContain("Class skill", rogue.Skills.Explain(Skill.Stealth).ToString());
    }

    [Fact]
    public void ABonusFromElsewhereStacksInTheOrdinaryWay()
    {
        var rogue = Scout();
        rogue.Skills.SetRanks(Skill.Stealth, 5);
        rogue.Skills.Modifiers(Skill.Stealth).Add(2, BonusType.Competence, "Cloak");

        Assert.Equal(11, rogue.Skills.Total(Skill.Stealth));
        Assert.Contains("Cloak", rogue.Skills.Explain(Skill.Stealth).ToString());
    }

    [Fact]
    public void ADexterityDrainReachesStealthOnTheNextCheck()
    {
        var rogue = Scout();
        rogue.Skills.SetRanks(Skill.Stealth, 5);
        var before = rogue.Skills.Total(Skill.Stealth);

        rogue.Abilities[Ability.Dexterity].Modifiers.Add(-4, BonusType.Untyped, "Poison");

        // Nothing is cached, so nothing has to be refreshed.
        Assert.Equal(before - 2, rogue.Skills.Total(Skill.Stealth));
    }

    private static Creature Scout() =>
        new("Scout", new AbilityScores(10, 18, 12, 12, 12, 10), 30, 5);
}

public class SkillCheckTests
{
    [Fact]
    public void ACheckIsTheRollPlusTheBonus()
    {
        var scout = Trained(Skill.Perception, 5);

        var check = scout.Skills.Check(Skill.Perception, new SequenceRandom(true, 12), difficulty: 15);

        Assert.Equal(12 + scout.Skills.Total(Skill.Perception), check.Total);
        Assert.True(check.Succeeded);
        Assert.Contains("vs DC 15", check.ToString());
    }

    [Fact]
    public void WithoutADifficultyItSimplyReportsTheNumber()
    {
        var check = Trained(Skill.Stealth, 5)
            .Skills.Check(Skill.Stealth, new SequenceRandom(true, 9));

        Assert.Null(check.Succeeded);
        Assert.DoesNotContain("vs DC", check.ToString());
    }

    [Fact]
    public void ThereIsNoNaturalTwentyToSaveYou()
    {
        var oaf = new Creature("Oaf", new AbilityScores(10, 10, 10, 10, 10, 10), 20, 1);

        var check = oaf.Skills.Check(Skill.Perception, new SequenceRandom(true, 20), difficulty: 40);

        // An attack roll always keeps its five percent. A lock does not, which is the whole
        // reason content can be gated behind one.
        Assert.Equal(20, check.Total);
        Assert.False(check.Succeeded);
    }

    [Fact]
    public void SomeThingsYouCannotAttemptAtAll()
    {
        var oaf = new Creature("Oaf", new AbilityScores(10, 10, 10, 14, 10, 10), 20, 1);

        var check = oaf.Skills.Check(Skill.DisableDevice, new SequenceRandom(true, 20), difficulty: 10);

        Assert.True(check.Untrained);
        Assert.Null(check.Succeeded);
        Assert.Equal(0, check.Total);
        Assert.Contains("cannot attempt", check.ToString());
    }

    [Fact]
    public void ARankMakesTheImpossiblePossible()
    {
        var picker = new Creature("Picker", new AbilityScores(10, 14, 10, 14, 10, 10), 20, 1);
        picker.Skills.SetRanks(Skill.DisableDevice, 1);

        Assert.True(picker.Skills.CanAttempt(Skill.DisableDevice));
        Assert.False(picker.Skills
            .Check(Skill.DisableDevice, new SequenceRandom(true, 10), difficulty: 10).Untrained);
    }

    private static Creature Trained(Skill skill, int ranks)
    {
        var creature = new Creature("Scout", new AbilityScores(10, 14, 12, 12, 14, 10), 30, 5);
        creature.Skills.SetRanks(skill, ranks);

        return creature;
    }
}

public class SkillsFromContentTests
{
    [Fact]
    public void ACreatureComesOutOfItsFileWithWhatItTrainedAt()
    {
        var goblin = TestContent.Library.BuildCreature("goblin")!;

        Assert.Equal(6, goblin.Skills.Ranks(Skill.Stealth));
        Assert.Equal(2, goblin.Skills.Ranks(Skill.Perception));
        Assert.Equal(0, goblin.Skills.Ranks(Skill.Spellcraft));
    }

    [Fact]
    public void AClassBringsItsOwnSkillsWithIt()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.True(merrin.Skills.IsClassSkill(Skill.Spellcraft));
        Assert.False(merrin.Skills.IsClassSkill(Skill.Stealth));
    }

    [Fact]
    public void ASmallGoblinIsGoodAtHidingAndTheWizardIsNot()
    {
        var goblin = TestContent.Library.BuildCreature("goblin")!;
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        Assert.True(goblin.Skills.Total(Skill.Stealth) > merrin.Skills.Total(Skill.Stealth));
    }
}
