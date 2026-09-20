using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Encounters;

public class InitiativeTests
{
    private static Creature Combatant(string name, int dexterity) =>
        new(name, new AbilityScores(12, dexterity, 12, 10, 10, 10), 20, 2);

    [Fact]
    public void HighestTotalActsFirst()
    {
        var order = Initiative.Roll(
            [Combatant("Slow", 10), Combatant("Quick", 10), Combatant("Middling", 10)],
            new SequenceRandom(5, 18, 11));

        Assert.Equal(["Quick", "Middling", "Slow"], order.Select(c => c.Creature.Name));
        Assert.Equal([18, 11, 5], order.Select(c => c.Initiative));
    }

    [Fact]
    public void DexterityDecidesATie()
    {
        // 12 + 0 and 10 + 2 both come to 12.
        var order = Initiative.Roll(
            [Combatant("Clumsy", 10), Combatant("Nimble", 14)],
            new SequenceRandom(12, 10));

        Assert.Equal(["Nimble", "Clumsy"], order.Select(c => c.Creature.Name));
        Assert.Equal([12, 12], order.Select(c => c.Initiative));
    }

    [Fact]
    public void AnOtherwisePerfectTieKeepsTheOrderItWasGivenIn()
    {
        var order = Initiative.Roll(
            [Combatant("First", 10), Combatant("Second", 10)],
            new SequenceRandom(12, 12));

        Assert.Equal(["First", "Second"], order.Select(c => c.Creature.Name));
    }

    [Fact]
    public void ImprovedInitiativeMovesYouUp()
    {
        var slow = Combatant("Slow", 10);
        var quick = Combatant("Quick", 10);
        quick.InitiativeModifiers.Add(4, BonusType.Untyped, "Improved Initiative");

        var order = Initiative.Roll([slow, quick], new SequenceRandom(14, 12));

        Assert.Equal(["Quick", "Slow"], order.Select(c => c.Creature.Name));
        Assert.Equal(16, order[0].Initiative);
        Assert.Equal(12, order[0].NaturalRoll);
    }

    [Fact]
    public void TheBonusIsExplained()
    {
        var creature = Combatant("Scout", 18);
        creature.InitiativeModifiers.Add(4, BonusType.Untyped, "Improved Initiative");

        Assert.Equal(8, Initiative.Bonus(creature));
        Assert.Equal(
            "+4 untyped (Dex) +4 untyped (Improved Initiative) = 8",
            Initiative.Explain(creature).ToString());
    }

    [Fact]
    public void TheSameSeedProducesTheSameOrder()
    {
        var first = Initiative.Roll(
            [Combatant("A", 10), Combatant("B", 12), Combatant("C", 14)], new PcgRandom(99));
        var second = Initiative.Roll(
            [Combatant("A", 10), Combatant("B", 12), Combatant("C", 14)], new PcgRandom(99));

        Assert.Equal(
            first.Select(c => (c.Creature.Name, c.Initiative)),
            second.Select(c => (c.Creature.Name, c.Initiative)));
    }
}

public class ActionBudgetTests
{
    [Fact]
    public void AFreshTurnHasAllThree()
    {
        var budget = new ActionBudget();

        Assert.True(budget.HasStandard);
        Assert.True(budget.HasMove);
        Assert.True(budget.HasSwift);
        Assert.Equal("standard + move + swift", budget.ToString());
    }

    [Fact]
    public void EachIsSpentIndependently()
    {
        var budget = new ActionBudget();

        Assert.True(budget.Spend(ActionCost.Swift));
        Assert.False(budget.HasSwift);
        Assert.True(budget.HasStandard);
        Assert.True(budget.HasMove);
    }

    [Fact]
    public void AStandardCanBeSpentAsASecondMove()
    {
        var budget = new ActionBudget();

        Assert.True(budget.Spend(ActionCost.Move));
        Assert.True(budget.Spend(ActionCost.Move));

        Assert.False(budget.HasMove);
        Assert.False(budget.HasStandard);
    }

