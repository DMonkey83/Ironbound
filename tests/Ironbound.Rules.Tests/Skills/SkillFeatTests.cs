using Ironbound.Rules.Abilities;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Skills;

/// <summary>
/// The skill check as one stack: ranks, ability, armour, race, the skill feats and every
/// condition that reaches all checks at once.
/// </summary>
public class SkillFeatTests
{
    private static Creature With(string feats, int perception = 0, int[]? abilities = null) =>
        ClassKit.Make(
            "rogue",
            10,
            $"\"feats\": [{feats}], \"skills\": [ {{ \"skill\": \"Perception\", \"ranks\": {perception} }}, {{ \"skill\": \"Intimidate\", \"ranks\": 1 }} ]",
            abilities ?? [10, 10, 10, 10, 10, 10]);

    [Fact]
    public void AlertnessIsTwoAndFourOnceTheSkillHasTenRanks()
    {
        var plain = With(string.Empty, 4);
        var alert = With("\"alertness\"", 4);
        var expert = With("\"alertness\"", 10);
        var expertPlain = With(string.Empty, 10);

        Assert.Equal(plain.Skills.Total(Skill.Perception) + 2, alert.Skills.Total(Skill.Perception));
        Assert.Equal(expertPlain.Skills.Total(Skill.Perception) + 4, expert.Skills.Total(Skill.Perception));
        Assert.Equal(plain.Skills.Total(Skill.SenseMotive) + 2, alert.Skills.Total(Skill.SenseMotive));
        Assert.Equal(plain.Skills.Total(Skill.Stealth), alert.Skills.Total(Skill.Stealth));
    }

    [Fact]
    public void SkillFocusIsThreeAndSixAndOnlyForItsSkill()
    {
        var plain = With(string.Empty, 4);
        var focused = With("\"skill-focus:Perception\"", 4);
        var master = With("\"skill-focus:Perception\"", 10);

        Assert.Equal(plain.Skills.Total(Skill.Perception) + 3, focused.Skills.Total(Skill.Perception));
        Assert.Equal(With(string.Empty, 10).Skills.Total(Skill.Perception) + 6, master.Skills.Total(Skill.Perception));
        Assert.Equal(plain.Skills.Total(Skill.Stealth), focused.Skills.Total(Skill.Stealth));
        Assert.Contains(focused.Skills.Explain(Skill.Perception).Entries, entry => entry.Modifier.Source == "Skill Focus (Perception)");
    }

    [Fact]
    public void FeatBonusesAreUntypedAndStack()
    {
        var both = With("\"alertness\", \"skill-focus:Perception\"", 4);

        Assert.Equal(With(string.Empty, 4).Skills.Total(Skill.Perception) + 5, both.Skills.Total(Skill.Perception));
    }

    [Fact]
    public void IntimidatingProwessAddsStrengthAsWellAsCharisma()
    {
        var bully = With("\"intimidating-prowess\"", abilities: [18, 10, 10, 10, 10, 14]);
        var plain = With(string.Empty, abilities: [18, 10, 10, 10, 10, 14]);

        Assert.Equal(plain.Skills.Total(Skill.Intimidate) + 4, bully.Skills.Total(Skill.Intimidate));
    }

    [Theory]
    [InlineData(Condition.Shaken)]
    [InlineData(Condition.Frightened)]
    [InlineData(Condition.Sickened)]
    public void FearAndSicknessCostTwoOnEverySkillAndEveryAbilityCheck(Condition condition)
    {
        var rogue = With(string.Empty, 4);
        var before = SkillInfo.All.ToDictionary(skill => skill, rogue.Skills.Total);
        var strength = rogue.AbilityCheck(Ability.Strength).Total;

        rogue.Effects.Apply(ConditionInfo.Effect(condition, Duration.Rounds(3)));

        Assert.All(SkillInfo.All, skill => Assert.Equal(before[skill] - 2, rogue.Skills.Total(skill)));
        Assert.Equal(strength - 2, rogue.AbilityCheck(Ability.Strength).Total);

        rogue.Effects.Remove(condition);
        Assert.All(SkillInfo.All, skill => Assert.Equal(before[skill], rogue.Skills.Total(skill)));
        Assert.Equal(strength, rogue.AbilityCheck(Ability.Strength).Total);
    }

