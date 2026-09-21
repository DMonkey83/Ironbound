using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Magic;

namespace Ironbound.Simulation.Tests;

public class CampaignTests
{
    [Fact]
    public void ItOpensOnTheFirstChapter()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        Assert.Equal(1, run.Chapter);
        Assert.Equal(CampaignState.Fighting, run.State);
        Assert.Equal(["Valeria", "Karn", "Merrin"], run.Party.Select(c => c.Name));
    }

    [Fact]
    public void WinningOneFightLeavesAnotherWaiting()
    {
        var run = Won();

        Assert.Equal(CampaignState.Between, run.State);
        Assert.True(run.CanAdvance);
    }

    [Fact]
    public void WoundsAndSpentSlotsFollowThePartyIntoTheNextFight()
    {
        var run = Won();
        var before = run.Party.Select(c => (c.Name, c.HitPoints.Damage)).ToList();
        var slots = run.Party.Single(c => c.Name == "Merrin").Spells.SlotsRemaining(3);

        Assert.True(run.Advance());

        Assert.Equal(2, run.Chapter);
        Assert.Equal(before, run.Party.Select(c => (c.Name, c.HitPoints.Damage)));
        Assert.Equal(slots, run.Party.Single(c => c.Name == "Merrin").Spells.SlotsRemaining(3));

        // Carried, not copied: the same people, standing somewhere new.
        Assert.Equal("Grey Fang", run.Battle.Foes.Single().Name);
    }

    [Fact]
    public void TheSecondChapterRollsItsOwnDiceRatherThanReplayingTheFirst()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle));
        var opening = run.Battle.Log.ToList();

        run.Advance();
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle));

        // By content rather than by length: two different fights can happen to produce the
        // same number of lines, and a test that only counts them says nothing.
        Assert.NotEqual(opening, run.Battle.Log);
    }

    [Fact]
    public void RestingGivesEverythingBack()
    {
        var run = Won();
        var merrin = run.Party.Single(c => c.Name == "Merrin");
        var maximum = merrin.Spells.SlotsMaximum(3);

        Assert.True(run.Rest());

        Assert.All(run.Party, creature => Assert.Equal(0, creature.HitPoints.Damage));
        Assert.All(run.Party, creature => Assert.Empty(creature.Effects.Active));
        Assert.Equal(maximum, merrin.Spells.SlotsRemaining(3));
    }

    [Fact]
    public void ThereIsOnlyOneRestAndSpendingItIsTheDecision()
    {
        var run = Won();

        Assert.Equal(1, run.RestsRemaining);
        Assert.True(run.Rest());

        Assert.Equal(0, run.RestsRemaining);
        Assert.False(run.CanRest);
        Assert.False(run.Rest());
    }

    [Fact]
    public void YouCannotRestInTheMiddleOfAFight()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        Assert.Equal(CampaignState.Fighting, run.State);
        Assert.False(run.CanRest);
        Assert.False(run.Advance());
    }

    [Fact]
    public void RunningOutOfChaptersIsWinning()
    {
        var run = Won();
        run.Advance();
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle, seed: 4242), maximumTurns: 400);

        Assert.Equal(
            run.Battle.Outcome == BattleOutcome.PartyWon ? CampaignState.Won : CampaignState.Lost,
            run.State);
        Assert.False(run.CanAdvance);
    }

    /// <summary>The first chapter, fought to a finish.</summary>
    private static Campaign Won()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);

        return run;
    }

    [Fact]
    public void ACampaignThatIsNotInTheFilesSaysSo()
    {
        var error = Assert.Throws<ArgumentException>(
            () => Campaign.Begin(ContentFiles.Default, "the-short-road"));

        Assert.Contains("the-short-road", error.Message);
    }
}

public class LootTests
{
    [Fact]
    public void TheFallenAreStrippedOfWhatTheyCarried()
    {
        var run = Fought();

        Assert.True(run.Collect() > 0);
        Assert.Contains(run.Stash, item => item.Id == "silvered-longsword");
    }