    [Fact]
    public void AMoveIsNeverASecondStandard()
    {
        var budget = new ActionBudget();
        budget.Spend(ActionCost.Standard);

        Assert.False(budget.CanAfford(ActionCost.Standard));
        Assert.False(budget.Spend(ActionCost.Standard));

        // The move survives, because nothing was taken from it.
        Assert.True(budget.HasMove);
    }

    [Fact]
    public void AFullRoundActionTakesBoth()
    {
        var budget = new ActionBudget();

        Assert.True(budget.Spend(ActionCost.FullRound));

        Assert.False(budget.HasStandard);
        Assert.False(budget.HasMove);
        Assert.True(budget.HasSwift);
    }

    [Fact]
    public void AFullRoundActionNeedsBothToStart()
    {
        var budget = new ActionBudget();
        budget.Spend(ActionCost.Move);

        Assert.False(budget.CanAfford(ActionCost.FullRound));
        Assert.True(budget.HasStandard);
    }

    [Fact]
    public void FreeActionsNeverRunOut()
    {
        var budget = new ActionBudget();
        budget.SpendAll();

        Assert.True(budget.CanAfford(ActionCost.Free));
        Assert.True(budget.Spend(ActionCost.Free));
        Assert.True(budget.IsSpent);
        Assert.Equal("nothing left", budget.ToString());
    }

    [Theory]
    [InlineData(ActionCost.Free, 0)]
    [InlineData(ActionCost.Swift, 0)]
    [InlineData(ActionCost.Move, 30)]
    [InlineData(ActionCost.Standard, 30)]
    [InlineData(ActionCost.FullRound, 60)]
    public void EachCostCarriesItsRealTimeDuration(ActionCost cost, int ticks)
    {
        Assert.Equal(Duration.FromTicks(ticks), ActionCosts.DefaultDuration(cost));
    }
}

public class TurnOrderTests
{
    private static Creature Fighter(string name) =>
        new(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);

    /// <summary>Alice 18, Bob 11, Carol 5 — all Dexterity 10, so the rolls decide.</summary>
    private static Encounter ThreeWay(params int[] extraRolls) =>
        new([Fighter("Alice"), Fighter("Bob"), Fighter("Carol")],
            new SequenceRandom([18, 11, 5, .. extraRolls]));

    private static WeaponAttack Sword() =>
        WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing);

    [Fact]
    public void EveryoneActsOncePerRoundInInitiativeOrder()
    {
        var encounter = ThreeWay();

        var names = Enumerable.Range(0, 6)
            .Select(_ => encounter.BeginNextTurn()!.Actor.Name)
            .ToArray();

        Assert.Equal(["Alice", "Bob", "Carol", "Alice", "Bob", "Carol"], names);
    }

    [Fact]
    public void TurnsSitOneTickApartAndARoundIsSixty()
    {
        var encounter = ThreeWay();

        var turns = Enumerable.Range(0, 4).Select(_ => encounter.BeginNextTurn()!).ToArray();

        Assert.Equal([0L, 1L, 2L, 60L], turns.Select(t => t.Tick));
        Assert.Equal([1, 1, 1, 2], turns.Select(t => t.Round));
    }

    [Fact]
    public void EachTurnStartsWithAFreshBudget()
    {
        var encounter = ThreeWay(15, 3);

        var first = encounter.BeginNextTurn()!;
        first.Take(new AttackAction(Sword(), encounter.Order[1].Creature));
        Assert.False(first.Budget.HasStandard);

        var second = encounter.BeginNextTurn()!;
        Assert.True(second.Budget.HasStandard);
    }

    [Fact]
    public void AFallenCombatantIsSkippedAndRejoinsWhenRevived()
    {
        var encounter = ThreeWay();
        var bob = encounter.Order[1].Creature;
        bob.HitPoints.Take(bob.HitPoints.Maximum + 1);
        Assert.False(bob.IsConscious);

        Assert.Equal("Alice", encounter.BeginNextTurn()!.Actor.Name);
        Assert.Equal("Carol", encounter.BeginNextTurn()!.Actor.Name);

        bob.HitPoints.Heal(20);

        Assert.Equal("Alice", encounter.BeginNextTurn()!.Actor.Name);
        Assert.Equal("Bob", encounter.BeginNextTurn()!.Actor.Name);
    }

    [Fact]
    public void WhenNobodyIsLeftStandingThereIsNoNextTurn()
    {
        var encounter = ThreeWay();
        foreach (var combatant in encounter.Order)
        {
            combatant.Creature.HitPoints.Take(combatant.Creature.HitPoints.Maximum + 1);
        }

        Assert.Null(encounter.BeginNextTurn());
    }

    [Fact]
    public void ASummonJoinsTheFightBehindWhoeverIsAlreadyWaiting()
    {
        var encounter = ThreeWay(12);
        encounter.BeginNextTurn();   // Alice, at tick 0

        var wolf = encounter.Add(Fighter("Wolf"));

        Assert.Equal(4, encounter.Order.Count);
        Assert.Equal(12, wolf.NaturalRoll);
        Assert.Equal(1, wolf.NextTurnTick);

        // Bob is already queued for tick 1, and an arrival does not jump an existing combatant.
        Assert.Equal("Bob", encounter.BeginNextTurn()!.Actor.Name);
        Assert.Equal("Wolf", encounter.BeginNextTurn()!.Actor.Name);
        Assert.Equal("Carol", encounter.BeginNextTurn()!.Actor.Name);
    }
}

