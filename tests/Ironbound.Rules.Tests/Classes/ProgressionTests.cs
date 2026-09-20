using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Tests.Content;
using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Tests.Classes;

public class ClassDefinitionTests
{
    [Theory]
    [InlineData(AttackProgression.Full, 1, 1)]
    [InlineData(AttackProgression.Full, 6, 6)]
    [InlineData(AttackProgression.Full, 20, 20)]
    [InlineData(AttackProgression.ThreeQuarters, 1, 0)]
    [InlineData(AttackProgression.ThreeQuarters, 4, 3)]
    [InlineData(AttackProgression.ThreeQuarters, 8, 6)]
    [InlineData(AttackProgression.ThreeQuarters, 20, 15)]
    [InlineData(AttackProgression.Half, 1, 0)]
    [InlineData(AttackProgression.Half, 2, 1)]
    [InlineData(AttackProgression.Half, 11, 5)]
    [InlineData(AttackProgression.Half, 20, 10)]
    public void BaseAttackFollowsTheProgression(AttackProgression progression, int level, int bonus) =>
        Assert.Equal(bonus, Class("x", attack: progression).BaseAttackAt(level));

    [Fact]
    public void AGoodSaveRisesFasterThanAPoorOne()
    {
        var fighter = Class("fighter", good: [Save.Fortitude]);

        Assert.Equal(5, fighter.SaveAt(Save.Fortitude, 6));
        Assert.Equal(2, fighter.SaveAt(Save.Reflex, 6));
        Assert.Equal(2, fighter.SaveAt(Save.Will, 6));
    }

    [Theory]
    [InlineData(CasterProgression.None, 8, 0)]
    [InlineData(CasterProgression.Full, 8, 8)]
    [InlineData(CasterProgression.Half, 8, 4)]
    public void CasterLevelFollowsItsOwnProgression(
        CasterProgression casting, int level, int casterLevel) =>
        Assert.Equal(casterLevel, Class("x", casting: casting).CasterLevelAt(level));

    internal static ClassDefinition Class(
        string id,
        int hitDie = 8,
        AttackProgression attack = AttackProgression.ThreeQuarters,
        Save[]? good = null,
        CasterProgression casting = CasterProgression.None,
        Ability ability = Ability.Intelligence) =>
        new()
        {
            Id = id,
            Name = id,
            HitDie = hitDie,
            Attack = attack,
            GoodSaves = good ?? [],
            Casting = casting,
            CastingAbility = ability,
        };
}

public class MulticlassTests
{
    private static readonly ClassDefinition Fighter = ClassDefinitionTests.Class(
        "fighter", hitDie: 10, attack: AttackProgression.Full, good: [Save.Fortitude]);

    private static readonly ClassDefinition Wizard = ClassDefinitionTests.Class(
        "wizard", hitDie: 6, attack: AttackProgression.Half, good: [Save.Will],
        casting: CasterProgression.Full);

    private static readonly ClassDefinition Rogue = ClassDefinitionTests.Class(
        "rogue", attack: AttackProgression.ThreeQuarters, good: [Save.Reflex]);

    [Fact]
    public void BaseAttackAddsUpAcrossClasses()
    {
        // Each class contributes at its own level, which is why a level 6 multiclass hits worse
        // than a level 6 fighter — and exactly why the single-class fighter is worth being.
        Assert.Equal(6, Progression.BaseAttack([new ClassLevel(Fighter, 6)]));
        Assert.Equal(4, Progression.BaseAttack([new ClassLevel(Fighter, 3), new ClassLevel(Wizard, 3)]));
    }

    [Fact]
    public void SavesAddUpToo()
    {
        var dip = new[] { new ClassLevel(Wizard, 5), new ClassLevel(Fighter, 1) };

        // The famous one-level dip: a single fighter level buys +2 Fortitude outright.
        Assert.Equal(4, Progression.SaveBase(Save.Will, dip));
        Assert.Equal(3, Progression.SaveBase(Save.Fortitude, dip));   // Poor(5) 1 + Good(1) 2
    }

