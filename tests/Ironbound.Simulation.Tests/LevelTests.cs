using System.Text.RegularExpressions;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Content;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;

namespace Ironbound.Simulation.Tests;

/// <summary>
/// A small level made for testing, alongside the shipped content.
/// </summary>
/// <remarks>
/// Small so that every square in a test can be named, and with its difficulty numbers passed in,
/// so that a test about what happens when somebody fails a door does not depend on finding a
/// seed where they happen to.
/// <code>
///   0123456789AB
/// 0 ############
/// 1 #c...#.....#     hall: x1-4 (quiet)          room: x6-10, y1-3 (Lurker, hidden; final)
/// 2 #...P+.....#     P = Pip, beside the door
/// 3 #....#.....#
/// 4 #....#######
/// 5 #...A~~....#     A = Aldric, at the bridge   far: x7-10, y5-6 (Sleeper, asleep; a dagger)
/// 6 #....~~....#
/// 7 ############
/// </code>
/// </remarks>
internal static class TestLevel
{
    public static readonly GridSquare Door = new(5, 2);
    public static readonly GridSquare Bridge = new(5, 5);
    public static readonly GridSquare PipStart = new(4, 2);
    public static readonly GridSquare AldricStart = new(4, 5);

    public static ContentLibrary Library(
        int lockDc = 15, int breakDc = 16, int jumpDc = 10, int climbDc = 15)
    {
        var files = ContentFiles.Read(Path.Combine(AppContext.BaseDirectory, "content")).ToList();

        files.Add(("test-level.json", $$"""
            { "kind": "level", "id": "test-level", "name": "Test Level",
              "stone": "rocky", "grass": "woodland",
              "map": [
                "############",
                "#c...#.....#",
                "#....+.....#",
                "#....#.....#",
                "#....#######",
                "#....~~....#",
                "#....~~....#",
                "############" ],
              "start": [
                { "creature": "pip", "x": 4, "y": 2 },
                { "creature": "aldric", "x": 4, "y": 5 } ],
              "areas": [
                { "id": "hall", "name": "The Hall", "x": 1, "y": 1, "width": 4, "height": 6,
                  "intro": "A hall." },
                { "id": "room", "name": "The Room", "x": 6, "y": 1, "width": 5, "height": 3,
                  "final": true,
                  "foes": [ { "creature": "goblin", "x": 9, "y": 2, "name": "Lurker", "hidden": true } ] },
                { "id": "far", "name": "The Far Bank", "x": 7, "y": 5, "width": 4, "height": 2,
                  "outro": "Done.", "loot": ["dagger"],
                  "foes": [ { "creature": "goblin", "x": 9, "y": 6, "name": "Sleeper", "asleep": true } ] } ],
              "features": [
                { "id": "door", "kind": "door", "name": "the door", "squares": [ { "x": 5, "y": 2 } ],
                  "lockDc": {{lockDc}}, "breakDc": {{breakDc}} },
                { "id": "bridge", "kind": "bridge", "name": "the bridge", "text": "It holds.",
                  "squares": [ { "x": 5, "y": 5 }, { "x": 6, "y": 5 } ],
                  "jumpDc": {{jumpDc}}, "climbDc": {{climbDc}}, "fall": "2d6" },
                { "id": "chest", "kind": "cache", "name": "the chest", "text": "A crossbow.",
                  "squares": [ { "x": 1, "y": 1 } ], "loot": ["light-crossbow"] } ] }
            """));

        files.Add(("test-level-campaign.json", """
            { "kind": "campaign", "id": "test-level-run", "name": "Test Level Run",
              "level": "test-level", "rests": 1 }
            """));

        var library = ContentLibrary.Load(files);
        Assert.Empty(library.Problems);
        return library;
    }

    public static Campaign Begin(ContentLibrary? library = null, ulong seed = 20260920) =>
        Campaign.Begin(library ?? Library(), "test-level-run", seed);

    public static Creature Pip(this Campaign run) => run.Party.Single(one => one.Name == "Pip");

    public static Creature Aldric(this Campaign run) => run.Party.Single(one => one.Name == "Aldric");

    public static void Defeat(IEnumerable<Creature> foes)
    {
        foreach (var foe in foes)
        {
            foe.HitPoints.Take(foe.HitPoints.Maximum + 20);
        }
    }
}

