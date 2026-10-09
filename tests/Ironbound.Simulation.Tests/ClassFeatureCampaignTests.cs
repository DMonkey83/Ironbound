using Ironbound.Rules.Classes;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;

namespace Ironbound.Simulation.Tests;

/// <summary>
/// A one-fight campaign with one hero of a chosen class, won by fiat, so the hero has a level
/// to take — the same arrangement the levelling-choice tests use, for any class.
/// </summary>
internal static class LevelReady
{
    public static (Campaign Run, Creature Hero, ContentLibrary Library) For(string classId, int level, string extra = "")
    {
        var files = ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).ToList();

        files.Add(("test-hero.json", $$"""
            { "kind": "creature", "id": "test-hero", "name": "Tester",
              "abilities": [16, 14, 14, 14, 14, 14],
              "classes": [ { "class": "{{classId}}", "level": {{level}} } ]
              {{(extra.Length > 0 ? "," + extra : string.Empty)}} }
            """));

        // Rated CR 9, and therefore worth 6,400: enough for a level from anywhere up to fourth.
        // Experience comes from the challenge rating now, and a warrior 8 is only CR 6 by the
        // Bestiary's rule, so the rating is written rather than left to the levels.
        files.Add(("test-foe.json", """
            { "kind": "creature", "id": "test-foe", "name": "Something Large", "cr": 9,
              "abilities": [16, 12, 14, 10, 12, 10],
              "classes": [ { "class": "warrior", "level": 8 } ] }
            """));

        files.Add(("test-encounter.json", """
            { "kind": "encounter", "id": "test-fight", "name": "A Test", "width": 8, "height": 8,
              "placements": [
                { "creature": "test-hero", "x": 1, "y": 1, "party": true },
                { "creature": "test-foe", "x": 6, "y": 6 } ] }
            """));

        files.Add(("test-campaign.json", """
            { "kind": "campaign", "id": "test-run", "name": "A Test Run", "encounters": ["test-fight"], "rests": 1 }
            """));

        var library = ContentLibrary.Load(files);
        Assert.Empty(library.Problems);

        var run = Campaign.Begin(library, "test-run");
        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        run.Collect();
        return (run, run.Party.Single(), library);
    }
}

public class ClassLevelUpTests
{
    [Fact]
    public void AFightersSecondLevelAsksForACombatFeat()
    {
        var (run, hero, library) = LevelReady.For("fighter", 1, "\"items\": [\"longsword\"]");

        var needs = run.NeedsFor(hero, library.GetClass("fighter")!);

        Assert.NotEmpty(needs.BonusFeats);
        Assert.All(needs.BonusFeats, feat => Assert.True(feat.Combat));
        Assert.Empty(needs.WeaponGroups);
        Assert.Empty(needs.Talents);
    }

    [Fact]
    public void TheChosenBonusFeatIsTaken()
    {
        var (run, hero, library) = LevelReady.For("fighter", 1, "\"items\": [\"longsword\"]");
        var armour = hero.ArmorClass.Total;

        Assert.True(run.LevelUp(hero, library.GetClass("fighter")!, null, new LevelChoices(library.GetFeat("dodge"))));

        Assert.True(hero.HasFeat("dodge"));
        Assert.Equal(armour + 1, hero.ArmorClass.Total);
        Assert.Equal("Fighter 2", hero.Description);
    }

    [Fact]
    public void ABonusFeatThatIsNotACombatFeatIsRefusedAndNothingChanges()
    {
        var (run, hero, library) = LevelReady.For("fighter", 1);

        Assert.False(run.LevelUp(hero, library.GetClass("fighter")!, null, new LevelChoices(library.GetFeat("iron-will"))));

        Assert.Equal("Fighter 1", hero.Description);
        Assert.False(hero.HasFeat("iron-will"));
        Assert.NotNull(run.LevelRefusal);
    }

    [Fact]
    public void NobodyChoosingMeansASensiblePick()
    {
        var (run, hero, library) = LevelReady.For("fighter", 1, "\"items\": [\"longsword\"]");

        Assert.True(run.LevelUp(hero, library.GetClass("fighter")!));

        Assert.Contains(hero.Feats, feat => feat.Key == "weapon-focus:longsword");
    }

    [Fact]
    public void TheOneArgumentFormPicksToo()
    {
        var (run, hero, _) = LevelReady.For("barbarian", 1);

        Assert.True(run.LevelUp(hero));

        Assert.Equal(["powerful-blow"], hero.Choices.Talents.Select(talent => talent.Id));
    }

    [Fact]
    public void ARogueChoosesHerTalent()
    {
        var (run, hero, library) = LevelReady.For("rogue", 1);

        Assert.True(run.LevelUp(hero, library.GetClass("rogue")!, null, new LevelChoices(Talent: "resiliency")));

        Assert.True(hero.Choices.HasTalent(TalentEffect.Resiliency));
        Assert.True(RogueDefences.HasEvasion(hero));
    }

    [Fact]
    public void ARagePowerIsNotARogueTalent()
    {
        var (run, hero, library) = LevelReady.For("rogue", 1);

        Assert.False(run.LevelUp(hero, library.GetClass("rogue")!, null, new LevelChoices(Talent: "powerful-blow")));
        Assert.Contains("powerful-blow", run.LevelRefusal);
        Assert.Equal("Rogue 1", hero.Description);
    }

