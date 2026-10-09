using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation.Tests;

public class SleepingPlacementTests
{
    [Fact]
    public void TheLairOpensWithTwoOrcsAsleep()
    {
        var battle = Scenarios.Build(ChainContent.Library, "orc-lair");

        Assert.Equal(
            ["Sleeping Orc", "Snoring Orc"],
            battle.Foes.Where(foe => foe.Has(Condition.Asleep)).Select(foe => foe.Name).Order());
        Assert.False(battle.Foes.Single(foe => foe.Name == "Orc Sentry").Has(Condition.Asleep));
        Assert.Equal(2, battle.Log.Count(line => line.Contains("is asleep")));
    }

    [Fact]
    public void NobodySleepsWhereNobodyWasWrittenAsleep()
    {
        var battle = Scenarios.Build(ChainContent.Library, "cave-mouth");

        Assert.DoesNotContain(battle.Foes, foe => foe.Has(Condition.Asleep));
        Assert.Empty(battle.Log);
    }

    [Fact]
    public void ASleeperStaysWhereItLayThroughTheFirstRound()
    {
        var battle = Scenarios.Build(ChainContent.Library, "orc-lair");
        var sleeper = battle.Foes.First(foe => foe.Has(Condition.Asleep));
        var square = battle.Battlefield!.SquareOf(sleeper);

        // Only the first round. Somebody may well have woken it by the second.
        var pilot = Scenarios.AutoPilot(battle);
        while (battle.Round == 1 && battle.AdvanceTurn(pilot) is not null)
        {
        }

        Assert.Equal(square, battle.Battlefield.SquareOf(sleeper));
    }

    [Fact]
    public void SleepersAreStillAsleepAfterASaveAndReload()
    {
        var battle = Scenarios.Build(ChainContent.Library, "orc-lair");
        battle.BeginTurn();
        battle.EndTurn();

        var reloaded = Battle.Restore(
            GameSave.FromJson(GameSave.ToJson(GameSave.Capture(battle.Encounter))),
            ChainContent.Library);

        Assert.Equal(
            battle.Foes.Where(foe => foe.Has(Condition.Asleep)).Select(foe => foe.Name).Order(),
            reloaded.Foes.Where(foe => foe.Has(Condition.Asleep)).Select(foe => foe.Name).Order());
    }

    [Fact]
    public void ACampaignSavedMidFightKeepsItsSleepersToo()
    {
        var run = AtTheLair();

        var restored = Campaign.FromJson(run.ToJson(), ChainContent.Library);

        Assert.Equal(2, restored.Battle.Foes.Count(foe => foe.Has(Condition.Asleep)));
    }

    private static Campaign AtTheLair()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        Win(run);
        run.Advance();
        return run;
    }

    internal static void Win(Campaign run)
    {
        foreach (var foe in run.Battle.Foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 20);
        }
    }
}

public class EncounterLootTests
{
    [Fact]
    public void WinningTheLairFindsTheStoreroom()
    {
        var run = AtTheLair();
        SleepingPlacementTests.Win(run);

        run.Collect();

        Assert.Contains(run.Stash, item => item.Id == "greatsword-plus-one");
        Assert.Contains(run.Stash, item => item.Id == "light-crossbow");
    }

    [Fact]
    public void ItIsFoundOnceHoweverManyTimesAnybodyLooks()
    {
        var run = AtTheLair();
        SleepingPlacementTests.Win(run);

        run.Collect();
        run.Collect();
        run.Advance();

        Assert.Single(run.Stash, item => item.Id == "greatsword-plus-one");
    }

    [Fact]
    public void AndReloadingIsNotAWayToFindItTwice()
    {
        var run = AtTheLair();
        SleepingPlacementTests.Win(run);
        run.Collect();

        var restored = Campaign.FromJson(run.ToJson(), ChainContent.Library);
        restored.Collect();
        restored.Advance();

        Assert.Single(restored.Stash, item => item.Id == "greatsword-plus-one");
    }

    [Fact]
    public void AdvancingCollectsItEvenIfNobodyAskedFirst()
    {
        var run = AtTheLair();
        SleepingPlacementTests.Win(run);

        run.Advance();

        Assert.Contains(run.Stash, item => item.Id == "greatsword-plus-one");
    }

    [Fact]
    public void NothingIsFoundBeforeTheFightIsOver()
    {
        var run = AtTheLair();

        Assert.Equal(0, run.Collect());
        Assert.DoesNotContain(run.Stash, item => item.Id == "greatsword-plus-one");
    }

    [Fact]
    public void NorByAPartyThatLost()
    {
        var run = AtTheLair();
        foreach (var hero in run.Party)
        {
            hero.HitPoints.Take(hero.HitPoints.Maximum + 20);
        }

        run.Collect();

        Assert.Equal(CampaignState.Lost, run.State);
        Assert.DoesNotContain(run.Stash, item => item.Id == "greatsword-plus-one");
    }

    [Fact]
    public void TheSwordFoundIsTheSwordAldricCanSwing()
    {
        var run = AtTheLair();
        SleepingPlacementTests.Win(run);
        run.Collect();

        var aldric = run.Party.Single(hero => hero.Name == "Aldric");

        Assert.True(run.Give(aldric, "greatsword-plus-one"));
        Assert.Equal("greatsword +1", aldric.PrimaryAttack!.Name);
        Assert.Contains(run.Stash, item => item.Id == "greatsword");
    }

    private static Campaign AtTheLair()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        SleepingPlacementTests.Win(run);
        run.Advance();
        return run;
    }
}

public class CavesOfShadowCampaignTests
{
    [Fact]
    public void ItOpensAtTheCaveMouthWithFourNewHeroes()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");

        Assert.Equal(1, run.Chapter);
        Assert.Equal("cave-mouth", run.Scene!.Id);
        Assert.Equal(["Aldric", "Pip", "Hale", "Sylwen"], run.Party.Select(hero => hero.Name));
        Assert.All(run.Party, hero => Assert.Equal(1, hero.Level));
        Assert.Equal(1, run.EarnedLevel);
    }

    [Fact]
    public void EachChapterHandsOverItsOwnIntro()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");
        var intros = new List<string> { run.Intro };

        while (run.Chapter < run.Definition.Encounters.Count)
        {
            SleepingPlacementTests.Win(run);
            Assert.True(run.Advance());
            intros.Add(run.Intro);
        }

        Assert.Equal(
            run.Definition.Encounters.Select(id => ChainContent.Library.GetEncounter(id)!.Intro),
            intros);
        Assert.All(intros, intro => Assert.False(string.IsNullOrWhiteSpace(intro)));
    }

    [Fact]
    public void TheLongRoadHasNothingToSayAndThatIsFine()
    {
        Assert.Equal(string.Empty, Campaign.Begin(ChainContent.Library, "the-long-road").Intro);
    }

    [Fact]
    public void EveryChapterCanBeFoughtToAFinish()
    {
        var run = Campaign.Begin(ChainContent.Library, "caves-of-shadow");

        // Not a balance test: the autopilot does not heal and fights every room at whatever
        // strength it walked in with. What it does prove is that each room builds, plays and
        // ends — nobody stuck in a rock, nobody asleep for ever.
        foreach (var _ in run.Definition.Encounters)
        {
            run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
            Assert.NotEqual(BattleOutcome.InProgress, run.Battle.Outcome);

            if (!run.CanAdvance)
            {
                break;
            }

            run.Rest();
            run.Advance();
        }

        Assert.NotEqual(CampaignState.Fighting, run.State);
    }
}