    [Fact]
    public void AnyoneWhoCannotObjectCountsAsFallen()
    {
        var run = Fought();
        run.Collect();

        // The sergeant is usually left dying rather than dead. Leaving his sword on him because
        // the rules call him "dying" would read as a bug, whatever it is called.
        Assert.All(run.Battle.Foes, foe => Assert.Empty(foe.Equipment.Items));
    }

    [Fact]
    public void ChaptersAreLootedOnceHoweverManyTimesAnybodyAsks()
    {
        var run = Fought();

        var first = run.Collect();
        var count = run.Stash.Count;

        Assert.True(first > 0);
        Assert.Equal(0, run.Collect());
        Assert.Equal(count, run.Stash.Count);
    }

    [Fact]
    public void NothingIsTakenWhileTheFightingIsStillGoingOn()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        Assert.Equal(0, run.Collect());
        Assert.Empty(run.Stash);
    }

    [Fact]
    public void TakingASwordPutsItInYourHandAndTheOldOneBackInTheSack()
    {
        var run = Fought();
        run.Collect();

        var valeria = run.Party.Single(c => c.Name == "Valeria");

        Assert.True(run.Give(valeria, "silvered-longsword"));

        // In her hand, not stowed behind the blade she already had — otherwise she keeps
        // swinging the wrong thing and the loot does nothing.
        Assert.Equal("silvered longsword", valeria.PrimaryAttack!.Name);
        Assert.Contains(run.Stash, item => item.Id == "longsword");
    }

    [Fact]
    public void AndTheSilverIsWhatFinallyGetsThroughTheWerewolf()
    {
        var run = Fought();
        run.Collect();

        var valeria = run.Party.Single(c => c.Name == "Valeria");
        run.Give(valeria, "silvered-longsword");
        run.Advance();

        var werewolf = run.Battle.Foes.Single();
        var karn = run.Party.Single(c => c.Name == "Karn");

        // Power Attack is itself a partial answer to flat reduction — more raw damage means
        // more of it survives — so it comes off for a like-for-like comparison.
        karn.Stances.Clear();

        // Same roll, same round, two blades. This is what the gate was built for.
        var axe = Strike.Resolve(karn, karn.PrimaryAttack!, werewolf, new SequenceRandom(true, 15, 4));
        var silver = Strike.Resolve(
            valeria, valeria.PrimaryAttack!, werewolf, new SequenceRandom(true, 15, 4));

        Assert.True(axe.Attack.IsHit);
        Assert.Equal(0, axe.DamageDealt);
        Assert.True(silver.DamageDealt > 0);
    }

    [Fact]
    public void SomethingGivenAwayCanBeTakenBack()
    {
        var run = Fought();
        run.Collect();

        var karn = run.Party.Single(c => c.Name == "Karn");
        run.Give(karn, "silvered-longsword");

        Assert.True(run.Reclaim(karn, "silvered-longsword"));
        Assert.Contains(run.Stash, item => item.Id == "silvered-longsword");
        Assert.False(karn.Equipment.Has("silvered-longsword"));
    }

    [Fact]
    public void TheSackSurvivesASaveWithWhatIsStillInIt()
    {
        var run = Fought();
        run.Collect();
        var carried = run.Stash.Select(item => item.Id).ToList();

        var restored = Campaign.FromJson(run.ToJson(), ContentFiles.Default);

        Assert.Equal(carried, restored.Stash.Select(item => item.Id));

        // And the chapter stays looted, so reloading is not a way to strip the bodies twice.
        Assert.Equal(0, restored.Collect());
    }

    private static Campaign Fought()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);

        return run;
    }
}

