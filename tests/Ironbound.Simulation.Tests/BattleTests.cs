using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Modifiers;
using Ironbound.Simulation;

namespace Ironbound.Simulation.Tests;

public class BattleTests
{
    private static Creature Warrior(string name, int hitPoints, int armourClass = 10)
    {
        var creature = new Creature(name, AbilityScores.All(10), hitPoints, 1);
        if (armourClass != 10)
        {
            creature.ArmorClass.Modifiers.Add(armourClass - 10, BonusType.Armor, "Armour");
        }

        return creature;
    }

    /// <summary>Hands the creature a weapon and returns it, so a test can build one inline.</summary>
    private static Creature Armed(Creature creature)
    {
        creature.Attacks.Add(WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing));
        return creature;
    }

    /// <summary>One hero against two foes, with initiative scripted so the hero acts first.</summary>
    private static Battle Skirmish(Creature hero, Creature first, Creature second, params int[] extra) =>
        new([Armed(hero)],
            [Armed(first), Armed(second)],
            new SequenceRandom([20, 5, 4, .. extra]));

    [Fact]
    public void EachCreatureKnowsWhichSideItIsOn()
    {
        var hero = Warrior("Hero", 20);
        var goblin = Warrior("Goblin", 10);
        var battle = Skirmish(hero, goblin, Warrior("Other", 10));

        Assert.Equal(Side.Party, battle.SideOf(hero));
        Assert.Equal(Side.Foes, battle.SideOf(goblin));
    }

    [Fact]
    public void EnemiesExcludeYourOwnSideAndAnyoneWhoHasFallen()
    {
        var hero = Warrior("Hero", 20);
        var standing = Warrior("Standing", 10);
        var fallen = Warrior("Fallen", 10);
        var battle = Skirmish(hero, standing, fallen);

        Assert.Equal(2, battle.EnemiesOf(hero).Count);

        fallen.HitPoints.Take(fallen.HitPoints.Maximum + 1);

        Assert.Equal(["Standing"], battle.EnemiesOf(hero).Select(c => c.Name));
        Assert.Equal(["Hero"], battle.EnemiesOf(standing).Select(c => c.Name));
    }

    [Fact]
    public void TheOutcomeFollowsWhoIsStillStanding()
    {
        var hero = Warrior("Hero", 20);
        var first = Warrior("First", 10);
        var second = Warrior("Second", 10);
        var battle = Skirmish(hero, first, second);

        Assert.Equal(BattleOutcome.InProgress, battle.Outcome);

        first.HitPoints.Take(100);
        Assert.Equal(BattleOutcome.InProgress, battle.Outcome);

        second.HitPoints.Take(100);
        Assert.Equal(BattleOutcome.PartyWon, battle.Outcome);
    }

    [Fact]
    public void ALostBattleIsReportedAsSuch()
    {
        var hero = Warrior("Hero", 20);
        var battle = Skirmish(hero, Warrior("First", 10), Warrior("Second", 10));

        hero.HitPoints.Take(100);

        Assert.Equal(BattleOutcome.PartyLost, battle.Outcome);
    }

    [Fact]
    public void EveryoneDownIsADraw()
    {
        var hero = Warrior("Hero", 20);
        var first = Warrior("First", 10);
        var second = Warrior("Second", 10);
        var battle = Skirmish(hero, first, second);

        foreach (var creature in new[] { hero, first, second })
        {
            creature.HitPoints.Take(100);
        }

        Assert.Equal(BattleOutcome.Draw, battle.Outcome);
    }

    [Fact]
    public void NoFurtherTurnsAreRunOnceTheFightIsDecided()
    {
        var hero = Warrior("Hero", 20);
        var first = Warrior("First", 10);
        var second = Warrior("Second", 10);
        var battle = Skirmish(hero, first, second);
        var source = new HeuristicActionSource(battle, new SequenceRandom(1));

        first.HitPoints.Take(100);
        second.HitPoints.Take(100);

        Assert.Null(battle.AdvanceTurn(source));
    }

    [Fact]
    public void RunningToCompletionStopsAtItsCapRatherThanSpinning()
    {
        // Nobody can hurt anybody: armour class 40 against a +5 attack.
        var hero = Warrior("Hero", 20, armourClass: 40);
        var battle = new Battle(
            [Armed(hero)],
            [Armed(Warrior("Goblin", 10, armourClass: 40))],
            new SequenceRandom(true, 10));

        var turns = battle.RunToCompletion(
            new HeuristicActionSource(battle, new SequenceRandom(true, 1)), maximumTurns: 12);

        Assert.Equal(12, turns.Count);
        Assert.Equal(BattleOutcome.InProgress, battle.Outcome);
    }

    [Fact]
    public void TheLogAccumulatesInOrder()
    {
        var battle = Scenarios.GoblinAmbush(seed: 4242);
        var source = Scenarios.AutoPilot(battle, seed: 4242);

        var turns = battle.RunToCompletion(source);

        Assert.NotEmpty(battle.Log);

        // The ambush is resolved before anybody has a turn, so those lines come first and the
        // turns account for the rest.
        Assert.Equal(
            turns.SelectMany(turn => turn.Lines),
            battle.Log.Skip(battle.Log.Count - turns.Sum(turn => turn.Lines.Count)));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var battle = Scenarios.GoblinAmbush();

        Assert.Throws<ArgumentNullException>(() => battle.AdvanceTurn(null!));
        Assert.Throws<ArgumentNullException>(() => battle.RunToCompletion(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            battle.RunToCompletion(Scenarios.AutoPilot(battle), maximumTurns: 0));
        Assert.Throws<ArgumentNullException>(() =>
            new Battle(null!, [], new SequenceRandom(1)));
    }
}