public class LevelStartTests
{
    [Fact]
    public void ACampaignWithALevelIsPlayedOnIt()
    {
        var run = TestLevel.Begin();

        Assert.True(run.IsLevel);
        Assert.Equal("test-level", run.Level!.Id);
        Assert.Equal(CampaignState.Exploring, run.State);
        Assert.Null(run.CurrentArea);
        Assert.False(run.CanAdvance);
        Assert.False(run.Advance());
    }

    [Fact]
    public void ThePartyStartsWhereTheLevelPutsThem()
    {
        var run = TestLevel.Begin();

        Assert.Equal(["Pip", "Aldric"], run.Party.Select(one => one.Name));
        Assert.Equal(TestLevel.PipStart, run.Field.SquareOf(run.Pip()));
        Assert.Equal(TestLevel.AldricStart, run.Field.SquareOf(run.Aldric()));
        Assert.Same(run.Field, run.Battle.Encounter.Battlefield);
        Assert.Empty(run.Battle.Foes);
    }

    [Fact]
    public void TheMapIsTheGround()
    {
        var run = TestLevel.Begin();

        Assert.Equal(12, run.Field.Width);
        Assert.Equal(8, run.Field.Height);
        Assert.True(run.Field.IsBlocked(new GridSquare(0, 0)));
        Assert.True(run.Field.IsBlocked(TestLevel.Door));
        Assert.True(run.Field.IsBlocked(TestLevel.Bridge));
        Assert.True(run.Field.IsBlocked(new GridSquare(1, 1)));
        Assert.False(run.Field.IsBlocked(new GridSquare(2, 2)));
    }

    [Fact]
    public void EverybodyElseIsWaitingInTheirRoomsButNotOnTheBoard()
    {
        var run = TestLevel.Begin();

        Assert.Equal(
            [("Lurker", new GridSquare(9, 2), "room"), ("Sleeper", new GridSquare(9, 6), "far")],
            run.Dormant.Select(one => (one.Foe.Name, one.Square, one.Area.Id)));
        Assert.All(run.Dormant, one => Assert.Null(run.Field.SquareOf(one.Foe)));
        Assert.All(run.Dormant, one => Assert.True(one.Foe.IsEnemyOf(run.Pip())));
    }

    [Fact]
    public void ExperienceStartsWhereTheirLevelsSay()
    {
        Assert.Equal(1, TestLevel.Begin().EarnedLevel);
    }

    [Fact]
    public void QuietRoomsAreClearedAlready()
    {
        var run = TestLevel.Begin();

        Assert.True(run.IsCleared("hall"));
        Assert.False(run.IsCleared("room"));
        Assert.False(run.IsCleared("far"));
    }

    [Fact]
    public void ARoomIsVisitedOnce()
    {
        var run = TestLevel.Begin();

        Assert.False(run.IsVisited("hall"));
        Assert.True(run.Visit("hall"));
        Assert.True(run.IsVisited("hall"));
        Assert.False(run.Visit("hall"));
        Assert.False(run.Visit("nowhere"));
    }

    [Fact]
    public void SquaresKnowWhichRoomAndWhichFeature()
    {
        var run = TestLevel.Begin();

        Assert.Equal("hall", run.AreaAt(new GridSquare(2, 2))!.Id);
        Assert.Equal("far", run.AreaAt(new GridSquare(8, 6))!.Id);
        Assert.Null(run.AreaAt(new GridSquare(0, 0)));
        Assert.Equal("door", run.FeatureAt(TestLevel.Door)!.Id);
        Assert.Equal("bridge", run.FeatureAt(new GridSquare(6, 5))!.Id);
        Assert.Null(run.FeatureAt(new GridSquare(2, 2)));
    }
}

public class WalkingTests
{
    [Fact]
    public void SomebodyCanWalkToAnyFreeSquare()
    {
        var run = TestLevel.Begin();

        Assert.True(run.Walk(run.Pip(), new GridSquare(2, 3)));
        Assert.Equal(new GridSquare(2, 3), run.Field.SquareOf(run.Pip()));
    }

    [Fact]
    public void ButNotIntoAWallOrSomebodyElse()
    {
        var run = TestLevel.Begin();

        Assert.False(run.Walk(run.Pip(), new GridSquare(0, 2)));
        Assert.False(run.Walk(run.Pip(), TestLevel.Door));
        Assert.False(run.Walk(run.Pip(), TestLevel.AldricStart));
        Assert.False(run.Walk(run.Pip(), new GridSquare(40, 40)));
        Assert.Equal(TestLevel.PipStart, run.Field.SquareOf(run.Pip()));
    }