public class ExperienceTests
{
    [Fact]
    public void ThePartyStartsWhereItsLevelsSayItAlreadyIs()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        // Otherwise the opening fight reads as though six levels had never happened, and the
        // first goblin killed would hand the whole party a level.
        Assert.Equal(6, run.EarnedLevel);
        Assert.Equal(6, run.Party.Max(one => one.Level));
        Assert.Empty(run.Ready);
    }

    [Fact]
    public void WinningAChapterIsWorthSomething()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        var opening = run.Experience;

        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        run.Collect();

        Assert.True(run.Experience > opening);
    }

    [Fact]
    public void ExperienceIsAwardedOnceHoweverManyTimesAnybodyAsks()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);

        run.Collect();
        var earned = run.Experience;
        run.Collect();

        Assert.Equal(earned, run.Experience);
    }

    [Fact]
    public void TwoFightsDoNotLevelASixthLevelParty()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        run.Advance();
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        run.Collect();

        // Not a shortcoming. Thirteen encounters is roughly what a level costs at this tier,
        // and a campaign that levelled you every fight would make the numbers meaningless.
        Assert.Equal(6, run.EarnedLevel);
        Assert.True(run.Experience < run.NextLevelAt);
        Assert.DoesNotContain(run.Ready, one => one.Level >= 6);
    }

    [Fact]
    public void ButTheOneWhoJoinedALevelBehindCatchesUp()
    {
        var run = Fought();
        run.Collect();

        // Merrin is a wizard 5 in a party of sixes. A shared pool means the shortfall closes
        // by itself, which is what a shared pool is for.
        var merrin = Assert.Single(run.Ready);
        Assert.Equal("Merrin", merrin.Name);
        Assert.Equal("Wizard 5", merrin.Description);

        Assert.True(run.LevelUp(merrin));

        Assert.Equal("Wizard 6", merrin.Description);
        Assert.Equal(6, merrin.Spells.CasterLevel);
        Assert.Equal(3, merrin.BaseAttackBonus);
        Assert.Empty(run.Ready);
    }

    [Fact]
    public void NobodyLevelsUpInTheMiddleOfAFight()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");

        Assert.Empty(run.Ready);
        Assert.False(run.LevelUp(run.Party[0]));
    }

    private static Campaign Fought()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);

        return run;
    }
}

public class LevellingChoiceTests
{
    [Fact]
    public void TheLevelCanGoIntoAnyClassInTheLibrary()
    {
        var (run, merrin) = Ready();

        Assert.Equal(
            ["Barbarian", "Fighter", "Rogue", "Warrior", "Wizard"],
            run.ClassesFor(merrin).Select(taken => taken.Name));
    }

    [Fact]
    public void TakingOneInSomethingElseMakesYouTwoThings()
    {
        var (run, merrin) = Ready();

        Assert.True(run.LevelUp(merrin, ContentFiles.Default.GetClass("fighter")!));

        Assert.Equal("Wizard 5 / Fighter 1", merrin.Description);
        Assert.Equal(6, merrin.Level);
    }

    [Fact]
    public void OnlyFeatsSheQualifiesForAreOffered()
    {
        var (run, merrin) = Ready();
        var offered = run.FeatsFor(merrin).Select(feat => feat.Id).ToList();

        // Strength 8: Power Attack is not hers to take, whatever else is.
        Assert.DoesNotContain("power-attack", offered);
        Assert.Contains("dodge", offered);
    }

    [Fact]
    public void NorAnythingSheAlreadyHas()
    {
        var (run, merrin) = Ready();

        Assert.True(merrin.HasFeat("improved-initiative"));
        Assert.DoesNotContain("improved-initiative", run.FeatsFor(merrin).Select(feat => feat.Id));
    }

    [Fact]
    public void AChosenFeatIsAppliedExactlyOnce()
    {
        var (run, hero, library) = Owed();
        var dodge = library.GetFeat("dodge")!;
        var armour = hero.ArmorClass.Total;

        Assert.True(run.NextLevelGrantsFeat(hero));
        Assert.True(run.LevelUp(hero, library.GetClass("warrior")!, dodge));

        Assert.True(hero.HasFeat("dodge"));

        // ApplyTo is not idempotent, so applying it twice would quietly be worth two.
        Assert.Equal(armour + 1, hero.ArmorClass.Total);
    }

