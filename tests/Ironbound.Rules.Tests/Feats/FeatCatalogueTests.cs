using System.Text.Json;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Classes;
using Ironbound.Rules.Content;
using Ironbound.Rules.Feats;
using Ironbound.Rules.Skills;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>
/// The Core Rulebook's whole list of feats is in the content: every one present, the ones the
/// game cannot do yet saying why, and the prerequisites written the way the book writes them.
/// </summary>
public class FeatCatalogueTests
{
    private static ContentLibrary Library => TestContent.Library;

    private static FeatDefinition Feat(string id) =>
        Library.GetFeat(id) ?? throw new InvalidOperationException($"No feat '{id}'.");

    [Fact]
    public void EveryCoreRulebookFeatHasAFile()
    {
        Assert.Equal(176, Library.FeatIds.Count);
        Assert.Empty(Library.Problems);
    }

    [Fact]
    public void AFeatTheGameCannotDoSaysWhyAndIsNeverOffered()
    {
        var twf = Feat("two-weapon-fighting");
        var hero = ClassKit.Make("fighter", 6, abilities: [16, 18, 14, 10, 10, 10]);

        Assert.False(twf.IsAvailable);
        Assert.Contains("two-weapon fighting", twf.Unavailable);
        Assert.False(twf.AvailableTo(hero));
        Assert.Equal([twf.Unavailable!], twf.WhyNot(hero));
    }

    [Theory]
    [InlineData("two-weapon-fighting")]
    [InlineData("improved-disarm")]
    [InlineData("blind-fight")]
    [InlineData("improved-unarmed-strike")]
    [InlineData("catch-off-guard")]
    [InlineData("quick-draw")]
    [InlineData("mounted-combat")]
    [InlineData("brew-potion")]
    [InlineData("leadership")]
    [InlineData("critical-mastery")]
    [InlineData("silent-spell")]
    [InlineData("strike-back")]
    [InlineData("disruptive")]
    public void TierBAndTheOutOfScopeFeatsAreDataOnly(string id) =>
        Assert.False(Feat(id).IsAvailable);

    [Theory]
    [InlineData("alertness")]
    [InlineData("mobility")]
    [InlineData("spring-attack")]
    [InlineData("greater-trip")]
    [InlineData("stunning-critical")]
    [InlineData("deadly-aim")]
    [InlineData("spell-focus")]
    [InlineData("turn-undead")]
    [InlineData("extra-rage")]
    [InlineData("endurance")]
    public void TierAFeatsAreAvailable(string id) =>
        Assert.True(Feat(id).IsAvailable);

    [Fact]
    public void WeaponSpecializationWantsWeaponFocusInTheSameWeapon()
    {
        var fighter = ClassKit.Make("fighter", 4, "\"items\": [\"longsword\", \"greataxe\"], \"feats\": [\"weapon-focus:longsword\"]");
        var specialization = Feat("weapon-specialization");

        Assert.True((specialization with { Choice = "longsword" }).AvailableTo(fighter));
        Assert.False((specialization with { Choice = "greataxe" }).AvailableTo(fighter));
        Assert.Contains("weapon-focus:greataxe", (specialization with { Choice = "greataxe" }).WhyNot(fighter));

        // Unchosen, it is on offer as long as some Weapon Focus is held, and taken for that weapon.
        Assert.True(specialization.AvailableTo(fighter));
        Assert.Equal("longsword", ClassLevelling.ChooseFor(fighter, specialization)!.Choice);
    }

    [Fact]
    public void FighterLevelsAreNotJustAnyLevels()
    {
        var rogue = ClassKit.Make("rogue", 6, "\"items\": [\"short-sword\"], \"feats\": [\"weapon-focus:short-sword\"]");
        var fighter = ClassKit.Make("fighter", 4, "\"items\": [\"short-sword\"], \"feats\": [\"weapon-focus:short-sword\"]");
        var specialization = Feat("weapon-specialization") with { Choice = "short-sword" };

        Assert.Contains("fighter 4", specialization.WhyNot(rogue));
        Assert.True(specialization.AvailableTo(fighter));
    }

    [Fact]
    public void WeaponFocusWantsProficiency()
    {
        // A wizard is not trained with a greataxe, so she may not focus on one.
        var wizard = ClassKit.Make("wizard", 3, "\"items\": [\"greataxe\", \"quarterstaff\"]");
        wizard.BaseAttackBonus = 1;

        Assert.False((Feat("weapon-focus") with { Choice = "greataxe" }).AvailableTo(wizard));
        Assert.True((Feat("weapon-focus") with { Choice = "quarterstaff" }).AvailableTo(wizard));
    }

    [Fact]
    public void ArcaneStrikeWantsArcaneSpells()
    {
        var feat = Feat("arcane-strike");

        Assert.True(feat.AvailableTo(ClassKit.Make("wizard", 1)));
        Assert.False(feat.AvailableTo(ClassKit.Make("cleric", 1, "\"deity\": \"none\"")));
        Assert.Contains("arcane spells", feat.WhyNot(ClassKit.Make("fighter", 1)));
    }