public class TurnClockTests
{
    private static Creature Fighter(string name) =>
        new(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);

    private static Encounter ThreeWay(params int[] extraRolls) =>
        new([Fighter("Alice"), Fighter("Bob"), Fighter("Carol")],
            new SequenceRandom([18, 11, 5, .. extraRolls]));

    [Fact]
    public void TotalDefenceLastsUntilExactlyYourNextTurn()
    {
        var encounter = ThreeWay();
        var alice = encounter.Order[0].Creature;
        var baseline = alice.ArmorClass.Total;

        encounter.BeginNextTurn()!.Take(new TotalDefenseAction());
        Assert.Equal(baseline + TotalDefenseAction.DodgeBonus, alice.ArmorClass.Total);

        // It has to survive everyone else's turn in between.
        encounter.BeginNextTurn();
        Assert.Equal(baseline + TotalDefenseAction.DodgeBonus, alice.ArmorClass.Total);
        encounter.BeginNextTurn();
        Assert.Equal(baseline + TotalDefenseAction.DodgeBonus, alice.ArmorClass.Total);

        // And end the moment Alice comes round again.
        var back = encounter.BeginNextTurn()!;
        Assert.Equal("Alice", back.Actor.Name);
        Assert.Equal(baseline, alice.ArmorClass.Total);
        Assert.Contains(back.Events, e => e.Kind == EffectEventKind.Expired);
    }

    [Fact]
    public void BleedTicksOnceARoundAndIsReportedAtItsOwnersTurn()
    {
        var encounter = ThreeWay(4);
        var alice = encounter.Order[0].Creature;

        encounter.BeginNextTurn();
        alice.Effects.Apply(new DamageOverTimeEffect(
            "Bleeding", Duration.Rounds(3), "1d6", DamageType.Slashing));

        // Nobody else's turn should make it fire.
        Assert.Empty(encounter.BeginNextTurn()!.Events);
        Assert.Empty(encounter.BeginNextTurn()!.Events);

        var aliceAgain = encounter.BeginNextTurn()!;

        Assert.Equal("Alice", aliceAgain.Actor.Name);
        Assert.Equal(EffectEventKind.Ticked, Assert.Single(aliceAgain.Events).Kind);
        Assert.Equal(48, alice.HitPoints.Current);
    }

    [Fact]
    public void TimeOnlyMovesThroughAdvance()
    {
        var encounter = ThreeWay();
        Assert.Equal(0, encounter.Tick);

        encounter.Advance(Duration.Rounds(2));

        Assert.Equal(120, encounter.Tick);
        Assert.Equal(3, encounter.Round);
    }