    [Fact]
    public void NorWhenTheyAreDown()
    {
        var run = TestLevel.Begin();
        run.Pip().HitPoints.Take(run.Pip().HitPoints.Maximum + 2);

        Assert.False(run.Walk(run.Pip(), new GridSquare(2, 3)));
    }

    [Fact]
    public void NorInTheMiddleOfAFight()
    {
        var run = TestLevel.Begin();
        run.Engage("far");

        Assert.False(run.Walk(run.Pip(), new GridSquare(2, 3)));
    }

    [Fact]
    public void NobodyIsAlarmedUntilSomebodyStepsIntoAGuardedRoom()
    {
        var run = TestLevel.Begin();

        Assert.Null(run.Alarm());

        run.Walk(run.Aldric(), new GridSquare(8, 5));

        Assert.Equal("far", run.Alarm()!.Id);
    }

    [Fact]
    public void AQuietRoomRaisesNoAlarm()
    {
        var run = TestLevel.Begin();
        run.Walk(run.Pip(), new GridSquare(1, 6));

        Assert.Null(run.Alarm());
    }
}

public class LevelFightTests
{
    [Fact]
    public void EngagingARoomPutsItsOccupantsOnTheBoard()
    {
        var run = TestLevel.Begin();

        Assert.True(run.Engage("far"));

        Assert.Equal(CampaignState.Fighting, run.State);
        Assert.Equal("far", run.CurrentArea!.Id);

        var sleeper = Assert.Single(run.Battle.Foes);
        Assert.Equal("Sleeper", sleeper.Name);
        Assert.Equal(new GridSquare(9, 6), run.Field.SquareOf(sleeper));
        Assert.Same(run.Field, run.Battle.Battlefield);
        Assert.Equal(["Lurker"], run.Dormant.Select(one => one.Foe.Name));
    }

    [Fact]
    public void TheAsleepAreAsleep()
    {
        var run = TestLevel.Begin();
        run.Engage("far");

        Assert.True(run.Battle.Foes.Single().Has(Condition.Asleep));
        Assert.Contains(run.Battle.Log, line => line.Contains("Sleeper is asleep"));
    }

    [Fact]
    public void AndTheHiddenGetTheirChance()
    {
        var run = TestLevel.Begin();
        run.Engage("room");

        Assert.Contains(run.Battle.Log, line => line.Contains("lying in wait"));
    }

    [Fact]
    public void AFoeWhoseSquareIsTakenGetsUpBesideIt()
    {
        var run = TestLevel.Begin();
        run.Walk(run.Aldric(), new GridSquare(9, 6));

        run.Engage("far");

        var sleeper = run.Battle.Foes.Single();
        var square = run.Field.SquareOf(sleeper)!.Value;
        Assert.NotEqual(new GridSquare(9, 6), square);
        Assert.True(Distance.AreAdjacent(square, new GridSquare(9, 6)));
    }

    [Fact]
    public void AClearedOrUnknownRoomCannotBeEngaged()
    {
        var run = TestLevel.Begin();

        Assert.False(run.Engage("hall"));
        Assert.False(run.Engage("nowhere"));

        run.Engage("far");
        Assert.False(run.Engage("room"));
    }

    [Fact]
    public void WinningARoomPutsThePartyBackToWalking()
    {
        var run = TestLevel.Begin();
        var experience = run.Experience;
        run.Engage("far");
        var sleeper = run.Battle.Foes.Single();

        TestLevel.Defeat(run.Battle.Foes);

        Assert.True(run.Collect() > 0);

        Assert.Equal(CampaignState.Exploring, run.State);
        Assert.Null(run.CurrentArea);
        Assert.True(run.IsCleared("far"));
        Assert.Null(run.Field.SquareOf(sleeper));
        Assert.Empty(run.Battle.Foes);
        Assert.Equal(["Pip", "Aldric"], run.Party.Select(one => one.Name));
        Assert.True(run.Experience > experience);
        Assert.Contains(run.Stash, item => item.Id == "dagger");
        Assert.Contains(run.Stash, item => item.Id == "scimitar");
    }

    [Fact]
    public void ARoomIsCollectedOnce()
    {
        var run = TestLevel.Begin();
        run.Engage("far");
        TestLevel.Defeat(run.Battle.Foes);

        run.Collect();
        var stash = run.Stash.Count;
        var experience = run.Experience;

        Assert.Equal(0, run.Collect());
        Assert.Equal(stash, run.Stash.Count);
        Assert.Equal(experience, run.Experience);
    }

