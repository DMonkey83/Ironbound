using Ironbound.Rules.Abilities;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;

namespace Ironbound.Rules.Tests.Content;

public class EncounterStoryParsingTests
{
    [Fact]
    public void AnEncounterCarriesItsIntroAndWhatIsLyingAbout()
    {
        var library = ContentParsingTests.From("""
            { "kind": "encounter", "id": "cellar", "name": "Cellar",
              "intro": "It smells of old wine.",
              "loot": ["corkscrew"] }
            """, """
            { "kind": "item", "id": "corkscrew", "name": "corkscrew" }
            """);

        var cellar = library.GetEncounter("cellar")!;

        Assert.Equal("It smells of old wine.", cellar.Intro);
        Assert.Equal(["corkscrew"], cellar.Loot);
    }

    [Fact]
    public void AndNeitherIsRequired()
    {
        var library = ContentParsingTests.From("""
            { "kind": "encounter", "id": "cellar", "name": "Cellar" }
            """);

        var cellar = library.GetEncounter("cellar")!;

        Assert.Equal(string.Empty, cellar.Intro);
        Assert.Empty(cellar.Loot);
    }

    [Fact]
    public void LootThatDoesNotExistIsReported()
    {
        var library = ContentParsingTests.From("""
            { "kind": "encounter", "id": "cellar", "name": "Cellar", "loot": ["philosophers-stone"] }
            """, expectProblems: true);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("encounter 'cellar'", problem.Source);
        Assert.Equal("loot", problem.Field);
        Assert.Contains("philosophers-stone", problem.Message);
    }

    [Fact]
    public void APlacementCanBeAsleep()
    {
        var library = ContentParsingTests.From("""
            { "kind": "creature", "id": "orc", "name": "Orc", "abilities": [15, 11, 12, 8, 7, 6] }
            """, """
            { "kind": "encounter", "id": "barracks", "name": "Barracks",
              "placements": [
                { "creature": "orc", "x": 1, "y": 1, "name": "Awake" },
                { "creature": "orc", "x": 2, "y": 1, "name": "Dozing", "asleep": true } ] }
            """);

        var placements = library.GetEncounter("barracks")!.Placements;

        Assert.Equal([false, true], placements.Select(p => p.Asleep));

        // Asleep and lying in wait are different things; one does not imply the other.
        Assert.All(placements, p => Assert.False(p.Hidden));
    }
}

public class CampaignListingTests
{
    [Fact]
    public void ACampaignCarriesADescriptionForTheMenu()
    {
        var library = ContentParsingTests.From("""
            { "kind": "campaign", "id": "run", "name": "Run", "description": "A short one." }
            """);

        Assert.Equal("A short one.", library.GetCampaign("run")!.Description);
    }

    [Fact]
    public void WithoutOneItIsEmptyRatherThanMissing()
    {
        var library = ContentParsingTests.From("""
            { "kind": "campaign", "id": "run", "name": "Run" }
            """);

        Assert.Equal(string.Empty, library.GetCampaign("run")!.Description);
    }

    [Fact]
    public void CampaignsAreListedByNameWhateverOrderTheFilesCameIn()
    {
        var library = ContentParsingTests.From(
            """{ "kind": "campaign", "id": "z", "name": "Zebra Crossing" }""",
            """{ "kind": "campaign", "id": "a", "name": "Middle March" }""",
            """{ "kind": "campaign", "id": "m", "name": "Aardvark Hill" }""");

        Assert.Equal(
            ["Aardvark Hill", "Middle March", "Zebra Crossing"],
            library.Campaigns.Select(run => run.Name));
    }

    [Fact]
    public void EveryShippedCampaignIsOnTheList()
    {
        Assert.Equal(
            ["caves-of-shadow", "the-long-road"],
            TestContent.Library.Campaigns.Select(run => run.Id));
        Assert.All(
            TestContent.Library.Campaigns,
            run => Assert.False(string.IsNullOrWhiteSpace(run.Description), run.Id));
    }
}

public class CavesOfShadowContentTests
{
    private static ContentLibrary Library => TestContent.Library;

    [Fact]
    public void FourChaptersInTheOrderTheCavesAreWalked()
    {
        var caves = Library.GetCampaign("caves-of-shadow")!;

        Assert.Equal("Caves of Shadow", caves.Name);
        Assert.Equal(["cave-mouth", "orc-lair", "guard-post", "ogre-den"], caves.Encounters);
        Assert.Equal(1, caves.Rests);
    }

    [Fact]
    public void EveryChapterSaysSomethingAsItOpens()
    {
        foreach (var id in Library.GetCampaign("caves-of-shadow")!.Encounters)
        {
            Assert.False(string.IsNullOrWhiteSpace(Library.GetEncounter(id)!.Intro), id);
        }
    }

    [Fact]
    public void TheLairHasOneOrcAwakeAndTwoAsleep()
    {
        var lair = Library.GetEncounter("orc-lair")!;
        var foes = lair.Placements.Where(p => !p.Party).ToList();

        Assert.Equal(3, foes.Count);
        Assert.Equal(2, foes.Count(p => p.Asleep));
        Assert.Equal("orc-sentry", foes.Single(p => !p.Asleep).CreatureId);
    }

    [Fact]
    public void AndTheStoreroomBehindItHoldsTheMerchantsSword()
    {
        Assert.Equal(
            ["greatsword-plus-one", "light-crossbow"],
            Library.GetEncounter("orc-lair")!.Loot);
    }