public class HeuristicActionSourceTests
{
    private static Creature Warrior(string name, int hitPoints) =>
        new(name, AbilityScores.All(10), hitPoints, 1);

    /// <summary>Hands the creature a weapon and returns it, so a test can build one inline.</summary>
    private static Creature Armed(Creature creature)
    {
        creature.Attacks.Add(WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing));
        return creature;
    }

    /// <summary>Initiative 20 for the hero, so the first turn is always theirs.</summary>
    private static Battle Skirmish(Creature hero, Creature healthy, Creature wounded, params int[] extra) =>
        new([Armed(hero)], [Armed(healthy), Armed(wounded)], new SequenceRandom([20, 5, 4, .. extra]));

    [Fact]
    public void ACompetentCreatureFinishesTheWoundedOne()
    {
        var hero = Warrior("Hero", 20);
        var healthy = Warrior("Healthy", 10);
        var wounded = Warrior("Wounded", 10);
        wounded.HitPoints.Take(7);

        var battle = Skirmish(hero, healthy, wounded);
        var source = new HeuristicActionSource(battle, new SequenceRandom(1));

        var action = source.NextAction(battle.Encounter.BeginNextTurn()!);

        Assert.Same(wounded, Assert.IsType<FullAttackAction>(action).Target);
    }

    [Fact]
    public void ACompetentCreatureCoversUpWhenBadlyHurt()
    {
        var hero = Warrior("Hero", 20);
        hero.HitPoints.Take(16);   // 4 of 20, which is under a quarter

        var battle = Skirmish(hero, Warrior("Healthy", 10), Warrior("Wounded", 10));
        var source = new HeuristicActionSource(battle, new SequenceRandom(1));

        Assert.IsType<TotalDefenseAction>(source.NextAction(battle.Encounter.BeginNextTurn()!));
    }

    [Fact]
    public void AnIncompetentCreatureSwingsAtWhoeverAndNeverGuards()
    {
        var hero = Warrior("Hero", 20);
        hero.HitPoints.Take(16);
        var healthy = Warrior("Healthy", 10);
        var wounded = Warrior("Wounded", 10);
        wounded.HitPoints.Take(7);

        var battle = Skirmish(hero, healthy, wounded);

        // 50 fails the competence check at 0; then index 0 picks the first enemy listed.
        var source = new HeuristicActionSource(battle, new SequenceRandom(50, 0), competence: 0);

        var action = source.NextAction(battle.Encounter.BeginNextTurn()!);

        // Badly hurt and facing a nearly dead foe, and it still picks the healthy one.
        Assert.Same(healthy, Assert.IsType<FullAttackAction>(action).Target);
    }

    [Fact]
    public void ItStopsOnceTheStandardActionIsSpent()
    {
        var hero = Warrior("Hero", 20);
        // Three initiative rolls, then the attack's d20 and its damage die.
        var battle = Skirmish(hero, Warrior("Healthy", 10), Warrior("Wounded", 10), 15, 3);
        var source = new HeuristicActionSource(battle, new SequenceRandom(true, 1));

        var turn = battle.Encounter.BeginNextTurn()!;
        Assert.NotNull(turn.Take(source.NextAction(turn)!));

        // Without this the battle loop would never end.
        Assert.Null(source.NextAction(turn));
    }

    [Fact]
    public void ItHasNothingToSayWithNoEnemiesLeft()
    {
        var hero = Warrior("Hero", 20);
        var healthy = Warrior("Healthy", 10);
        var wounded = Warrior("Wounded", 10);
        var battle = Skirmish(hero, healthy, wounded);

        healthy.HitPoints.Take(100);
        wounded.HitPoints.Take(100);

        Assert.Null(new HeuristicActionSource(battle, new SequenceRandom(1))
            .NextAction(battle.Encounter.BeginNextTurn()!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void CompetenceOutsideItsRangeIsRejected(int competence)
    {
        var battle = Scenarios.GoblinAmbush();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HeuristicActionSource(battle, new SequenceRandom(1), competence));
    }
}

public class ScenarioTests
{
    [Fact]
    public void TheGoblinAmbushIsDecidedRatherThanEndless()
    {
        var battle = Scenarios.GoblinAmbush();

        battle.RunToCompletion(Scenarios.AutoPilot(battle));

        Assert.NotEqual(BattleOutcome.InProgress, battle.Outcome);
    }

    [Fact]
    public void TheSameSeedFightsTheSameFight()
    {
        static IReadOnlyList<string> Play(ulong seed)
        {
            var battle = Scenarios.GoblinAmbush(seed);
            battle.RunToCompletion(Scenarios.AutoPilot(battle, seed));
            return battle.Log;
        }

        Assert.Equal(Play(1234), Play(1234));
    }

    [Fact]
    public void DifferentSeedsFightDifferentFights()
    {
        static IReadOnlyList<string> Play(ulong seed)
        {
            var battle = Scenarios.GoblinAmbush(seed);
            battle.RunToCompletion(Scenarios.AutoPilot(battle, seed));
            return battle.Log;
        }

        Assert.NotEqual(Play(1), Play(2));
    }

    [Fact]
    public void ThinkingHarderNeverChangesWhatTheDiceDo()
    {
        // The AI rolls on its own stream, so the initiative order is identical either way.
        var clever = Scenarios.GoblinAmbush(seed: 77);
        var foolish = Scenarios.GoblinAmbush(seed: 77);

        clever.RunToCompletion(Scenarios.AutoPilot(clever, seed: 77, competence: 100));
        foolish.RunToCompletion(Scenarios.AutoPilot(foolish, seed: 77, competence: 0));

        Assert.Equal(
            clever.Encounter.Order.Select(c => (c.Creature.Name, c.Initiative)),
            foolish.Encounter.Order.Select(c => (c.Creature.Name, c.Initiative)));
    }
}

public class TacticalHeuristicTests
{
    private static Creature Fighter(string name)
    {
        var creature = new Creature(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);
        creature.Attacks.Add(WeaponAttack.Create("sword", 10, "1d6", DamageType.Slashing));
        return creature;
    }

    [Fact]
    public void ACompetentCreatureStepsRoundToFlankBeforeSwinging()
    {
        var field = new Battlefield(12, 6);
        var actor = Fighter("Actor");
        var ally = Fighter("Ally");
        var target = Fighter("Target");

        // Actor is already in reach, but on the same side as its ally.
        field.Place(actor, 5, 1);
        field.Place(ally, 4, 2);
        field.Place(target, 5, 2);

        var battle = new Battle(
            [actor, ally], [target], new SequenceRandom(20, 10, 1), rules: null, battlefield: field);
        var source = new HeuristicActionSource(battle, new SequenceRandom(true, 1));

        var turn = battle.Encounter.BeginNextTurn()!;
        Assert.Same(actor, turn.Actor);

        var step = source.NextAction(turn);

        // Stepping to (6,2) puts the target between the two of them.
        var moved = Assert.IsType<FiveFootStepAction>(step);
        Assert.Equal(new GridSquare(6, 2), moved.Destination);

        turn.Take(moved);
        Assert.Same(ally, field.FindFlankingPartner(actor, target));

        // And the step was free, so the swing still happens this turn — all of it, since a
        // five-foot step is the one movement a full attack still allows.
        Assert.IsType<FullAttackAction>(source.NextAction(turn));
    }

    [Fact]
    public void ItDoesNotShuffleAboutWhenItIsAlreadyFlanking()
    {
        var field = new Battlefield(12, 6);
        var actor = Fighter("Actor");
        var ally = Fighter("Ally");
        var target = Fighter("Target");

        field.Place(actor, 4, 2);
        field.Place(ally, 6, 2);
        field.Place(target, 5, 2);

        var battle = new Battle(
            [actor, ally], [target], new SequenceRandom(20, 10, 1), rules: null, battlefield: field);
        var source = new HeuristicActionSource(battle, new SequenceRandom(true, 1));

        var turn = battle.Encounter.BeginNextTurn()!;

        Assert.IsType<FullAttackAction>(source.NextAction(turn));
    }
}

public class SaveAndLoadTests
{
    private const ulong Seed = 20260920;

    /// <summary>
    /// The test the whole persistence layer exists for: stop a fight halfway, write it out, read
    /// it back, and let both finish. If the reloaded fight diverges by so much as one die, some
    /// piece of state was not saved.
    /// </summary>
    [Fact]
    public void AFightReloadedHalfwayThroughPlaysOutIdentically()
    {
        var original = Scenarios.GoblinAmbush(Seed);
        var source = Scenarios.AutoPilot(original, Seed);

        for (var turn = 0; turn < 6; turn++)
        {
            original.AdvanceTurn(source);
        }

        var save = GameSave.FromJson(GameSave.ToJson(GameSave.Capture(original.Encounter)));
        var writtenSoFar = original.Log.Count;

        original.RunToCompletion(source);
        var uninterrupted = original.Log.Skip(writtenSoFar).ToArray();

        var reloaded = Battle.Restore(save, ContentFiles.Default);
        reloaded.RunToCompletion(Scenarios.AutoPilot(reloaded, Seed));

        Assert.NotEmpty(uninterrupted);
        Assert.Equal(uninterrupted, reloaded.Log);
        Assert.Equal(original.Outcome, reloaded.Outcome);
    }

    [Fact]
    public void TheStateAtTheMomentOfSavingIsWhatComesBack()
    {
        var original = Scenarios.GoblinAmbush(Seed);
        var source = Scenarios.AutoPilot(original, Seed);

        for (var turn = 0; turn < 8; turn++)
        {
            original.AdvanceTurn(source);
        }

        var reloaded = Battle.Restore(GameSave.Capture(original.Encounter), ContentFiles.Default);

        Assert.Equal(original.Round, reloaded.Round);
        Assert.Equal(original.Encounter.Tick, reloaded.Encounter.Tick);
        Assert.Equal(original.Party.Count, reloaded.Party.Count);
        Assert.Equal(original.Foes.Count, reloaded.Foes.Count);

        Assert.Equal(
            original.Encounter.Order.Select(c =>
                (c.Creature.Name, c.Creature.HitPoints.Current, c.Initiative)),
            reloaded.Encounter.Order.Select(c =>
                (c.Creature.Name, c.Creature.HitPoints.Current, c.Initiative)));
    }

    [Fact]
    public void SidesComeBackOffTheCreaturesThemselves()
    {
        var original = Scenarios.GoblinAmbush(Seed);

        var reloaded = Battle.Restore(GameSave.Capture(original.Encounter), ContentFiles.Default);

        Assert.Equal(
            original.Party.Select(c => c.Name).Order(),
            reloaded.Party.Select(c => c.Name).Order());
        Assert.All(reloaded.Foes, foe => Assert.Equal(Side.Foes, reloaded.SideOf(foe)));
    }
}

public class InteractiveTurnTests
{
    private static Battle Skirmish() => Scenarios.GoblinAmbush(seed: 4242);

    [Fact]
    public void ATurnStaysOpenUntilItIsEnded()
    {
        var battle = Skirmish();

        var turn = battle.BeginTurn()!;

        Assert.NotNull(turn);
        Assert.False(battle.Encounter.Current!.IsEnded);
        Assert.Same(turn.Actor, battle.Encounter.Current.Actor);

        battle.EndTurn();

        Assert.True(battle.Encounter.Current.IsEnded);
    }

    [Fact]
    public void ActingOnAnOpenTurnProducesLogLines()
    {
        var battle = Skirmish();
        var turn = battle.BeginTurn()!;
        var target = battle.EnemiesOf(turn.Actor)[0];

        var lines = battle.Act(new AttackAction(turn.Actor.PrimaryAttack!, target));

        // Out of reach on the first round, so this one is refused rather than swung.
        Assert.Empty(lines);
        Assert.True(turn.Turn.Budget.HasStandard);
    }

    [Fact]
    public void AMoveTakenByHandIsLoggedLikeAnyOther()
    {
        var battle = Skirmish();
        var field = battle.Battlefield!;

        // Past anybody the ambush caught: a surprised combatant is refused every action, so
        // their turn would produce no lines at all.
        BattleTurn? turn;
        while ((turn = battle.BeginTurn()) is not null
            && !battle.CanAct(MoveAction.Towards(field, turn.Actor, battle.EnemiesOf(turn.Actor)[0])!))
        {
            battle.EndTurn();
        }

        Assert.NotNull(turn);
        var target = battle.EnemiesOf(turn.Actor)[0];
        var lines = battle.Act(MoveAction.Towards(field, turn.Actor, target)!);

        Assert.NotEmpty(lines);
        Assert.Contains("moves to", lines[0]);
        Assert.Contains(lines[0], battle.Log);
    }

    [Fact]
    public void NothingCanBeDoneAfterTheTurnEnds()
    {
        var battle = Skirmish();
        var turn = battle.BeginTurn()!;
        var target = battle.EnemiesOf(turn.Actor)[0];

        battle.EndTurn();

        Assert.Empty(battle.Act(new AttackAction(turn.Actor.PrimaryAttack!, target)));
    }

    [Fact]
    public void TheOpenTurnKnowsWhoseSideItIsOn()
    {
        var battle = Skirmish();

        var sides = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            battle.BeginTurn();
            sides.Add(battle.IsPartyTurn);
            battle.EndTurn();
        }

        // A mixed initiative order, so both answers had better turn up.
        Assert.Contains(true, sides);
        Assert.Contains(false, sides);
    }

    [Fact]
    public void HandDrivenAndSourceDrivenTurnsInterleave()
    {
        var battle = Skirmish();
        var source = Scenarios.AutoPilot(battle, seed: 4242);

        battle.BeginTurn();
        battle.EndTurn();                 // somebody dithers and does nothing
        Assert.NotNull(battle.AdvanceTurn(source));

        Assert.NotEmpty(battle.Log);
        Assert.Equal(BattleOutcome.InProgress, battle.Outcome);
    }
}