    [Fact]
    public void TotalLevelIsTheSumAndCasterLevelIsNot()
    {
        var levels = new[] { new ClassLevel(Wizard, 3), new ClassLevel(Rogue, 3) };

        Assert.Equal(6, Progression.TotalLevel(levels));

        // A wizard 3 / rogue 3 casts as a third-level wizard, not a sixth-level anything.
        Assert.Equal(3, Progression.CasterLevel(levels));
        Assert.Equal(Wizard, Progression.Caster(levels)!.Value.Class);
    }

    [Fact]
    public void SomethingWithNoMagicHasNoCasterLevel()
    {
        Assert.Equal(0, Progression.CasterLevel([new ClassLevel(Fighter, 20)]));
        Assert.Null(Progression.Caster([new ClassLevel(Fighter, 20)]));
    }

    [Theory]
    [InlineData(10, 1, 10)]      // the first level is always the full die
    [InlineData(10, 2, 16)]
    [InlineData(10, 6, 40)]
    [InlineData(6, 5, 22)]
    public void TheFirstLevelIsMaximumAndTheRestAreAverage(int hitDie, int level, int expected) =>
        Assert.Equal(expected, Progression.HitPointsBase(
            [new ClassLevel(ClassDefinitionTests.Class("x", hitDie), level)]));

    [Fact]
    public void OnlyTheVeryFirstLevelOfACareerIsMaximum()
    {
        // Fighter 1 then wizard 1: d10 in full, then a d6 at average. Not two full dice.
        Assert.Equal(14, Progression.HitPointsBase(
            [new ClassLevel(Fighter, 1), new ClassLevel(Wizard, 1)]));
    }

    [Fact]
    public void MaximumGenerationGivesEveryLevelItsWholeDie() =>
        Assert.Equal(60, Progression.HitPointsBase(
            [new ClassLevel(Fighter, 6)],
            RuleOptions.Pathfinder with { HitPointGeneration = HitPointGeneration.Maximum }));

    [Fact]
    public void RollingIsTreatedAsAverageWhenNothingIsThereToRollWith() =>
        Assert.Equal(
            Progression.HitPointsBase([new ClassLevel(Fighter, 6)]),
            Progression.HitPointsBase(
                [new ClassLevel(Fighter, 6)],
                RuleOptions.Pathfinder with { HitPointGeneration = HitPointGeneration.Rolled }));
}

public class ClassContentTests
{
    [Fact]
    public void AClassIsReadFromItsFile()
    {
        var library = ContentParsingTests.From("""
            { "kind": "class", "id": "wizard", "name": "Wizard", "hitDie": 6,
              "attack": "Half", "goodSaves": ["Will"],
              "casting": "Full", "castingAbility": "Intelligence" }
            """);

        var wizard = Assert.IsType<ClassDefinition>(library.GetClass("wizard"));

        Assert.Equal("Wizard", wizard.Name);
        Assert.Equal(6, wizard.HitDie);
        Assert.Equal(AttackProgression.Half, wizard.Attack);
        Assert.Equal([Save.Will], wizard.GoodSaves);
        Assert.Equal(CasterProgression.Full, wizard.Casting);
    }