    [Fact]
    public void ACombatTrickBringsItsFeat()
    {
        var (run, hero, library) = LevelReady.For("rogue", 1);

        Assert.True(run.LevelUp(
            hero, library.GetClass("rogue")!, null, new LevelChoices(library.GetFeat("dodge"), Talent: "combat-trick")));

        Assert.True(hero.HasFeat("dodge"));
        Assert.True(hero.Choices.HasTalent("combat-trick"));
    }

    [Fact]
    public void AFighterChoosesAWeaponGroupAtFifth()
    {
        var (run, hero, library) = LevelReady.For("fighter", 4, "\"items\": [\"shortbow\"]");
        var fighter = library.GetClass("fighter")!;

        Assert.Contains("bows", run.NeedsFor(hero, fighter).WeaponGroups);
        Assert.True(run.LevelUp(hero, fighter, null, new LevelChoices(WeaponGroup: "bows")));

        Assert.Equal(1, Martial.WeaponTraining(hero, "bows"));
    }

    [Fact]
    public void AGroupThatDoesNotExistIsRefused()
    {
        var (run, hero, library) = LevelReady.For("fighter", 4);

        Assert.False(run.LevelUp(hero, library.GetClass("fighter")!, null, new LevelChoices(WeaponGroup: "lightsabres")));
    }

    [Fact]
    public void AWizardsFifthBringsEmpowerSpell()
    {
        var (run, hero, library) = LevelReady.For("wizard", 4);

        Assert.True(run.LevelUp(hero, library.GetClass("wizard")!, library.GetFeat("toughness"), new LevelChoices()));

        Assert.True(hero.HasFeat("empower-spell"));
        Assert.True(hero.HasFeat("toughness"));
    }

    [Fact]
    public void TheSameFeatCannotBeBothTheLevelsFeatAndItsBonusFeat()
    {
        // A wizard 2 taking her first fighter level: third character level, so a feat, and a
        // fighter's first, so a bonus feat as well.
        var (run, hero, library) = LevelReady.For("wizard", 2);
        var dodge = library.GetFeat("dodge")!;

        Assert.False(run.LevelUp(hero, library.GetClass("fighter")!, dodge, new LevelChoices(dodge)));
        Assert.Equal("Wizard 2", hero.Description);

        Assert.True(run.LevelUp(hero, library.GetClass("fighter")!, dodge, new LevelChoices(library.GetFeat("improved-initiative"))));
        Assert.Equal("Wizard 2 / Fighter 1", hero.Description);
        Assert.True(hero.HasFeat("dodge"));
        Assert.True(hero.HasFeat("improved-initiative"));
    }

    [Fact]
    public void AChoiceForALevelThatOffersNoneIsRefused()
    {
        var (run, hero, library) = LevelReady.For("wizard", 1);

        Assert.False(run.LevelUp(hero, library.GetClass("wizard")!, null, new LevelChoices(Talent: "resiliency")));
        Assert.False(run.LevelUp(hero, library.GetClass("wizard")!, null, new LevelChoices(library.GetFeat("empower-spell"))));
        Assert.True(run.LevelUp(hero, library.GetClass("wizard")!, null, new LevelChoices()));
    }

    [Fact]
    public void AClericsNewSpellLevelBringsANewDomainSlot()
    {
        var (run, hero, library) = LevelReady.For("cleric", 2, "\"deity\": \"cihua-couatl\", \"domains\": [\"healing\", \"war\"]");

        Assert.Equal(0, hero.Spells.SpecialtyMaximum(2));
        Assert.True(run.LevelUp(hero));

        Assert.Equal(1, hero.Spells.SpecialtyMaximum(2));
        Assert.Equal(1, hero.Spells.SpecialtyRemaining(2));
    }
}

public class ClassFeatureRestTests
{
    [Fact]
    public void RestingRefillsEveryPoolAndShedsRageFatigue()
    {
        var (run, karn) = Between("the-long-road", "Karn");
        Rage.Start(karn);
        karn.DailyUses.Spend(Rage.Pool, 5);
        var merrin = run.Party.Single(one => one.Name == "Merrin");
        merrin.DailyUses.Spend("force-missile", 7);

        Assert.True(run.Rest());

        Assert.False(karn.IsRaging);
        Assert.False(karn.Has(Condition.Fatigued));
        Assert.Equal(17, karn.RageRoundsLeft);
        Assert.Equal(7, merrin.UsesLeft(merrin.Powers.Single(power => power.Id == "force-missile")));
    }

    [Fact]
    public void AFightThatIsOverIsOverForTheRageToo()
    {
        var (run, karn) = Between("the-long-road", "Karn");

        // Started after the fight, as the autopilot might have left it running.
        Rage.Start(karn);
        run.Collect();

        Assert.False(karn.IsRaging);
        Assert.False(karn.Has(Condition.Fatigued));
        Assert.Equal(16, karn.RageRoundsLeft);
    }

    [Fact]
    public void AFightStillOnLeavesItRunning()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        var karn = run.Party.Single(one => one.Name == "Karn");
        Rage.Start(karn);

        run.Collect();

        Assert.True(karn.IsRaging);
    }

    private static (Campaign Run, Creature Who) Between(string id, string name)
    {
        var run = Campaign.Begin(ChainContent.Library, id);
        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        Assert.Equal(CampaignState.Between, run.State);
        return (run, run.Party.Single(one => one.Name == name));
    }
}