    [Fact]
    public void BlindnessCostsFourOnTheSkillsOfStrengthAndDexterity()
    {
        var rogue = With(string.Empty, 4);
        var climb = rogue.Skills.Total(Skill.Climb);
        var stealth = rogue.Skills.Total(Skill.Stealth);
        var bluff = rogue.Skills.Total(Skill.Bluff);

        rogue.Effects.Apply(ConditionInfo.Effect(Condition.Blinded, Duration.Rounds(1)));

        Assert.Equal(climb - 4, rogue.Skills.Total(Skill.Climb));
        Assert.Equal(stealth - 4, rogue.Skills.Total(Skill.Stealth));
        Assert.Equal(bluff, rogue.Skills.Total(Skill.Bluff));
    }

    [Fact]
    public void TheNewSkillsRunOnTheBooksAbilities()
    {
        Assert.Equal(Ability.Dexterity, SkillInfo.AbilityFor(Skill.Fly));
        Assert.Equal(Ability.Dexterity, SkillInfo.AbilityFor(Skill.EscapeArtist));
        Assert.Equal(Ability.Dexterity, SkillInfo.AbilityFor(Skill.Ride));
        Assert.Equal(Ability.Dexterity, SkillInfo.AbilityFor(Skill.SleightOfHand));
        Assert.Equal(Ability.Charisma, SkillInfo.AbilityFor(Skill.Disguise));
        Assert.Equal(Ability.Charisma, SkillInfo.AbilityFor(Skill.HandleAnimal));
        Assert.Equal(Ability.Charisma, SkillInfo.AbilityFor(Skill.UseMagicDevice));
        Assert.True(SkillInfo.TrainedOnly(Skill.UseMagicDevice));
        Assert.True(SkillInfo.TrainedOnly(Skill.HandleAnimal));
        Assert.True(SkillInfo.TrainedOnly(Skill.SleightOfHand));
        Assert.False(SkillInfo.TrainedOnly(Skill.Fly));
        Assert.Equal("Use Magic Device", SkillInfo.Name(Skill.UseMagicDevice));
    }

    [Fact]
    public void AContentGrantCanAimAtOneSkillOrAtAllOfThem()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("keen.json", """
            { "kind": "feat", "id": "keen-eyed", "name": "Keen-Eyed", "description": "A test feat.",
              "grants": [
                { "target": "skill.Perception", "value": 1, "type": "Competence" },
                { "target": "skills", "value": 1, "type": "Luck" },
                { "target": "abilityChecks", "value": 2, "type": "Luck" } ] }
            """)));

        var keen = library.GetFeat("keen-eyed")!;
        var hero = ClassKit.Dummy();
        var perception = hero.Skills.Total(Skill.Perception);
        var climb = hero.Skills.Total(Skill.Climb);

        keen.ApplyTo(hero);

        Assert.Equal(perception + 2, hero.Skills.Total(Skill.Perception));
        Assert.Equal(climb + 1, hero.Skills.Total(Skill.Climb));
        Assert.Equal(2, hero.AbilityCheck(Ability.Wisdom).Total);
        Assert.Equal("skill.Perception", ModifierTarget.Skill(Skill.Perception).ToString());
    }

    [Fact]
    public void ArmourCheckPenaltiesAndRacialBonusesStackWithTheRestInOnePass()
    {
        var sylwen = TestContent.Library.BuildCreature("sylwen")!;
        var breakdown = sylwen.Skills.Explain(Skill.Perception);

        // The elf's two, as a racial bonus, alongside the ranks and Wisdom.
        Assert.Contains(breakdown.Entries, entry => entry.Modifier.Type == BonusType.Racial && entry.Modifier.Value == 2);
    }

    [Fact]
    public void TheCheckStacksSurviveASave()
    {
        var rogue = With(string.Empty, 4);
        rogue.Effects.Apply(ConditionInfo.Effect(Condition.Shaken, Duration.Rounds(5)));
        var encounter = new Encounter([rogue], new SequenceRandom(true, 10));

        var back = GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single().Creature;

        Assert.Equal(rogue.Skills.Total(Skill.Stealth), back.Skills.Total(Skill.Stealth));
        Assert.Equal(rogue.AbilityCheck(Ability.Strength).Total, back.AbilityCheck(Ability.Strength).Total);

        // And the shaken, when it ends, takes its penalties with it.
        back.Effects.Remove(Condition.Shaken);
        Assert.Equal(With(string.Empty, 4).Skills.Total(Skill.Stealth), back.Skills.Total(Skill.Stealth));
    }
}