    [Fact]
    public void ACreatureCitingAClassThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("hero.json", """
                { "kind": "creature", "id": "hero", "name": "Hero",
                  "abilities": [10, 10, 10, 10, 10, 10],
                  "classes": [ { "class": "paladin", "level": 3 } ] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("classes", problem.Field);
        Assert.Contains("paladin", problem.Message);
    }

    [Fact]
    public void SayingFighterSixSaysEverythingElseToo()
    {
        var library = ContentParsingTests.From(
            """
            { "kind": "class", "id": "fighter", "name": "Fighter", "hitDie": 10,
              "attack": "Full", "goodSaves": ["Fortitude"] }
            """,
            """
            { "kind": "creature", "id": "hero", "name": "Hero",
              "abilities": [16, 12, 14, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 6 } ] }
            """);

        var hero = library.BuildCreature("hero")!;

        Assert.Equal(6, hero.BaseAttackBonus);
        Assert.Equal(2, hero.AttacksPerFullAttack);
        Assert.Equal(52, hero.HitPoints.Maximum);   // 40 from the dice, +2 a level for Con 14
        Assert.Equal(5, hero.Saves[Save.Fortitude].Base);
        Assert.Equal(2, hero.Saves[Save.Reflex].Base);
    }

    [Fact]
    public void WrittenDownHitPointsStillWinOverTheDice()
    {
        var library = ContentParsingTests.From(
            """
            { "kind": "class", "id": "fighter", "hitDie": 10, "attack": "Full" }
            """,
            """
            { "kind": "creature", "id": "boss", "name": "Boss",
              "abilities": [10, 10, 10, 10, 10, 10], "hitPoints": 300,
              "classes": [ { "class": "fighter", "level": 6 } ] }
            """);

        // A named enemy is allowed to be exactly as tough as the encounter needs.
        Assert.Equal(300, library.BuildCreature("boss")!.HitPoints.Maximum);
        Assert.Equal(6, library.BuildCreature("boss")!.BaseAttackBonus);
    }

    [Fact]
    public void ACreatureWithNoClassesStillWorksFromRawNumbers()
    {
        var library = ContentParsingTests.From("""
            { "kind": "creature", "id": "ooze", "name": "Ooze",
              "abilities": [10, 1, 10, 10, 10, 1], "hitPoints": 30, "hitDice": 4,
              "level": 4, "baseAttack": 3, "goodSave": "Fortitude" }
            """);

        var ooze = library.BuildCreature("ooze")!;

        // Monsters are not obliged to have classes, and the old shape still loads.
        Assert.Equal(3, ooze.BaseAttackBonus);
        Assert.Equal(4, ooze.Saves[Save.Fortitude].Base);
    }

    [Fact]
    public void AWizardsCasterLevelAndAbilityComeFromTheClass()
    {
        var library = ContentParsingTests.From(
            """
            { "kind": "class", "id": "wizard", "hitDie": 6, "attack": "Half",
              "goodSaves": ["Will"], "casting": "Full", "castingAbility": "Intelligence" }
            """,
            """
            { "kind": "creature", "id": "mage", "name": "Mage",
              "abilities": [8, 14, 12, 18, 13, 10],
              "classes": [ { "class": "wizard", "level": 5 } ] }
            """);

        var mage = library.BuildCreature("mage")!;

        Assert.Equal(5, mage.Spells.CasterLevel);
        Assert.Equal(Ability.Intelligence, mage.Spells.CastingAbility);
        Assert.Equal(2, mage.BaseAttackBonus);
        Assert.Equal(4, mage.Saves[Save.Will].Base);
    }
}

public class ShippedClassTests
{
    [Theory]
    [InlineData("fighter")]
    [InlineData("barbarian")]
    [InlineData("wizard")]
    [InlineData("rogue")]
    [InlineData("warrior")]
    public void EveryClassTheContentNeedsIsThere(string id) =>
        Assert.NotNull(TestContent.Library.GetClass(id));

    [Fact]
    public void TheWholeCastIsBuiltFromClassesNow()
    {
        foreach (var id in new[]
        {
            "valeria", "karn", "merrin", "goblin", "goblin-archer", "hobgoblin-sergeant",
        })
        {
            Assert.NotEmpty(TestContent.Library.GetCreature(id)!.Classes);
        }
    }

    [Fact]
    public void MerrinIsAWizardAndFinallyHasAWizardsSaves()
    {
        var merrin = TestContent.Library.BuildCreature("merrin")!;

        // The hand-written numbers had her good at Fortitude, because the helper that built her
        // only knew how to make warriors. A wizard resists spells, not poison.
        Assert.Equal(4, merrin.Saves[Save.Will].Base);
        Assert.Equal(1, merrin.Saves[Save.Fortitude].Base);
        Assert.Equal(5, merrin.Spells.CasterLevel);
    }

    [Fact]
    public void KarnIsABarbarianWhichIsWhyHeWasAlwaysTheToughOne()
    {
        var karn = TestContent.Library.BuildCreature("karn")!;

        // d12: 12 + 5x7 = 47, +3 a level for Con 16. The same 65 the hand-written file had,
        // which is how we know the number was a barbarian's all along.
        Assert.Equal(65, karn.HitPoints.Maximum);
        Assert.Equal(6, karn.BaseAttackBonus);
    }
}