    [Fact]
    public void ThePartyStaysWhereTheFightLeftThem()
    {
        var run = TestLevel.Begin();
        run.Engage("far");
        run.Field.Place(run.Aldric(), new GridSquare(8, 6));
        TestLevel.Defeat(run.Battle.Foes);

        run.Collect();

        Assert.Equal(new GridSquare(8, 6), run.Field.SquareOf(run.Aldric()));
    }

    [Fact]
    public void WinningTheFinalRoomWinsTheLevel()
    {
        var run = TestLevel.Begin();
        run.Engage("room");
        TestLevel.Defeat(run.Battle.Foes);

        Assert.Equal(CampaignState.Won, run.State);

        run.Collect();

        Assert.Equal(CampaignState.Won, run.State);
        Assert.False(run.Walk(run.Pip(), new GridSquare(2, 3)));
    }

    [Fact]
    public void LosingIsLosing()
    {
        var run = TestLevel.Begin();
        run.Engage("far");
        TestLevel.Defeat(run.Party);

        Assert.Equal(CampaignState.Lost, run.State);
        Assert.Equal(0, run.Collect());
        Assert.DoesNotContain(run.Stash, item => item.Id == "dagger");
    }

    [Fact]
    public void AFightCanBePlayedOutByTheAutopilot()
    {
        var run = TestLevel.Begin();
        run.Walk(run.Aldric(), new GridSquare(7, 5));
        run.Walk(run.Pip(), new GridSquare(7, 6));
        run.Engage(run.Alarm()!.Id);

        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        run.Collect();

        Assert.NotEqual(CampaignState.Fighting, run.State);
    }

    [Fact]
    public void TheSameSeedFightsTheSameFight()
    {
        static IReadOnlyList<string> Fight()
        {
            var run = TestLevel.Begin();
            run.Walk(run.Aldric(), new GridSquare(7, 5));
            run.Engage("far");
            run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
            return [.. run.Battle.Log];
        }

        Assert.Equal(Fight(), Fight());
    }
}

public class LevelRestTests
{
    [Fact]
    public void ThePartyCanRestWhileExploring()
    {
        var run = TestLevel.Begin();
        run.Aldric().HitPoints.Take(5);

        Assert.True(run.CanRest);
        Assert.True(run.Rest());

        Assert.Equal(0, run.Aldric().HitPoints.Damage);
        Assert.Equal(0, run.RestsRemaining);
        Assert.False(run.Rest());
    }

    [Fact]
    public void ButNotInTheMiddleOfAFight()
    {
        var run = TestLevel.Begin();
        run.Engage("far");

        Assert.False(run.CanRest);
        Assert.False(run.Rest());
    }

    [Fact]
    public void LootCanBeHandedOutWhileExploring()
    {
        var run = TestLevel.Begin();
        run.Engage("far");
        TestLevel.Defeat(run.Battle.Foes);
        run.Collect();

        Assert.True(run.Give(run.Pip(), "dagger"));
        Assert.True(run.Reclaim(run.Pip(), "dagger"));
    }
}

