using System.Text.Json.Nodes;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Tests.Classes;

/// <summary>The Core Rulebook's medium track all the way up.</summary>
public class MediumTrackTests
{
    [Theory]
    [InlineData(11, 155_000)]
    [InlineData(12, 220_000)]
    [InlineData(13, 315_000)]
    [InlineData(14, 445_000)]
    [InlineData(15, 635_000)]
    [InlineData(16, 890_000)]
    [InlineData(17, 1_300_000)]
    [InlineData(18, 1_800_000)]
    [InlineData(19, 2_550_000)]
    [InlineData(20, 3_600_000)]
    [InlineData(10, 105_000)]
    [InlineData(2, 2_000)]
    public void TheThresholdsAreTheBooks(int level, int experience) =>
        Assert.Equal(experience, Levelling.ThresholdFor(level));

    [Theory]
    [InlineData(4, true)]
    [InlineData(8, true)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    [InlineData(20, true)]
    [InlineData(5, false)]
    [InlineData(1, false)]
    public void AbilityScoresRiseEveryFourthLevel(int level, bool rises) =>
        Assert.Equal(rises, Levelling.GrantsAbilityIncreaseAt(level));
}

/// <summary>Challenge ratings, and what beating something is worth by them.</summary>
public class ChallengeRatingTests
{
    [Theory]
    [InlineData("1/8", 50)]
    [InlineData("1/6", 65)]
    [InlineData("1/4", 100)]
    [InlineData("1/3", 135)]
    [InlineData("1/2", 200)]
    [InlineData("1", 400)]
    [InlineData("2", 600)]
    [InlineData("3", 800)]
    [InlineData("4", 1_200)]
    [InlineData("5", 1_600)]
    [InlineData("6", 2_400)]
    [InlineData("7", 3_200)]
    [InlineData("8", 4_800)]
    [InlineData("9", 6_400)]
    [InlineData("10", 9_600)]
    [InlineData("13", 25_600)]
    [InlineData("20", 307_200)]
    [InlineData("25", 1_638_400)]
    public void ExperienceFollowsTheBestiarysTable(string written, int experience)
    {
        Assert.True(ChallengeRating.TryParse(written, out var rating));
        Assert.Equal(experience, rating.Experience);
        Assert.Equal(written, rating.ToString());
    }

    [Theory]
    [InlineData("1/5")]
    [InlineData("0")]
    [InlineData("two")]
    [InlineData("")]
    public void NothingElseIsAChallengeRating(string written) =>
        Assert.False(ChallengeRating.TryParse(written, out _));

    [Theory]
    [InlineData("warrior", 1, "1/3")]
    [InlineData("warrior", 2, "1/2")]
    [InlineData("warrior", 3, "1")]
    [InlineData("warrior", 8, "6")]
    [InlineData("fighter", 1, "1/2")]
    [InlineData("fighter", 2, "1")]
    [InlineData("fighter", 6, "5")]
    [InlineData("wizard", 5, "4")]
    public void ClassLevelsAreRatedAsTheBestiaryRatesThem(string classId, int level, string expected) =>
        Assert.Equal(expected, ClassKit.Make(classId, level).Challenge.ToString());

    [Fact]
    public void TheShippedCreaturesAreRatedAsThePlanSays()
    {
        var expected = new Dictionary<string, string>
        {
            ["goblin"] = "1/3",
            ["goblin-archer"] = "1/3",
            ["orc"] = "1/3",
            ["orc-sentry"] = "1/3",
            ["dire-rat"] = "1/3",
            ["hobgoblin-sergeant"] = "5",
            ["werewolf"] = "2",
            ["ogre"] = "3",
        };

        foreach (var (id, rating) in expected)
        {
            Assert.Equal((id, rating), (id, TestContent.Library.BuildCreature(id)!.Challenge.ToString()));
        }
    }

    [Fact]
    public void EveryShippedCreatureWritesItsRating()
    {
        foreach (var id in TestContent.Library.CreatureIds)
        {
            Assert.NotNull(TestContent.Library.GetCreature(id)!.ChallengeRating);
        }
    }

