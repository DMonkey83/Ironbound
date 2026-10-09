using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;

namespace Ironbound.Rules.Tests.Classes;

public class ClassFeatureTableTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(7, 4)]
    [InlineData(9, 5)]
    [InlineData(10, 5)]
    public void SneakAttackIsADieEveryOddLevel(int level, int dice) =>
        Assert.Equal(dice, SneakAttack.Dice(ClassKit.Make("rogue", level)));

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    [InlineData(10, 3)]
    public void BraveryIsOneAtSecondAndOneMoreEveryFour(int level, int bravery) =>
        Assert.Equal(bravery, Martial.Bravery(ClassKit.Make("fighter", level)));

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 2)]
    public void ArmourTrainingStepsAtThirdAndSeventh(int level, int steps) =>
        Assert.Equal(steps, Martial.ArmorTraining(ClassKit.Make("fighter", level)));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(9, 5)]
    public void ChannelEnergyGainsADieEveryOddLevel(int level, int dice) =>
        Assert.Equal(dice, ClassPowers.ChannelDice(ClassKit.Make("cleric", level, "\"deity\": \"none\"")));

    [Theory]
    [InlineData("rogue", 2, 0)]
    [InlineData("rogue", 3, 1)]
    [InlineData("rogue", 6, 2)]
    [InlineData("rogue", 9, 3)]
    [InlineData("barbarian", 3, 1)]
    [InlineData("barbarian", 6, 2)]
    public void TrapSenseIsRecordedAndCounted(string classId, int level, int sense) =>
        Assert.Equal(sense, RogueDefences.TrapSense(ClassKit.Make(classId, level)));

    [Fact]
    public void TrapSenseFromTwoClassesStacks()
    {
        var both = ClassKit.Make("rogue", 3);
        both.Levels.Add(new ClassLevel(TestContent.Library.GetClass("barbarian")!, 3));

        Assert.Equal(2, RogueDefences.TrapSense(both));
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(10, 2)]
    public void BarbarianDamageReductionArrivesAtSevenAndTen(int level, int amount)
    {
        var barbarian = ClassKit.Make("barbarian", level);

        Assert.Equal(amount, barbarian.Defenses.ReductionAgainst(Ironbound.Rules.Defense.DamageBypass.Slashing | Ironbound.Rules.Defense.DamageBypass.Magic));
    }

    [Fact]
    public void AClassWithoutTheFeatureHasNoneOfIt()
    {
        var wizard = ClassKit.Make("wizard", 10);

        Assert.Equal(0, SneakAttack.Dice(wizard));
        Assert.Equal(0, Martial.Bravery(wizard));
        Assert.Equal(0, Rage.RoundsPerDay(wizard));
        Assert.Null(ClassPowers.ChannelKindOf(wizard));
        Assert.False(UncannyDodge.Has(wizard));
    }

    [Fact]
    public void TheWarriorStillHasNoFeaturesAtAll()
    {
        // The NPC class: everything a fighter has except the reason to be interesting.
        var warrior = TestContent.Library.GetClass("warrior")!;

        Assert.Empty(warrior.Features);
        Assert.Empty(ClassFeatures.Describe(TestContent.Library.BuildCreature("goblin")!));
    }

    [Theory]
    [InlineData("fighter", new[] { 1, 2, 4, 6, 8, 10 })]
    [InlineData("wizard", new[] { 5, 10 })]
    public void BonusFeatsComeWhereTheTableSays(string classId, int[] levels)
    {
        var table = TestContent.Library.GetClass(classId)!;

        Assert.Equal(levels, table.Features.Where(row => row.Id == FeatureIds.BonusFeat).Select(row => row.Level));
    }

    [Fact]
    public void EveryShippedClassRowIsAFeatureTheCodeKnows()
    {
        foreach (var id in TestContent.Library.ClassIds)
        {
            foreach (var row in TestContent.Library.GetClass(id)!.Features)
            {
                Assert.True(FeatureIds.Known.ContainsKey(row.Id), $"{id}: {row.Id}");
            }
        }
    }

    [Fact]
    public void AnUnknownFeatureIsReportedWithTheClassItIsIn()
    {
        var library = ContentLibrary.Load([
            ("monk.json", """
                { "kind": "class", "id": "monk", "name": "Monk",
                  "features": [ { "level": 1, "id": "flurry-of-blows" } ] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("class 'monk'", problem.Source);
        Assert.Contains("flurry-of-blows", problem.Message);
    }

    [Fact]
    public void AParameterAFeatureDoesNotTakeIsReported()
    {
        var library = ContentLibrary.Load([
            ("odd.json", """
                { "kind": "class", "id": "odd", "name": "Odd",
                  "features": [
                    { "level": 1, "id": "sneak-attack", "dice": "2" },
                    { "level": 2, "id": "talent", "list": "spells" }
                  ] }
                """),
        ]);

        Assert.Equal(2, library.Problems.Count);
        Assert.Contains(library.Problems, problem => problem.Message.Contains("takes no 'dice'"));
        Assert.Contains(library.Problems, problem => problem.Message.Contains("'spells'"));
    }

    [Fact]
    public void FeatureRowsKeepTheirParameters()
    {
        var rogue = TestContent.Library.GetClass("rogue")!;
        var talent = rogue.Features.First(row => row.Id == FeatureIds.Talent);

        Assert.Equal(2, talent.Level);
        Assert.Equal(FeatureIds.RogueTalents, talent.Parameter("list"));
        Assert.Equal("fallback", talent.Parameter("missing", "fallback"));
    }

    [Fact]
    public void LevelOfCountsOnlyTheClassesThatGrantAFeature()
    {
        var multi = ClassKit.Make("rogue", 3);
        multi.Levels.Add(new ClassLevel(TestContent.Library.GetClass("fighter")!, 2));

        Assert.Equal(3, ClassFeatures.LevelOf(multi, FeatureIds.SneakAttack));
        Assert.Equal(2, ClassFeatures.LevelOf(multi, FeatureIds.Bravery));
        Assert.Equal(0, ClassFeatures.LevelOf(multi, FeatureIds.Rage));
        Assert.Equal(3, ClassFeatures.ClassLevel(multi, "rogue"));
    }

    [Fact]
    public void TheClericTableNoLongerCountsTheDomainSlot()
    {
        var cleric = TestContent.Library.GetClass("cleric")!;

        // The wizard's numbers: the domain slot is kept apart now, not folded in.
        Assert.Equal([1], cleric.SlotsAt(1));
        Assert.Equal([2, 1], cleric.SlotsAt(3));
    }
}

public class ClassChoiceValidationTests
{
    private static IReadOnlyList<Ironbound.Rules.Content.ContentProblem> Problems(string members) =>
        ClassKit.Library(("chooser.json", $$"""
            { "kind": "creature", "id": "chooser", "name": "Chooser", "abilities": [10, 10, 10, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 6 } ], {{members}} }
            """)).Problems;

    [Fact]
    public void AFightersOwnChoicesAreFine() =>
        Assert.Empty(Problems("\"weaponTraining\": [\"heavy-blades\"]"));

    [Fact]
    public void ARagePowerOnAFighterIsReported() =>
        Assert.Contains(Problems("\"talents\": [\"powerful-blow\"]"), problem => problem.Message.Contains("rage-power"));

    [Fact]
    public void AnUnknownTalentIsReported() =>
        Assert.Contains(Problems("\"talents\": [\"flurry\"]"), problem => problem.Message.Contains("no talent called 'flurry'"));

    [Fact]
    public void DomainsOrASchoolOnAFighterAreReported()
    {
        Assert.Contains(Problems("\"domains\": [\"war\"]"), problem => problem.Field == "domains");
        Assert.Contains(
            Problems("\"school\": \"evocation\", \"opposition\": [\"Illusion\", \"Enchantment\"]"),
            problem => problem.Field == "school");
    }

    [Fact]
    public void AWeaponGroupThatDoesNotExistIsReported() =>
        Assert.Contains(Problems("\"weaponTraining\": [\"lightsabres\"]"), problem => problem.Message.Contains("lightsabres"));

    [Fact]
    public void WeaponTrainingOnAClassWithoutItIsReported()
    {
        var library = ClassKit.Library(("wizardly.json", """
            { "kind": "creature", "id": "wizardly", "name": "Wizardly", "abilities": [10, 10, 10, 10, 10, 10],
              "classes": [ { "class": "wizard", "level": 6 } ], "weaponTraining": ["bows"] }
            """));

        Assert.Contains(library.Problems, problem => problem.Field == "weaponTraining");
    }

    [Fact]
    public void AWeaponFileNamingAGroupThatDoesNotExistIsReported()
    {
        var library = Ironbound.Rules.Content.ContentLibrary.Load([
            ("odd.json", """{ "kind": "weapon", "id": "odd", "name": "odd", "groups": ["sporks"] }"""),
        ]);

        Assert.Contains(library.Problems, problem => problem.Message.Contains("sporks"));
    }

    [Fact]
    public void ATalentWithoutAnEffectOrWithAStrangeListIsReported()
    {
        var library = Ironbound.Rules.Content.ContentLibrary.Load([
            ("odd.json", """{ "kind": "talent", "id": "odd", "name": "Odd", "list": "spells" }"""),
        ]);

        Assert.Contains(library.Problems, problem => problem.Field == "effect");
        Assert.Contains(library.Problems, problem => problem.Field == "list");
    }

    [Fact]
    public void ADomainSpellThatDoesNotExistIsReported()
    {
        var library = Ironbound.Rules.Content.ContentLibrary.Load([
            ("odd.json", """{ "kind": "domain", "id": "odd", "name": "Odd", "spells": ["wish"], "powers": [ { "level": 1 } ] }"""),
        ]);

        Assert.Contains(library.Problems, problem => problem.Message.Contains("'wish'"));
        Assert.Contains(library.Problems, problem => problem.Message.Contains("'effect'"));
    }

    [Fact]
    public void EveryShippedTalentDomainAndSchoolLoadsCleanly()
    {
        Assert.Empty(TestContent.Library.Problems);
        Assert.Equal(19, TestContent.Library.TalentIds.Count);
        Assert.Equal(["healing", "war"], TestContent.Library.DomainIds.Order());
        Assert.Equal(["conjuration", "evocation", "universalist"], TestContent.Library.SchoolIds.Order());
    }
}