public class DoorTests
{
    [Fact]
    public void SomebodyWithTheSkillPicksTheLock()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: -10));

        var result = run.Use("door", run.Pip());

        Assert.True(result.Success);
        Assert.Contains("picks the lock", result.Lines[0]);
        Assert.Contains(result.Lines, line => line.Contains("Disable Device"));
        Assert.False(run.Field.IsBlocked(TestLevel.Door));
        Assert.True(run.IsUsed("door"));
        Assert.Null(result.MovedTo);
    }

    [Fact]
    public void SomebodyWithoutItForcesTheDoor()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: 100, breakDc: -10));
        run.Walk(run.Aldric(), new GridSquare(4, 3));

        var result = run.Use("door", run.Aldric());

        Assert.True(result.Success);
        Assert.Contains("forces", result.Lines[0]);
        Assert.Contains(result.Lines, line => line.Contains("Strength"));
        Assert.False(run.Field.IsBlocked(TestLevel.Door));
    }

    [Fact]
    public void AFailedAttemptLeavesItShutAndCanBeTriedAgain()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: 100));

        var result = run.Use("door", run.Pip());

        Assert.False(result.Success);
        Assert.Contains("cannot pick the lock", result.Lines[0]);
        Assert.True(run.Field.IsBlocked(TestLevel.Door));
        Assert.False(run.IsUsed("door"));
        Assert.True(run.CanUse("door", run.Pip()));
    }

    [Fact]
    public void AnOpenDoorIsNotOpenedTwice()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: -10));
        run.Use("door", run.Pip());

        Assert.False(run.CanUse("door", run.Pip()));
        Assert.False(run.Use("door", run.Pip()).Success);
    }

    [Fact]
    public void NobodyCanTryADoorFromAcrossTheRoom()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: -10));

        Assert.False(run.CanUse("door", run.Aldric()));

        var result = run.Use("door", run.Aldric());
        Assert.False(result.Success);
        Assert.Contains("not close enough", result.Lines[0]);
    }

    [Fact]
    public void NorInTheMiddleOfAFight()
    {
        var run = TestLevel.Begin(TestLevel.Library(lockDc: -10));
        run.Engage("far");

        Assert.False(run.CanUse("door", run.Pip()));
        Assert.False(run.Use("door", run.Pip()).Success);
    }

    [Fact]
    public void TheSameSeedOpensTheSameWay()
    {
        static IReadOnlyList<string> Try()
        {
            var run = TestLevel.Begin();
            return [.. Enumerable.Range(0, 5).SelectMany(_ => run.Use("door", run.Pip()).Lines)];
        }

        Assert.Equal(Try(), Try());
    }
}

public class BridgeTests
{
    [Fact]
    public void CrossingLandsOnTheFarSideAndTiesTheBridgeOff()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: -10));

        var result = run.Use("bridge", run.Aldric());

        Assert.True(result.Success);
        Assert.Equal(new GridSquare(7, 5), result.MovedTo);
        Assert.Equal(new GridSquare(7, 5), run.Field.SquareOf(run.Aldric()));
        Assert.False(run.Field.IsBlocked(new GridSquare(5, 5)));
        Assert.False(run.Field.IsBlocked(new GridSquare(6, 5)));
        Assert.True(run.Field.IsBlocked(new GridSquare(5, 6)));
        Assert.True(run.IsUsed("bridge"));
        Assert.Contains("It holds.", result.Lines);
    }

    [Fact]
    public void AndWhereTheyLandIsAGuardedRoom()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: -10));
        run.Use("bridge", run.Aldric());

        Assert.Equal("far", run.Alarm()!.Id);
    }

    [Fact]
    public void ThenEveryoneElseCanWalkOver()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: -10));
        run.Use("bridge", run.Aldric());

        var path = run.Field.FindPath(new GridSquare(3, 5), new GridSquare(8, 5), run.Pip());

        Assert.NotEmpty(path);
    }

    [Fact]
    public void ItWorksFromTheFarSideToo()
    {
        var run = TestLevel.Begin(TestLevel.Library(climbDc: -10, jumpDc: 100));
        run.Walk(run.Aldric(), new GridSquare(7, 5));

        var result = run.Use("bridge", run.Aldric());

        Assert.True(result.Success);
        Assert.Contains("climbs", result.Lines[0]);
        Assert.Equal(new GridSquare(4, 5), result.MovedTo);
    }

    [Fact]
    public void FailingIsAFallAndItHurts()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: 100, climbDc: 100));
        var aldric = run.Aldric();

        var result = run.Use("bridge", aldric);

        Assert.False(result.Success);
        Assert.Null(result.MovedTo);
        Assert.Contains("falls", result.Lines[0]);
        Assert.True(aldric.HitPoints.Damage >= 2);
        Assert.Equal(TestLevel.AldricStart, run.Field.SquareOf(aldric));
        Assert.True(run.Field.IsBlocked(TestLevel.Bridge));
        Assert.False(run.IsUsed("bridge"));
    }

    [Fact]
    public void AFallCanPutSomebodyDown()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: 100, climbDc: 100));
        var aldric = run.Aldric();
        aldric.HitPoints.Take(aldric.HitPoints.Maximum - 1);

        var result = run.Use("bridge", aldric);

        Assert.False(aldric.IsConscious);
        Assert.Contains("does not get up", result.Lines[0]);

        // Pip is still standing, so the run goes on; with nobody standing it would not.
        Assert.Equal(CampaignState.Exploring, run.State);
    }

    [Fact]
    public void NobodyCanCrossFromAwayFromTheBridge()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: -10));

        Assert.False(run.CanUse("bridge", run.Pip()));
    }
}

