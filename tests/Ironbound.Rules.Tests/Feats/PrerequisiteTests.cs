using Ironbound.Rules.Abilities;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Tests.Content;

namespace Ironbound.Rules.Tests.Feats;

public class PrerequisiteTests
{
    [Fact]
    public void NothingAskedMeansNothingOwed()
    {
        var plain = new FeatRequirements();

        Assert.Empty(plain.Unmet(Hero()));
        Assert.Equal("none", plain.ToString());
    }

    [Fact]
    public void AnAbilityOneShortFailsAndExactlyMeetingItPasses()
    {
        var wants = new FeatRequirements
        {
            Abilities = new Dictionary<Ability, int> { [Ability.Strength] = 13 },
        };

        Assert.Equal(["Str 13"], wants.Unmet(Hero(strength: 12)));
        Assert.Empty(wants.Unmet(Hero(strength: 13)));
    }

    [Fact]
    public void AMissingPrerequisiteFeatIsNamed()
    {
        var wants = new FeatRequirements { Feats = ["combat-expertise"] };
        var hero = Hero();

        Assert.Equal(["combat-expertise"], wants.Unmet(hero));

        hero.Feats.Add(TestContent.Library.GetFeat("combat-expertise")!);

        Assert.Empty(wants.Unmet(hero));
    }

    [Fact]
    public void SkillAtArmsAndLevelCountToo()
    {
        var wants = new FeatRequirements { BaseAttack = 4, Level = 4 };
        var novice = Hero();

        Assert.Equal(["base attack +4", "level 4"], wants.Unmet(novice));
    }

    [Fact]
    public void EverythingShortIsListedRatherThanTheFirstOne()
    {
        var wants = new FeatRequirements
        {
            Abilities = new Dictionary<Ability, int>
            {
                [Ability.Strength] = 20,
                [Ability.Intelligence] = 20,
            },
            Feats = ["dodge"],
        };

        // Telling somebody one of the three things they are missing wastes two of their guesses.
        Assert.Equal(3, wants.Unmet(Hero()).Count);
    }

    [Fact]
    public void AFeatAlreadyHeldIsNotOnOffer()
    {
        var hero = Hero();
        var dodge = TestContent.Library.GetFeat("dodge")!;

        Assert.True(dodge.AvailableTo(hero));

        hero.Feats.Add(dodge);

        Assert.False(dodge.AvailableTo(hero));
    }

    [Fact]
    public void ShippedPrerequisitesReadAsTheyShould()
    {
        Assert.Equal("Str 13, base attack +1", Feat("power-attack").Requires.ToString());
        Assert.Equal("Int 13", Feat("combat-expertise").Requires.ToString());
        Assert.Equal("Int 13, combat-expertise", Feat("improved-trip").Requires.ToString());
        Assert.Equal("none", Feat("dodge").Requires.ToString());
    }

    [Fact]
    public void AWirySortCannotTakePowerAttack()
    {
        var duellist = Hero(strength: 10);

        Assert.False(Feat("power-attack").AvailableTo(duellist));
        Assert.Contains("Str 13", Feat("power-attack").Requires.Unmet(duellist));
    }

    internal static FeatDefinition Feat(string id) => TestContent.Library.GetFeat(id)!;

    internal static Creature Hero(int strength = 16, int baseAttack = 0) =>
        new("Hero", new AbilityScores(strength, 12, 12, 10, 10, 10), 20, 1)
        {
            BaseAttackBonus = baseAttack,
        };
}

public class PrerequisiteContentTests
{
    [Fact]
    public void AFeatRequiringOneThatDoesNotExistIsReportedOnce()
    {
        var library = ContentLibrary.Load([
            ("odd.json", """
                { "kind": "feat", "id": "odd", "name": "Odd", "requires": { "feats": ["nonsense"] } }
                """),
            ("goblin.json", """
                { "kind": "creature", "id": "goblin", "name": "Goblin",
                  "abilities": [10, 10, 10, 10, 10, 10] }
                """),
            ("orc.json", """
                { "kind": "creature", "id": "orc", "name": "Orc",
                  "abilities": [10, 10, 10, 10, 10, 10] }
                """),
        ]);

        // Nested inside the creature loop, this was reported once per creature in the library.
        var problem = Assert.Single(library.Problems);
        Assert.Equal("feat 'odd'", problem.Source);
        Assert.Contains("nonsense", problem.Message);
    }

    [Fact]
    public void AndIsStillReportedWhenThereAreNoCreaturesAtAll()
    {
        var library = ContentLibrary.Load([
            ("odd.json", """
                { "kind": "feat", "id": "odd", "name": "Odd", "requires": { "feats": ["nonsense"] } }
                """),
        ]);

        // Nested, the check never ran at all in a library with nothing to iterate.
        Assert.Single(library.Problems);
    }

    [Fact]
    public void RequirementsAreReadFromTheFile()
    {
        var library = ContentParsingTests.From("""
            { "kind": "feat", "id": "heavy", "name": "Heavy Blows",
              "requires": { "str": 15, "con": 13, "baseAttack": 6, "level": 7 } }
            """);

        var wants = library.GetFeat("heavy")!.Requires;

        Assert.Equal(15, wants.Abilities[Ability.Strength]);
        Assert.Equal(13, wants.Abilities[Ability.Constitution]);
        Assert.Equal(6, wants.BaseAttack);
        Assert.Equal(7, wants.Level);
    }

    [Fact]
    public void EveryShippedCreatureQualifiesForTheFeatsItsFileGivesIt()
    {
        foreach (var id in TestContent.Library.CreatureIds)
        {
            var creature = TestContent.Library.BuildCreature(id)!;

            foreach (var feat in creature.Feats)
            {
                // A creature written with a feat it could never have taken is a content
                // mistake, even though nothing refuses it at build time.
                Assert.Equal((id, feat.Name, 0), (id, feat.Name, feat.Requires.Unmet(creature).Count));
            }
        }
    }
}