    [Fact]
    public void TurnAndCommandUndeadWantTheirOwnKindOfChannel()
    {
        var good = ClassKit.Make("cleric", 1, "\"deity\": \"none\"");
        var evil = ClassKit.Make("cleric", 1, "\"deity\": \"none\", \"channel\": \"Negative\"");

        Assert.True(Feat("turn-undead").AvailableTo(good));
        Assert.False(Feat("turn-undead").AvailableTo(evil));
        Assert.True(Feat("command-undead").AvailableTo(evil));
        Assert.Contains("channel negative energy", Feat("command-undead").WhyNot(good));
    }

    [Fact]
    public void AFeatTakenMoreThanOnceStaysOnOffer()
    {
        var barbarian = ClassKit.Make("barbarian", 2, "\"feats\": [\"extra-rage\"]");

        Assert.True(Feat("extra-rage").Repeatable);
        Assert.True(Feat("extra-rage").AvailableTo(barbarian));
        Assert.False(Feat("toughness").Repeatable);
    }

    [Fact]
    public void PrerequisitesBeyondFeatsAndAbilitiesAreReadAndPrinted()
    {
        Assert.Equal("Dex 13, dodge, mobility, base attack +4", Feat("spring-attack").Requires.ToString());
        Assert.Equal("shield-focus, base attack +1, fighter 8, shields armour proficiency", Feat("greater-shield-focus").Requires.ToString());
        Assert.Equal("caster level 3", Feat("brew-potion").Requires.ToString());
        Assert.Equal("Ride 1 rank", Feat("mounted-combat").Requires.ToString());
        Assert.Equal("catch-off-guard or throw-anything, base attack +8", Feat("improvised-weapon-mastery").Requires.ToString());
        Assert.Equal("fighter 14, 2 critical feats", Feat("critical-mastery").Requires.ToString());
        Assert.Equal("arcane spells", Feat("arcane-strike").Requires.ToString());
        Assert.Equal("channel positive energy, channel-energy", Feat("turn-undead").Requires.ToString());
    }

    [Fact]
    public void ACriticalFeatIsMarkedAsOne()
    {
        Assert.True(Feat("bleeding-critical").Critical);
        Assert.True(Feat("stunning-critical").Critical);
        Assert.False(Feat("critical-focus").Critical);
    }

    [Fact]
    public void AFeatChoiceOfTheWrongKindIsReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("bad.json", """
            { "kind": "creature", "id": "bad", "name": "Bad", "abilities": [10, 10, 10, 10, 10, 10],
              "classes": [ { "class": "fighter", "level": 1 } ],
              "feats": ["skill-focus:Juggling", "spell-focus:Evocation", "alignment-channel:evil"] }
            """)));

        var problem = Assert.Single(library.Problems);
        Assert.Contains("Juggling", problem.Message);
    }

    [Fact]
    public void ARequirementNamingAClassOrSkillThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load(TestContent.Files().Append(("odd.json", """
            { "kind": "feat", "id": "odd", "name": "Odd",
              "requires": { "classLevels": { "samurai": 3 }, "ranks": { "Juggling": 2 } } }
            """)));

        Assert.Contains(library.Problems, problem => problem.Message.Contains("samurai"));
        Assert.Contains(library.Problems, problem => problem.Message.Contains("Juggling"));
    }

    [Fact]
    public void ASkillFeatNamesItsSkills()
    {
        Assert.Equal([Skill.Perception, Skill.SenseMotive], Feat("alertness").Skills);
        Assert.Equal(FeatEffect.SkillBonus, Feat("stealthy").Effect);
    }

    [Fact]
    public void NoFeatFileQuotesTheBook()
    {
        // House rule: every description is the project's own, and short.
        foreach (var id in Library.FeatIds)
        {
            Assert.InRange(Feat(id).Description.Length, 10, 200);
        }
    }

    [Fact]
    public void EveryFeatFileIsWhatTheLoaderExpects()
    {
        var folder = Path.Combine(TestContent.Directory, "feats");

        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            Assert.Equal("feat", document.RootElement.GetProperty("kind").GetString());
            Assert.Equal(Path.GetFileNameWithoutExtension(path), document.RootElement.GetProperty("id").GetString());
        }
    }

    [Fact]
    public void ValeriaTakesGreaterTripAndQualifiesForIt()
    {
        var valeria = Library.BuildCreature("valeria")!;
        var trip = Feat("greater-trip");

        Assert.True(valeria.HasFeat("greater-trip"));
        Assert.Empty(trip.Requires.Unmet(valeria));
        Assert.Equal(8, valeria.Feats.Count);
        Assert.Equal(13, valeria.Abilities[Ability.Intelligence].Score);
    }
}