    [Fact]
    public void AFeatTheyDoNotQualifyForIsRefusedAndChangesNothing()
    {
        var (run, hero, library) = Owed();
        var before = hero.Description;

        Assert.False(run.LevelUp(
            hero,
            library.GetClass("warrior")!,
            library.GetFeat("improved-trip")!));

        // Refused outright rather than levelled without the feat: the choice was the point.
        Assert.Equal(before, hero.Description);
        Assert.False(hero.HasFeat("improved-trip"));
    }

    [Fact]
    public void AFeatOnAnEvenLevelIsRefusedRatherThanQuietlyDropped()
    {
        var (run, merrin) = Ready();

        // Wizard 5 taking her sixth: an even level, so no feat comes with it.
        Assert.False(run.NextLevelGrantsFeat(merrin));
        Assert.False(run.LevelUp(
            merrin,
            ContentFiles.Default.GetClass("wizard")!,
            ContentFiles.Default.GetFeat("dodge")!));

        Assert.Equal("Wizard 5", merrin.Description);
    }

    [Fact]
    public void WithoutAFeatAnEvenLevelIsFine()
    {
        var (run, merrin) = Ready();

        Assert.True(run.LevelUp(merrin, ContentFiles.Default.GetClass("wizard")!));
        Assert.Equal("Wizard 6", merrin.Description);
    }

    [Fact]
    public void TheOldNoArgumentFormStillTakesTheClassSheHasMostOf()
    {
        var (run, merrin) = Ready();

        Assert.True(run.LevelUp(merrin));
        Assert.Equal("Wizard 6", merrin.Description);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(20, false)]
    public void FeatsComeAtOddLevels(int level, bool grants) =>
        Assert.Equal(grants, Ironbound.Rules.Classes.Levelling.GrantsFeatAt(level));

    /// <summary>
    /// A campaign wound forward to somebody standing on an odd level with experience to spend.
    /// </summary>
    /// <remarks>
    /// The shipped campaign cannot produce one. Merrin is its only party member who can level,
    /// and her sixth is an even level, so no feat comes with it; the werewolf beyond her is
    /// worth 2,500 against the 5,600 that a seventh would cost. Rather than inflate the content
    /// to suit a test, this adds a two-creature run alongside it and wins that by fiat.
    /// </remarks>
    private static (Campaign Run, Creature Hero, ContentLibrary Library) Owed()
    {
        var files = ContentFiles
            .Read(Path.Combine(AppContext.BaseDirectory, "content"))
            .ToList();

        files.Add(("test-hero.json", """
            { "kind": "creature", "id": "test-hero", "name": "Tester",
              "abilities": [16, 12, 14, 10, 12, 10],
              "classes": [ { "class": "warrior", "level": 4 } ] }
            """));

        // Level eight, and therefore worth 6,400 — enough on its own to buy the fifth level.
        files.Add(("test-foe.json", """
            { "kind": "creature", "id": "test-foe", "name": "Something Large",
              "abilities": [16, 12, 14, 10, 12, 10],
              "classes": [ { "class": "warrior", "level": 8 } ] }
            """));

        files.Add(("test-encounter.json", """
            { "kind": "encounter", "id": "test-fight", "name": "A Test",
              "width": 8, "height": 8,
              "placements": [
                { "creature": "test-hero", "x": 1, "y": 1, "party": true },
                { "creature": "test-foe", "x": 6, "y": 6 } ] }
            """));

        files.Add(("test-campaign.json", """
            { "kind": "campaign", "id": "test-run", "name": "A Test Run",
              "encounters": ["test-fight"], "rests": 0 }
            """));

        var library = ContentLibrary.Load(files);
        var run = Campaign.Begin(library, "test-run");

        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 100);
        }

        run.Collect();

        return (run, run.Party.Single(), library);
    }

    /// <summary>Chapter one won, leaving Merrin a level behind the fighters and able to take it.</summary>
    private static (Campaign Run, Creature Merrin) Ready()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        run.Collect();

        return (run, run.Party.Single(one => one.Name == "Merrin"));
    }
}