public class CacheTests
{
    [Fact]
    public void ACacheIsSearchedOnce()
    {
        var run = TestLevel.Begin();
        run.Walk(run.Pip(), new GridSquare(2, 2));

        var result = run.Use("chest", run.Pip());

        Assert.True(result.Success);
        Assert.Contains("A crossbow.", result.Lines);
        Assert.Single(run.Stash, item => item.Id == "light-crossbow");

        Assert.False(run.CanUse("chest", run.Pip()));
        Assert.False(run.Use("chest", run.Pip()).Success);
        Assert.Single(run.Stash, item => item.Id == "light-crossbow");
    }
}

public class LevelSaveTests
{
    [Fact]
    public void ARunSavedWhileExploringComesBackAsItWas()
    {
        var library = TestLevel.Library(lockDc: -10);
        var run = TestLevel.Begin(library);
        run.Use("door", run.Pip());
        run.Walk(run.Pip(), new GridSquare(2, 2));
        run.Use("chest", run.Pip());
        run.Visit("hall");
        run.Aldric().HitPoints.Take(3);

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.True(restored.IsLevel);
        Assert.Equal(CampaignState.Exploring, restored.State);
        Assert.Null(restored.CurrentArea);
        Assert.Equal(["Pip", "Aldric"], restored.Party.Select(one => one.Name));
        Assert.Equal(new GridSquare(2, 2), restored.Field.SquareOf(restored.Pip()));
        Assert.Equal(TestLevel.AldricStart, restored.Field.SquareOf(restored.Aldric()));
        Assert.Equal(3, restored.Aldric().HitPoints.Damage);
        Assert.False(restored.Field.IsBlocked(TestLevel.Door));
        Assert.True(restored.Field.IsBlocked(TestLevel.Bridge));
        Assert.True(restored.IsUsed("door"));
        Assert.True(restored.IsUsed("chest"));
        Assert.True(restored.IsVisited("hall"));
        Assert.Equal(run.Stash.Select(item => item.Id), restored.Stash.Select(item => item.Id));
        Assert.Equal(
            run.Dormant.Select(one => (one.Foe.Name, one.Square, one.Area.Id)),
            restored.Dormant.Select(one => (one.Foe.Name, one.Square, one.Area.Id)));
        Assert.Equal(run.Experience, restored.Experience);
        Assert.Equal(run.RestsRemaining, restored.RestsRemaining);
    }

    [Fact]
    public void AndRollsTheSameDiceAfterwards()
    {
        var library = TestLevel.Library();
        var run = TestLevel.Begin(library);
        run.Use("door", run.Pip());

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.Equal(run.Use("bridge", run.Aldric()).Lines, restored.Use("bridge", restored.Aldric()).Lines);
        Assert.Equal(run.Use("door", run.Pip()).Lines, restored.Use("door", restored.Pip()).Lines);
    }

    [Fact]
    public void ACrossedBridgeStaysCrossed()
    {
        var library = TestLevel.Library(jumpDc: -10);
        var run = TestLevel.Begin(library);
        run.Use("bridge", run.Aldric());

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.False(restored.Field.IsBlocked(TestLevel.Bridge));
        Assert.Equal(new GridSquare(7, 5), restored.Field.SquareOf(restored.Aldric()));
        Assert.Equal("far", restored.Alarm()!.Id);
    }

    [Fact]
    public void ARunSavedMidFightComesBackMidFight()
    {
        var library = TestLevel.Library();
        var run = TestLevel.Begin(library);
        run.Walk(run.Aldric(), new GridSquare(7, 5));
        run.Engage("far");
        run.Battle.AdvanceTurn(Scenarios.AutoPilot(run.Battle));

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.Equal(CampaignState.Fighting, restored.State);
        Assert.Equal("far", restored.CurrentArea!.Id);
        var sleeper = Assert.Single(restored.Battle.Foes);
        Assert.Equal(run.Field.SquareOf(run.Battle.Foes.Single()), restored.Field.SquareOf(sleeper));
        Assert.Equal(["Lurker"], restored.Dormant.Select(one => one.Foe.Name));
        Assert.Same(restored.Field, restored.Battle.Battlefield);
    }

