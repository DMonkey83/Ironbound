using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
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

    private static Loadout Armed(Creature creature) =>
        new(creature, WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing));

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
        Assert.Equal(turns.SelectMany(turn => turn.Lines), battle.Log);
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

    private static Loadout Armed(Creature creature) =>
        new(creature, WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing));

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

        Assert.Same(wounded, Assert.IsType<AttackAction>(action).Target);
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
        Assert.Same(healthy, Assert.IsType<AttackAction>(action).Target);
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
