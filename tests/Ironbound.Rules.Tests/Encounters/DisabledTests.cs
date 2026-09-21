using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;

namespace Ironbound.Rules.Tests.Encounters;

public class DisabledTests
{
    [Fact]
    public void ExactlyNoughtIsStandingButOnlyJust()
    {
        var werewolf = Wounded(to: 0);

        // Not a bug and not a corpse: nought hit points is disabled, which is conscious.
        Assert.Equal(HitPointState.Disabled, werewolf.HitPoints.State);
        Assert.True(werewolf.IsConscious);
        Assert.True(werewolf.IsAlive);
    }

    [Fact]
    public void ItBuysOneActionAndNotTwo()
    {
        var (turn, _, _) = Skirmish(to: 0);

        Assert.True(turn.Budget.IsSingleAction);
        Assert.True(turn.Budget.CanAfford(ActionCost.Standard));
        Assert.True(turn.Budget.CanAfford(ActionCost.Move));

        turn.Budget.Spend(ActionCost.Move);

        // Whichever of the two it was, it was the only one.
        Assert.False(turn.Budget.CanAfford(ActionCost.Standard));
        Assert.False(turn.Budget.CanAfford(ActionCost.Move));
    }

    [Fact]
    public void AndNeverAFullAttack()
    {
        var (turn, _, enemy) = Skirmish(to: 0);

        Assert.False(turn.Budget.CanAfford(ActionCost.FullRound));
        Assert.False(turn.CanTake(new FullAttackAction(enemy)));
    }

    [Fact]
    public void SwingingAtSomebodyReopensTheWound()
    {
        var (turn, actor, enemy) = Skirmish(to: 0);

        Assert.NotNull(turn.Take(new AttackAction(actor.PrimaryAttack!, enemy)));

        // The rule that makes standing at zero a decision rather than a free extra round.
        Assert.Equal(-1, actor.HitPoints.Current);
        Assert.Equal(HitPointState.Dying, actor.HitPoints.State);
    }

    [Fact]
    public void WalkingAwayCostsNothingButTheTurn()
    {
        var (turn, actor, _) = Skirmish(to: 0, apart: 3);
        var field = turn.Encounter.Battlefield!;

        var path = field.FindPath(new GridSquare(0, 0), new GridSquare(1, 1), actor);
        Assert.NotNull(turn.Take(new MoveAction(path)));

        // A move action is not strenuous, so it does not open anything.
        Assert.Equal(0, actor.HitPoints.Current);
        Assert.Equal(HitPointState.Disabled, actor.HitPoints.State);
    }

    [Fact]
    public void AHealthyCreatureIsRestrictedByNoneOfThis()
    {
        var (turn, _, enemy) = Skirmish(to: 20);

        Assert.False(turn.Budget.IsSingleAction);
        Assert.True(turn.CanTake(new FullAttackAction(enemy)));
    }

    [Fact]
    public void TheBudgetSaysWhyItIsSoShort()
    {
        var (turn, _, _) = Skirmish(to: 0);

        Assert.Contains("one action only", turn.Budget.ToString());
    }

    [Fact]
    public void ResettingATurnClearsTheRestriction()
    {
        var budget = new ActionBudget();
        budget.RestrictToSingleAction();

        Assert.True(budget.IsSingleAction);

        budget.Reset();

        Assert.False(budget.IsSingleAction);
        Assert.True(budget.CanAfford(ActionCost.FullRound));
    }

    private static Creature Wounded(int to)
    {
        var creature = new Creature("Grey Fang", new AbilityScores(18, 14, 16, 10, 12, 10), 40, 6);
        creature.HitPoints.Take(creature.HitPoints.Maximum - to);

        return creature;
    }

    private static (Turn Turn, Creature Actor, Creature Enemy) Skirmish(int to, int apart = 1)
    {
        var field = new Battlefield(8, 4);
        var actor = Wounded(to);
        actor.Allegiance = 1;
        actor.BaseAttackBonus = 6;
        actor.Attacks.Add(WeaponAttack.Melee("bite", "1d8", DamageType.Piercing));

        var enemy = new Creature("Hunter", new AbilityScores(14, 14, 14, 10, 10, 10), 40, 6)
        {
            Allegiance = 2,
        };

        field.Place(actor, 0, 0);
        field.Place(enemy, apart, 0);

        var encounter = new Encounter(
            [actor, enemy], new SequenceRandom(true, 20, 1, 15, 4), rules: null, battlefield: field);

        return (encounter.BeginNextTurn()!, actor, enemy);
    }
}
