using Ironbound.Rules.Combat;
using Ironbound.Rules.Content;
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
        var first = Campaign.Begin(ContentFiles.Default, "the-long-road");
        first.Battle.RunToCompletion(Scenarios.AutoPilot(first.Battle));
        var opening = first.Battle.Log.Count;

        first.Advance();
        first.Battle.RunToCompletion(Scenarios.AutoPilot(first.Battle));

        Assert.NotEqual(opening, first.Battle.Log.Count);
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