    [Fact]
    public void NoChapterStandsAnybodyInsideARockOrOffTheMap()
    {
        foreach (var id in Library.GetCampaign("caves-of-shadow")!.Encounters)
        {
            var encounter = Library.GetEncounter(id)!;
            var blocked = encounter.Blocked.Select(square => (square.X, square.Y)).ToHashSet();

            foreach (var placement in encounter.Placements)
            {
                Assert.InRange(placement.X, 0, encounter.Width - 1);
                Assert.InRange(placement.Y, 0, encounter.Height - 1);
                Assert.DoesNotContain((placement.X, placement.Y), blocked);
            }

            // And names that tell two of a kind apart, which the save file depends on.
            Assert.Equal(
                encounter.Placements.Count,
                encounter.Placements.Select(p => p.Name ?? Library.GetCreature(p.CreatureId)!.Name)
                    .Distinct()
                    .Count());
        }
    }

    [Fact]
    public void AldricIsTheOneWhoCanTakeAHit()
    {
        var aldric = Library.BuildCreature("aldric")!;

        Assert.Equal(12, aldric.HitPoints.Maximum);   // d10 at first level, +2 for Con 14
        Assert.Equal(1, aldric.BaseAttackBonus);
        Assert.Equal(16, aldric.ArmorClass.Total);    // 10 + 4 scale + 1 Dexterity + 1 Dodge
        Assert.Equal("greatsword", aldric.PrimaryAttack!.Name);
        Assert.Equal(19, aldric.PrimaryAttack.Attack.Critical.ThreatsOn);
    }

    [Fact]
    public void SylwenHasTwoMissilesAndVeryLittleElse()
    {
        var sylwen = Library.BuildCreature("sylwen")!;

        Assert.Equal(7, sylwen.HitPoints.Maximum);
        Assert.Equal(2, sylwen.Spells.SlotsMaximum(1));   // one from the table, one from Int 17
        Assert.Equal(["Magic Missile"], sylwen.Spells.Prepared.Select(spell => spell.Name));
        Assert.Equal("shortbow", sylwen.PrimaryAttack!.Name);
    }

    [Fact]
    public void HaleIsAClericWithThreeSpellsADay()
    {
        var hale = Library.BuildCreature("hale")!;

        Assert.Equal("Cleric 1", hale.Description);
        Assert.Equal(Ability.Wisdom, hale.Spells.CastingAbility);
        Assert.Equal(1, hale.Spells.CasterLevel);

        // Two from the table, one of which stands in for the domain slot, and one for Wis 15.
        Assert.Equal(3, hale.Spells.SlotsMaximum(1));
        Assert.Equal(
            ["Bless", "Cure Light Wounds"],
            hale.Spells.Prepared.Select(spell => spell.Name));
        Assert.Equal(17, hale.ArmorClass.Total);   // 10 + 4 scale + 2 heavy shield + 1 Dodge
    }

    [Fact]
    public void PipIsSmallAndHardToHit()
    {
        var pip = Library.BuildCreature("pip")!;

        Assert.Equal(CreatureSize.Small, pip.Size);
        Assert.Equal(20, pip.Speed);
        Assert.Equal(16, pip.ArmorClass.Total);   // 10 + 2 leather + 3 Dexterity + 1 size
        Assert.Equal("short sword", pip.PrimaryAttack!.Name);
    }

    [Fact]
    public void TheMerchantsSwordIsOneBetterAndCountsAsMagic()
    {
        var item = Library.GetItem("greatsword-plus-one")!;
        var plain = Library.BuildItemWeapon(Library.GetItem("greatsword")!)!;
        var magic = Library.BuildItemWeapon(item)!;

        Assert.Equal(1, item.Enhancement);
        Assert.Equal(plain.Attack.Modifiers.Total + 1, magic.Attack.Modifiers.Total);
        Assert.Equal(plain.DamageModifiers.Total + 1, magic.DamageModifiers.Total);
        Assert.True(magic.Qualities.HasFlag(DamageBypass.Magic));
    }

    [Theory]
    [InlineData("orc", 6, 13)]
    [InlineData("orc-sentry", 6, 13)]
    [InlineData("dire-rat", 5, 14)]
    [InlineData("ogre", 24, 15)]
    public void TheFoesAreToughEnoughAndNoMore(string id, int hitPoints, int armour)
    {
        var foe = Library.BuildCreature(id)!;

        Assert.Equal(hitPoints, foe.HitPoints.Maximum);
        Assert.Equal(armour, foe.ArmorClass.Total);
    }

    [Fact]
    public void TheOgreIsLargeAndReachesTenFeet()
    {
        var ogre = Library.BuildCreature("ogre")!;

        Assert.Equal(CreatureSize.Large, ogre.Size);
        Assert.Equal(10, ogre.Reach);
        Assert.Equal("massive axe", ogre.PrimaryAttack!.Name);
    }

    [Fact]
    public void TheRatsTeethAreNotSomethingYouCanPutInASack()
    {
        var rat = Library.BuildCreature("dire-rat")!;

        Assert.Empty(rat.Equipment.Items);
        Assert.Equal("bite", rat.PrimaryAttack!.Name);
    }

    [Theory]
    [InlineData("aldric", "res://art/karn.glb")]
    [InlineData("sylwen", "res://art/merrin.glb")]
    [InlineData("hale", "res://art/valeria.glb")]
    [InlineData("pip", "res://art/karn.glb")]
    [InlineData("orc", "res://art/orc.glb")]
    [InlineData("dire-rat", "res://art/dire-rat.glb")]
    [InlineData("ogre", "res://art/ogre.glb")]
    public void EachIsDrawnWithItsModel(string id, string model) =>
        Assert.Equal(model, Library.GetCreature(id)!.Model);
}