    [Fact]
    public void AndTheFightPlaysOutTheSameEitherWay()
    {
        var library = TestLevel.Library();
        var run = TestLevel.Begin(library);
        run.Walk(run.Aldric(), new GridSquare(7, 5));
        run.Engage("far");
        run.Battle.AdvanceTurn(Scenarios.AutoPilot(run.Battle));

        var restored = Campaign.FromJson(run.ToJson(), library);
        var saved = run.Battle.Log.Count;
        run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
        restored.Battle.RunToCompletion(Scenarios.AutoPilot(restored.Battle), maximumTurns: 400);

        // The log is not saved, so only what happened after the save can be compared.
        Assert.Equal(run.Battle.Log.Skip(saved), restored.Battle.Log);
    }

    [Fact]
    public void AFightWonAfterReloadingIsCollectedAndCleared()
    {
        var library = TestLevel.Library();
        var run = TestLevel.Begin(library);
        run.Engage("far");

        var restored = Campaign.FromJson(run.ToJson(), library);
        TestLevel.Defeat(restored.Battle.Foes);

        Assert.True(restored.Collect() > 0);
        Assert.True(restored.IsCleared("far"));
        Assert.Equal(CampaignState.Exploring, restored.State);
        Assert.Contains(restored.Stash, item => item.Id == "dagger");
    }

    [Fact]
    public void AClearedRoomStaysClearedAndEmpty()
    {
        var library = TestLevel.Library();
        var run = TestLevel.Begin(library);
        run.Engage("far");
        TestLevel.Defeat(run.Battle.Foes);
        run.Collect();

        var restored = Campaign.FromJson(run.ToJson(), library);

        Assert.True(restored.IsCleared("far"));
        Assert.DoesNotContain(restored.Dormant, one => one.Area.Id == "far");
        Assert.False(restored.Engage("far"));
        Assert.Equal(0, restored.Collect());
    }

    [Fact]
    public void ASaveFromBeforeLevelsStillLoads()
    {
        var run = Campaign.Begin(ChainContent.Library, "the-long-road");
        // What an eleven looked like: the same file, without the field twelve added.
        var json = Regex.Replace(run.ToJson(), "\"Version\":\\s*\\d+", "\"Version\": 11");
        json = Regex.Replace(json, ",\\s*\"Level\":\\s*null", string.Empty);

        Assert.DoesNotMatch("\"Level\":\\s*null", json);
        Assert.Contains("\"Version\": 11", json);

        var restored = Campaign.FromJson(json, ChainContent.Library);

        Assert.False(restored.IsLevel);
        Assert.Equal(1, restored.Chapter);
        Assert.Equal(CampaignState.Fighting, restored.State);
    }
}

public class ShippedLevelTests
{
    [Theory]
    [InlineData("caves-of-shadow", 4, 9)]
    [InlineData("the-long-road", 3, 5)]
    public void BothCampaignsOpenOnTheirLevel(string id, int party, int waiting)
    {
        var run = Campaign.Begin(ContentFiles.Default, id);

        Assert.True(run.IsLevel);
        Assert.Equal(CampaignState.Exploring, run.State);
        Assert.Equal(party, run.Party.Count);
        Assert.Equal(waiting, run.Dormant.Count);

        foreach (var (member, start) in run.Party.Zip(run.Level!.Start))
        {
            Assert.Equal(new GridSquare(start.X, start.Y), run.Field.SquareOf(member));
        }
    }

    [Fact]
    public void TheRoadIsAmbushedAtTheStandingStones()
    {
        var run = Campaign.Begin(ContentFiles.Default, "the-long-road");
        var valeria = run.Party.Single(one => one.Name == "Valeria");

        Assert.True(run.Walk(valeria, new GridSquare(8, 6)));
        var alarm = run.Alarm()!;
        Assert.Equal("goblin-ambush", alarm.Id);

        Assert.True(run.Engage(alarm.Id));
        Assert.Equal(4, run.Battle.Foes.Count);
        Assert.All(run.Battle.Foes, foe => Assert.NotNull(run.Field.SquareOf(foe)));
        Assert.Contains(run.Battle.Log, line => line.Contains("lying in wait"));
    }