public class CampaignSaveTests
{
    [Fact]
    public void ARunComesBackWhereItWasLeft()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle));
        run.Advance();

        var restored = Campaign.FromJson(run.ToJson(), ContentFiles.Default);

        Assert.Equal(2, restored.Chapter);
        Assert.Equal(run.RestsRemaining, restored.RestsRemaining);
        Assert.Equal(CampaignState.Fighting, restored.State);
        // By name rather than by position: a freshly built battle lists the party in placement
        // order and a restored one in initiative order. Cosmetic, but worth not asserting.
        Assert.Equal(
            run.Party.Select(c => (c.Name, c.HitPoints.Current)).OrderBy(c => c.Name),
            restored.Party.Select(c => (c.Name, c.HitPoints.Current)).OrderBy(c => c.Name));
    }

    [Fact]
    public void ThePartyIsStillRecognisedAsTheSamePeopleAfterwards()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle));

        var restored = Campaign.FromJson(run.ToJson(), ContentFiles.Default);
        var wounded = restored.Party.Sum(c => c.HitPoints.Damage);

        Assert.True(restored.Advance());

        // Keyed by the definition they came from, not by their display name — so a reload does
        // not quietly hand you a fresh, unwounded party for the next fight.
        Assert.Equal(wounded, restored.Party.Sum(c => c.HitPoints.Damage));
        Assert.All(restored.Party, creature => Assert.NotNull(creature.DefinitionId));
    }

    [Fact]
    public void LevelsSurviveASaveSoTheNextOneLandsInTheRightClass()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        var restored = Campaign.FromJson(run.ToJson(), ContentFiles.Default);
        var valeria = restored.Party.Single(c => c.Name == "Valeria");

        // Without the levels coming back, another one would open a second Fighter entry and
        // she would be "Fighter 6 / Fighter 1" — six plus one base attack instead of seven.
        Assert.Equal("Fighter 6", valeria.Description);
        Assert.Equal(run.Experience, restored.Experience);

        Ironbound.Rules.Classes.Levelling.Gain(
            valeria, ContentFiles.Default.GetClass("fighter")!);

        Assert.Equal("Fighter 7", valeria.Description);
        Assert.Equal(7, valeria.BaseAttackBonus);
    }

    [Fact]
    public void ASingleFightSaveIsNotACampaignAndSaysSo()
    {
        var battle = Scenarios.GoblinAmbush();
        var json = Ironbound.Rules.Persistence.GameSave.ToJson(
            Ironbound.Rules.Persistence.GameSave.Capture(battle.Encounter));

        var error = Assert.Throws<InvalidDataException>(
            () => Campaign.FromJson(json, ContentFiles.Default));

        Assert.Contains("single fight", error.Message);
    }
}

public class ShippedCampaignTests
{
    [Fact]
    public void TheLongRoadIsShippedAndBothItsFightsExist()
    {
        var road = ContentFiles.Default.GetCampaign("the-long-road")!;

        Assert.Equal(["goblin-ambush", "moonlit-clearing"], road.Encounters);
        Assert.Equal(1, road.Rests);
        Assert.All(road.Encounters, id => Assert.NotNull(ContentFiles.Default.GetEncounter(id)));
    }

    [Fact]
    public void ACampaignNamingAFightThatDoesNotExistIsReported()
    {
        var library = ContentLibrary.Load([
            ("run.json", """
                { "kind": "campaign", "id": "run", "name": "Run",
                  "encounters": ["nowhere"] }
                """),
        ]);

        var problem = Assert.Single(library.Problems);
        Assert.Equal("campaign 'run'", problem.Source);
        Assert.Contains("nowhere", problem.Message);
    }
}