    [Fact]
    public void AdvancingByAPermanentSpanIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ThreeWay().Advance(Duration.Permanent));
    }
}

public class TurnActionTests
{
    private static Creature Fighter(string name) =>
        new(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);

    private static WeaponAttack Sword() =>
        WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing);

    /// <summary>Alice acts first; initiative 18 against 11.</summary>
    private static Encounter Duel(params int[] extraRolls) =>
        new([Fighter("Alice"), Fighter("Bob")], new SequenceRandom([18, 11, .. extraRolls]));

    [Fact]
    public void AnAttackProducesItsStrike()
    {
        var encounter = Duel(15, 4);
        var turn = encounter.BeginNextTurn()!;
        var bob = encounter.Order[1].Creature;

        var result = turn.Take(new AttackAction(Sword(), bob));

        var attack = Assert.IsType<AttackActionResult>(result);
        Assert.True(attack.Strike.IsHit);      // 15 + 5 against armour class 10
        Assert.Equal(52 - attack.Strike.DamageDealt, bob.HitPoints.Current);
        Assert.Equal(ActionCost.Standard, attack.Cost);
    }

    [Fact]
    public void ASecondStandardActionIsRefusedAndChangesNothing()
    {
        var encounter = Duel(15, 4);
        var turn = encounter.BeginNextTurn()!;
        var bob = encounter.Order[1].Creature;

        Assert.NotNull(turn.Take(new AttackAction(Sword(), bob)));
        var after = bob.HitPoints.Current;

        Assert.Null(turn.Take(new AttackAction(Sword(), bob)));
        Assert.Equal(after, bob.HitPoints.Current);
        Assert.Single(turn.Taken);
    }

    [Fact]
    public void AnActionThatMakesNoSenseIsRefusedBeforeTheBudgetIsTouched()
    {
        var encounter = Duel();
        var turn = encounter.BeginNextTurn()!;

        Assert.Null(turn.Take(new AttackAction(Sword(), turn.Actor)));

        Assert.True(turn.Budget.HasStandard);
        Assert.Empty(turn.Taken);
    }

    [Fact]
    public void NothingCanBeTakenAfterTheTurnEnds()
    {
        var encounter = Duel();
        var turn = encounter.BeginNextTurn()!;
        var bob = encounter.Order[1].Creature;

        turn.End();

        Assert.Null(turn.Take(new AttackAction(Sword(), bob)));
        Assert.True(turn.IsEnded);
    }

    [Fact]
    public void EndingTwiceIsHarmless()
    {
        var encounter = Duel();
        var turn = encounter.BeginNextTurn()!;

        turn.End();
        turn.End();

        Assert.Equal(60, encounter.Order[0].NextTurnTick);
    }

    [Fact]
    public void ATurnNobodyActsOnSimplyPassesWithoutBlocking()
    {
        var encounter = Duel();

        // An action source with nothing to say, which is what an unattended player looks like.
        static GameAction? Idle(Turn turn) => null;

        var turns = 0;
        for (var i = 0; i < 4; i++)
        {
            var turn = encounter.BeginNextTurn()!;
            while (Idle(turn) is { } action)
            {
                turn.Take(action);
            }

            turn.End();
            turns++;
        }

        // Alice at 0, Bob at 1, Alice at 60, Bob at 61 — the clock moved with nobody doing anything.
        Assert.Equal(4, turns);
        Assert.Equal(61, encounter.Tick);
    }

    [Fact]
    public void BeginningTheNextTurnClosesTheOneBefore()
    {
        var encounter = Duel();
        var first = encounter.BeginNextTurn()!;

        encounter.BeginNextTurn();

        Assert.True(first.IsEnded);
    }

    [Fact]
    public void ActionsCarryTheirRealTimeShapeEvenThoughTurnsIgnoreIt()
    {
        var attack = new AttackAction(Sword(), Fighter("Target"));

        Assert.Equal(Duration.FromTicks(30), attack.Occupies);
        Assert.Equal(Duration.Zero, attack.ResolvesAt);

        // The turn-based scheduler resolves everything at one instant regardless.
        var encounter = Duel(15, 4);
        var turn = encounter.BeginNextTurn()!;
        turn.Take(new AttackAction(Sword(), encounter.Order[1].Creature));

        Assert.Equal(0, encounter.Tick);
    }

    [Fact]
    public void AnActionResultStillPrintsItsLogLineWhenSubclassed()
    {
        var encounter = Duel(15, 4);
        var turn = encounter.BeginNextTurn()!;

        var result = turn.Take(new AttackAction(Sword(), encounter.Order[1].Creature))!;

        // A derived record would otherwise print a dump of its members instead.
        Assert.Equal(result.Description, result.ToString());
        Assert.StartsWith("Alice attacks Bob (sword):", result.ToString());
        Assert.DoesNotContain("Strike = ", result.ToString());
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var encounter = Duel();
        var turn = encounter.BeginNextTurn()!;

        Assert.Throws<ArgumentNullException>(() => turn.Take(null!));
        Assert.Throws<ArgumentNullException>(() => new AttackAction(null!, turn.Actor));
        Assert.Throws<ArgumentNullException>(() => new AttackAction(Sword(), null!));
        Assert.Throws<ArgumentNullException>(() => new Encounter(null!, new SequenceRandom(1)));
        Assert.Throws<ArgumentNullException>(() => encounter.Add(null!));
    }
}