    [Fact]
    public void TheCaveBridgeCrossesTheCrevice()
    {
        // Seeds are tried until one jumps it rather than one being hunted for and written in,
        // so that a change to the dice does not silently turn this into a test of failing.
        for (var seed = 1UL; seed < 40; seed++)
        {
            var run = Campaign.Begin(ContentFiles.Default, "caves-of-shadow", seed);
            var aldric = run.Party.Single(one => one.Name == "Aldric");
            run.Walk(aldric, new GridSquare(16, 23));

            Assert.True(run.CanUse("rope-bridge", aldric));

            var result = run.Use("rope-bridge", aldric);
            if (!result.Success)
            {
                continue;
            }

            Assert.True(result.MovedTo!.Value.X > 18);
            Assert.False(run.Field.IsBlocked(new GridSquare(17, 23)));
            Assert.NotEmpty(run.Field.FindPath(new GridSquare(15, 23), new GridSquare(20, 23), aldric));
            return;
        }

        Assert.Fail("Nobody got across in forty tries.");
    }

    [Fact]
    public void EveryRoomInTheCavesCanBeFoughtToAFinish()
    {
        var run = Campaign.Begin(ContentFiles.Default, "caves-of-shadow");

        // Not a balance test, any more than the chained version is. The party is put straight
        // into each room rather than walked there, and the autopilot fights it at whatever
        // strength it has left. What it proves is that every room wakes, plays and ends on the
        // level's own ground — nobody stuck in a wall, nobody asleep for ever.
        foreach (var area in run.Level!.Areas.Where(room => room.Foes.Count > 0))
        {
            if (run.State != CampaignState.Exploring)
            {
                break;
            }

            var inside = Enumerable.Range(area.X, area.Width)
                .SelectMany(x => Enumerable.Range(area.Y, area.Height).Select(y => new GridSquare(x, y)))
                .Where(run.Field.IsFree)
                .ToList();

            foreach (var (member, square) in run.Party.Where(one => one.IsConscious).Zip(inside))
            {
                Assert.True(run.Walk(member, square));
            }

            Assert.Equal(area.Id, run.Alarm()!.Id);
            Assert.True(run.Engage(area.Id));

            run.Battle.RunToCompletion(Scenarios.AutoPilot(run.Battle), maximumTurns: 400);
            Assert.NotEqual(BattleOutcome.InProgress, run.Battle.Outcome);

            run.Collect();
            run.Rest();
        }

        Assert.NotEqual(CampaignState.Fighting, run.State);
    }
}

public class LevelExperienceTests
{
    [Fact]
    public void FindingAPlaceIsWorthExperienceTheFirstTimeOnly()
    {
        var run = TestLevel.Begin();
        var before = run.Experience;

        run.Visit("hall");
        run.Visit("hall");

        Assert.Equal(before + AreaDefinition.DefaultExperience, run.Experience);
        var award = Assert.Single(run.TakeAwards());
        Assert.Equal(new ExperienceAward("Discovered The Hall", AreaDefinition.DefaultExperience), award);
        Assert.Empty(run.TakeAwards());
    }

    [Fact]
    public void OpeningADoorIsWorthExperienceAndFailingIsNot()
    {
        var shut = TestLevel.Begin(TestLevel.Library(lockDc: 100));
        var before = shut.Experience;
        shut.Use("door", shut.Pip());
        Assert.Equal(before, shut.Experience);
        Assert.Empty(shut.TakeAwards());

        var open = TestLevel.Begin(TestLevel.Library(lockDc: -10));
        open.Use("door", open.Pip());
        Assert.Equal(before + FeatureDefinition.DefaultExperience(FeatureKind.Door), open.Experience);
        Assert.Equal("Opened the door", Assert.Single(open.TakeAwards()).Why);
    }

    [Fact]
    public void CrossingAndSearchingAreWorthExperienceOnce()
    {
        var run = TestLevel.Begin(TestLevel.Library(jumpDc: -10, climbDc: -10));
        var before = run.Experience;

        run.Use("bridge", run.Aldric());
        run.Walk(run.Pip(), new GridSquare(2, 2));
        run.Use("chest", run.Pip());
        run.Use("chest", run.Pip());

        var awards = run.TakeAwards();
        Assert.Equal(["Crossed the bridge", "Searched the chest"], awards.Select(one => one.Why));
        Assert.Equal(before + awards.Sum(one => one.Amount), run.Experience);
    }

    [Fact]
    public void WinningARoomIsAnAwardOfItsOwn()
    {
        var run = TestLevel.Begin();
        run.Engage("far");
        TestLevel.Defeat(run.Battle.Foes);

        run.Collect();

        Assert.Contains(run.TakeAwards(), award => award.Why == "Won The Far Bank" && award.Amount > 0);
    }
}