    [Fact]
    public void AFileThatDoesNotWriteOneIsRatedByItsLevels()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("hob.json", """
            { "kind": "creature", "id": "hob", "name": "Hob", "abilities": [13, 13, 12, 10, 10, 8],
              "classes": [ { "class": "fighter", "level": 1 } ] }
            """)));

        Assert.Empty(library.Problems);
        Assert.Null(library.GetCreature("hob")!.ChallengeRating);
        Assert.Equal("1/2", library.BuildCreature("hob")!.Challenge.ToString());
    }

    [Fact]
    public void ABadRatingIsReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("odd.json", """
            { "kind": "creature", "id": "odd", "name": "Odd", "cr": "1/5", "abilities": [10, 10, 10, 10, 10, 10] }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "cr");
    }

    [Fact]
    public void AnAwardIsTheRatingsExperienceAndAShareIsItDividedByTheParty()
    {
        var goblin = TestContent.Library.BuildCreature("goblin")!;
        var ogre = TestContent.Library.BuildCreature("ogre")!;

        Assert.Equal(135, Levelling.Award(goblin));
        Assert.Equal(800, Levelling.Award(ogre));
        Assert.Equal(233, Levelling.Share(800 + 135, 4));
        Assert.Equal(135, Levelling.Share(135, 1));
    }

    [Fact]
    public void TheRatingComesBackFromTheFileAfterASave()
    {
        var werewolf = TestContent.Library.BuildCreature("werewolf")!;
        var encounter = new Encounter([werewolf], new SequenceRandom(true, 10));

        var back = GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single().Creature;

        Assert.Equal("2", back.Challenge.ToString());
    }
}

/// <summary>The ability increase and the favoured-class bonus, as choices a level makes.</summary>
public class LevelChoiceRulesTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void TheFourthLevelRaisesTheClassesKeyAbilityByDefault()
    {
        var fighter = ClassKit.Make("fighter", 3);
        var taken = Library.GetClass("fighter")!;
        var needs = ClassLevelling.NeedsFor(fighter, taken, Library);

        Assert.True(needs.AbilityIncrease);
        Assert.Equal(Ability.Strength, needs.DefaultAbility);

        var (choices, refusal) = ClassLevelling.Resolve(fighter, taken, new LevelChoices(), Library);
        Assert.Null(refusal);
        Assert.Equal(Ability.Strength, choices!.AbilityIncrease);

        var strength = fighter.Abilities[Ability.Strength].Base;
        Levelling.Gain(fighter, taken);
        ClassLevelling.Apply(fighter, choices, Library);

        Assert.Equal(strength + 1, fighter.Abilities[Ability.Strength].Base);
    }

    [Theory]
    [InlineData("rogue", Ability.Dexterity)]
    [InlineData("cleric", Ability.Wisdom)]
    [InlineData("wizard", Ability.Intelligence)]
    [InlineData("barbarian", Ability.Strength)]
    public void EachClassHasAKeyAbility(string classId, Ability key) =>
        Assert.Equal(key, Library.GetClass(classId)!.KeyAbility);

    [Fact]
    public void AnyAbilityCanBeChosenInstead()
    {
        var wizard = ClassKit.Make("wizard", 3);
        var taken = Library.GetClass("wizard")!;

        var (choices, _) = ClassLevelling.Resolve(wizard, taken, new LevelChoices(AbilityIncrease: Ability.Constitution), Library);
        var hitPoints = wizard.HitPoints.Maximum;
        Levelling.Gain(wizard, taken);
        ClassLevelling.Apply(wizard, choices!, Library);

        Assert.Equal(15, wizard.Abilities[Ability.Constitution].Base);
        Assert.True(wizard.HitPoints.Maximum > hitPoints);
    }

    [Fact]
    public void AnIncreaseOnALevelThatGrantsNoneIsRefused()
    {
        var wizard = ClassKit.Make("wizard", 4);

        var (choices, refusal) = ClassLevelling.Resolve(
            wizard, Library.GetClass("wizard")!, new LevelChoices(AbilityIncrease: Ability.Intelligence), Library);

        Assert.Null(choices);
        Assert.Contains("raises no ability score", refusal);
    }

    [Fact]
    public void ARaisedCastingScoreBringsItsBonusSpell()
    {
        // Intelligence 19 to 20 at fourth level: a fifth modifier point, and a second bonus
        // 1st-level slot, as Table 1-3 has it.
        var wizard = ClassKit.Make("wizard", 3, abilities: [10, 10, 10, 19, 10, 10]);
        var taken = Library.GetClass("wizard")!;
        var (choices, _) = ClassLevelling.Resolve(wizard, taken, new LevelChoices(), Library);

        Levelling.Gain(wizard, taken);
        var before = wizard.Spells.SlotsMaximum(1);
        ClassLevelling.Apply(wizard, choices!, Library);

        Assert.Equal(before + 1, wizard.Spells.SlotsMaximum(1));
    }

    [Fact]
    public void ALevelInTheFavouredClassIsAHitPointByDefault()
    {
        var fighter = ClassKit.Make("fighter", 1);
        var taken = Library.GetClass("fighter")!;
        var needs = ClassLevelling.NeedsFor(fighter, taken, Library);

        Assert.True(needs.FavouredClass);
        Assert.True(needs.AnyChoice);

        var (choices, _) = ClassLevelling.Resolve(fighter, taken, new LevelChoices(), Library);
        Assert.Equal(FavouredClassBonus.HitPoint, choices!.Favoured);

        var plain = ClassKit.Make("fighter", 1);
        Levelling.Gain(plain, taken);
        Levelling.Gain(fighter, taken);
        ClassLevelling.Apply(fighter, choices, Library);

        Assert.Equal(plain.HitPoints.Maximum + 1, fighter.HitPoints.Maximum);
    }

    [Fact]
    public void OrASkillRankWhereverItIsWanted()
    {
        var rogue = ClassKit.Make("rogue", 1, "\"skills\": [ { \"skill\": \"Stealth\", \"ranks\": 1 } ]");
        var taken = Library.GetClass("rogue")!;

        var (choices, refusal) = ClassLevelling.Resolve(
            rogue, taken, new LevelChoices(Favoured: FavouredClassBonus.SkillRank, FavouredSkill: Skill.Stealth), Library);

        Assert.Null(refusal);
        Levelling.Gain(rogue, taken);
        ClassLevelling.Apply(rogue, choices!, Library);

        Assert.Equal(2, rogue.Skills.Ranks(Skill.Stealth));
    }

    [Fact]
    public void ASkillRankWithNoSkillNamedGoesWhereTheClassHasPutMost()
    {
        var rogue = ClassKit.Make("rogue", 1, "\"skills\": [ { \"skill\": \"Acrobatics\", \"ranks\": 1 } ]");
        var (choices, _) = ClassLevelling.Resolve(
            rogue, Library.GetClass("rogue")!, new LevelChoices(Favoured: FavouredClassBonus.SkillRank), Library);

        Assert.Equal(Skill.Acrobatics, choices!.FavouredSkill);
    }

    [Fact]
    public void ASkillAlreadyAtItsLimitIsRefused()
    {
        var rogue = ClassKit.Make("rogue", 1, "\"skills\": [ { \"skill\": \"Stealth\", \"ranks\": 2 } ]");

        var (choices, refusal) = ClassLevelling.Resolve(
            rogue, Library.GetClass("rogue")!, new LevelChoices(Favoured: FavouredClassBonus.SkillRank, FavouredSkill: Skill.Stealth), Library);

        Assert.Null(choices);
        Assert.Contains("Stealth", refusal);
    }

    [Fact]
    public void ALevelInAnotherClassIsNotFavoured()
    {
        var wizard = ClassKit.Make("wizard", 2);
        var fighter = Library.GetClass("fighter")!;

        Assert.False(ClassLevelling.NeedsFor(wizard, fighter, Library).FavouredClass);

        var (choices, refusal) = ClassLevelling.Resolve(wizard, fighter, new LevelChoices(Favoured: FavouredClassBonus.HitPoint), Library);
        Assert.Null(choices);
        Assert.Contains("favoured class", refusal);
    }

    [Fact]
    public void AFileCanFavourAClassOtherThanItsFirst()
    {
        var hero = ClassKit.Make("wizard", 2, "\"favouredClass\": \"wizard\"");

        Assert.Equal("wizard", hero.FavouredClass);
        Assert.True(ClassLevelling.IsFavoured(hero, Library.GetClass("wizard")!));
    }

    [Fact]
    public void AFavouredClassThatIsNotOneOfItsClassesIsReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("odd.json", """
            { "kind": "creature", "id": "odd", "name": "Odd", "abilities": [10, 10, 10, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 1 } ], "favouredClass": "wizard" }
            """)));

        Assert.Contains(library.Problems, problem => problem.Field == "favouredClass");
    }
}

/// <summary>Version fifteen: what it added, and the fourteens it still reads.</summary>
public class VersionFifteenTests
{
    [Fact]
    public void TheMovementFeatsStateOnACombatantComesBack()
    {
        // A run through a patch of difficult ground, saved before the turn ends.
        var runner = ClassKit.Make("rogue", 1, "\"feats\": [\"nimble-moves\"]", [10, 16, 10, 10, 10, 10]);
        var field = new Ironbound.Rules.Maps.Battlefield(20, 3);
        field.Place(runner, 0, 1);
        field.MakeDifficult(new Ironbound.Rules.Maps.GridSquare(1, 1));
        var encounter = new Encounter([runner], new SequenceRandom(true, 10), battlefield: field);
        var turn = encounter.BeginNextTurn()!;

        Assert.NotNull(turn.Take(new Ironbound.Rules.Encounters.Actions.RunAction(
            [.. Enumerable.Range(0, 8).Select(x => new Ironbound.Rules.Maps.GridSquare(x, 1))])));

        var back = GameSave.Restore(GameSave.FromJson(GameSave.ToJson(GameSave.Capture(encounter))), TestContent.Library)
            .Order.Single();

        Assert.True(back.IsRunning);
        Assert.Equal(5, back.EasyGroundUsed);
        Assert.True(back.IsFlatFooted);
    }

    [Fact]
    public void AFourteenStillLoads()
    {
        var valeria = TestContent.Library.BuildCreature("valeria")!;
        var encounter = new Encounter([valeria], new SequenceRandom(true, 10));
        var json = JsonNode.Parse(GameSave.ToJson(GameSave.Capture(encounter)))!.AsObject();

        json["Version"] = 14;
        var creature = json["Creatures"]![0]!.AsObject();
        creature.Remove("SkillChecks");
        creature.Remove("AbilityChecks");
        json["Order"]![0]!.AsObject().Remove("SteppedUp");

        var back = GameSave.Restore(GameSave.FromJson(json.ToJsonString()), TestContent.Library).Order.Single();

        Assert.Equal(valeria.ArmorClass.Total, back.Creature.ArmorClass.Total);
        Assert.Empty(back.Creature.Skills.Checks);
        Assert.False(back.SteppedUp);
        Assert.True(back.Creature.HasFeat("greater-trip"));
    }
}
