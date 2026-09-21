using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Skills;

namespace Ironbound.Simulation.Tests;

public class FlatFootedTests
{
    [Fact]
    public void EverybodyIsCaughtOffGuardUntilTheyHaveHadATurn()
    {
        var battle = Scenarios.GoblinAmbush();

        // The rule that makes initiative worth caring about, and it was simply missing.
        Assert.All(
            battle.Encounter.Order,
            combatant => Assert.True(combatant.IsFlatFooted));
    }

    [Fact]
    public void HavingActedYouAreNoLongerFlatFooted()
    {
        var battle = Scenarios.GoblinAmbush();
        var turn = battle.BeginTurn()!;
        var actor = turn.Actor;

        Assert.True(battle.Encounter.IsFlatFooted(actor));

        battle.EndTurn();

        Assert.False(battle.Encounter.IsFlatFooted(actor));
    }

    [Fact]
    public void ASwingAtSomebodyWhoHasNotActedFindsThemFlatFooted()
    {
        var (turn, actor, target) = Duel();

        var result = Assert.IsType<AttackActionResult>(
            turn.Take(new AttackAction(actor.PrimaryAttack!, target)));

        Assert.Contains("flat-footed", result.Strike!.Attack.ToString());
        Assert.Equal(target.ArmorClass.FlatFooted, result.Strike.Attack.TargetArmorClass);
    }

    private static (Turn Turn, Creature Actor, Creature Target) Duel()
    {
        var field = new Battlefield(8, 4);
        var actor = new Creature("Actor", new AbilityScores(16, 14, 14, 10, 10, 10), 40, 6)
        {
            Allegiance = 1,
            BaseAttackBonus = 6,
        };

        var target = new Creature("Target", new AbilityScores(12, 16, 12, 10, 10, 10), 40, 6)
        {
            Allegiance = 2,
        };

        actor.Attacks.Add(WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing));
        field.Place(actor, 0, 0);
        field.Place(target, 1, 0);

        var encounter = new Encounter(
            [actor, target], new SequenceRandom(true, 20, 1, 12, 4), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, actor, target);
    }
}

public class SurpriseTests
{
    [Fact]
    public void TheOpeningEncounterIsFinallyAnAmbush()
    {
        var battle = Scenarios.GoblinAmbush();

        Assert.Contains(battle.Log, line => line.Contains("lying in wait"));
        Assert.Contains(battle.Log, line => line.Contains("Perception"));
    }

    [Fact]
    public void WhoeverFailsToNoticeLosesTheirFirstTurn()
    {
        var battle = Scenarios.GoblinAmbush();
        var surprised = battle.Encounter.Order
            .Where(combatant => combatant.IsUnaware)
            .ToList();

        Assert.NotEmpty(surprised);

        var victim = surprised[0].Creature;
        var turn = battle.Encounter.Order.First(c => c.IsUnaware);

        // Refused everything, and not charged for any of it.
        Assert.True(turn.Budget.HasStandard);
        Assert.True(battle.Encounter.IsFlatFooted(victim));
    }

    [Fact]
    public void TheHidersRollOnceBetweenThemRatherThanEach()
    {
        var battle = Scenarios.GoblinAmbush();

        // Four goblins were hiding. Rolling per hider would mean the more of them there were,
        // the likelier you were to spot one — which is backwards.
        Assert.Single(battle.Log, line => line.Contains("lying in wait"));
    }

    [Fact]
    public void NobodyIsSurprisedWhereNobodyWasHiding()
    {
        var battle = Scenarios.Build(ContentFiles.Default, "moonlit-clearing");

        Assert.Empty(battle.Log);
        Assert.DoesNotContain(battle.Encounter.Order, combatant => combatant.IsUnaware);
    }

    [Fact]
    public void ActingClearsItEvenIfTheTurnDidNothing()
    {
        var battle = Scenarios.GoblinAmbush();
        var victim = battle.Encounter.Order.First(combatant => combatant.IsUnaware).Creature;

        // Walk the fight forward until the surprised party has had their turn taken from them.
        while (battle.BeginTurn() is { } turn)
        {
            var acting = turn.Actor;
            battle.EndTurn();

            if (ReferenceEquals(acting, victim))
            {
                break;
            }
        }

        Assert.False(battle.Encounter.IsFlatFooted(victim));
    }

    [Fact]
    public void AnAmbushSurvivesASaveAndReload()
    {
        var battle = Scenarios.GoblinAmbush();
        var surprised = battle.Encounter.Order
            .Where(combatant => combatant.IsUnaware)
            .Select(combatant => combatant.Creature.Name)
            .ToList();

        var json = Rules.Persistence.GameSave.ToJson(
            Rules.Persistence.GameSave.Capture(battle.Encounter));
        var reloaded = Battle.Restore(
            Rules.Persistence.GameSave.FromJson(json), ContentFiles.Default);

        // Without this the reload quietly hands everybody their guard back.
        Assert.Equal(
            surprised,
            reloaded.Encounter.Order.Where(c => c.IsUnaware).Select(c => c.Creature.Name));
        Assert.All(reloaded.Encounter.Order, c => Assert.True(c.IsFlatFooted));
    }

    [Fact]
    public void AnAmbushWithNobodyHidingChangesNothing()
    {
        var battle = Scenarios.Build(ContentFiles.Default, "moonlit-clearing");

        Assert.Empty(battle.Ambush([]));
    }
}