/// <summary>
/// <see cref="Turn.CanTake"/> exists so an interface can offer only what will work. These pin the
/// one property that makes it worth having: it agrees with <see cref="Turn.Take"/>, always.
/// </summary>
public class TurnCanTakeTests
{
    private static Creature Fighter(string name) =>
        new(name, new AbilityScores(18, 10, 14, 10, 10, 10), 40, 6);

    private static WeaponAttack Sword() =>
        WeaponAttack.Create("sword", 5, "1d6", DamageType.Slashing);

    private static Encounter Duel(params int[] extraRolls) =>
        new([Fighter("Alice"), Fighter("Bob")], new SequenceRandom([18, 11, .. extraRolls]));

    [Fact]
    public void AskingDoesNotSpendAnything()
    {
        var turn = Duel(12, 4).BeginNextTurn()!;
        var attack = new AttackAction(Sword(), turn.Encounter.Order[1].Creature);

        Assert.True(turn.CanTake(attack));
        Assert.True(turn.CanTake(attack));

        // Three yeses and the budget is still untouched, or the cursor would cost you your turn.
        Assert.True(turn.Budget.HasStandard);
        Assert.Empty(turn.Taken);
    }

    [Fact]
    public void ARefusedActionIsRefusedByBoth()
    {
        var turn = Duel().BeginNextTurn()!;

        // Nobody can attack themselves, so CanPerform says no.
        var absurd = new AttackAction(Sword(), turn.Actor);

        Assert.False(turn.CanTake(absurd));
        Assert.Null(turn.Take(absurd));
    }

    [Fact]
    public void OnceTheStandardActionIsGoneBothSayNo()
    {
        var turn = Duel(12, 4).BeginNextTurn()!;
        var target = turn.Encounter.Order[1].Creature;

        Assert.True(turn.CanTake(new AttackAction(Sword(), target)));
        Assert.NotNull(turn.Take(new AttackAction(Sword(), target)));

        // This is the case that made the cursor lie: the action is still perfectly sensible,
        // it simply cannot be paid for any more.
        var second = new AttackAction(Sword(), target);
        Assert.False(turn.CanTake(second));
        Assert.Null(turn.Take(second));
    }

    [Fact]
    public void AnEndedTurnAcceptsNothing()
    {
        var turn = Duel().BeginNextTurn()!;
        var attack = new AttackAction(Sword(), turn.Encounter.Order[1].Creature);

        turn.End();

        Assert.False(turn.CanTake(attack));
        Assert.Null(turn.Take(attack));
    }

    [Fact]
    public void NullIsRejectedRatherThanTreatedAsIllegal() =>
        Assert.Throws<ArgumentNullException>(() => Duel().BeginNextTurn()!.CanTake(null!));
}
