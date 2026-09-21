using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Encounters;
using Ironbound.Rules.Encounters.Actions;
using Ironbound.Rules.Maps;
using Ironbound.Rules.Content;
using Ironbound.Rules.Persistence;
using Ironbound.Rules.Skills;

namespace Ironbound.Rules.Tests.Effects;

public class BleedingOutTests
{
    [Fact]
    public void GoingBelowNoughtStartsTheClock()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -3);

        encounter.BeginNextTurn();

        Assert.Equal(HitPointState.Dying, victim.HitPoints.State);
        Assert.NotNull(Bleeding.Find(victim));
    }

    [Fact]
    public void APointARoundComesOffUntilSomethingStops()
    {
        // Constitution 8 and a run of low rolls: nothing is going to stabilise.
        var (encounter, victim, _) = Skirmish(hurtTo: -1, constitution: 8, rolls: [2]);

        Advance(encounter, rounds: 3);

        Assert.True(victim.HitPoints.Current <= -4);
    }

    [Fact]
    public void AGoodCheckStopsIt()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -1, rolls: [20]);

        Advance(encounter, rounds: 2);

        Assert.True(Bleeding.IsStable(victim));
        Assert.Equal(HitPointState.Dying, victim.HitPoints.State);
    }

    [Fact]
    public void StableMeansStableAndStopsSayingSo()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -1, rolls: [20]);

        Advance(encounter, rounds: 2);
        var settled = victim.HitPoints.Current;

        var quiet = Advance(encounter, rounds: 3);

        Assert.Equal(settled, victim.HitPoints.Current);
        Assert.DoesNotContain(quiet, line => line.Contains(victim.Name));
    }

    [Fact]
    public void EnoughOfItKillsYou()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -1, constitution: 8, rolls: [1]);

        Advance(encounter, rounds: 14);

        Assert.Equal(HitPointState.Dead, victim.HitPoints.State);
        Assert.False(victim.IsAlive);
    }

    [Fact]
    public void HealingAboveNoughtEndsItAltogether()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -3);
        encounter.BeginNextTurn();

        Assert.NotNull(Bleeding.Find(victim));

        victim.HitPoints.Heal(20);
        Advance(encounter, rounds: 1);

        Assert.Null(Bleeding.Find(victim));
    }

    [Fact]
    public void ItSurvivesASaveWithItsStabilityIntact()
    {
        var (encounter, victim, _) = Skirmish(hurtTo: -1, rolls: [20]);
        Advance(encounter, rounds: 2);

        Assert.True(Bleeding.IsStable(victim));

        var restored = GameSave.Restore(
            GameSave.FromJson(
                GameSave.ToJson(GameSave.Capture(encounter))),
            ContentLibrary.Load([]))
            .Order.Single(c => c.Creature.Name == victim.Name).Creature;

        Assert.True(Bleeding.IsStable(restored));
    }

    internal static IReadOnlyList<string> Advance(Encounter encounter, int rounds)
    {
        var lines = new List<string>();

        for (var i = 0; i < rounds * 2; i++)
        {
            if (encounter.BeginNextTurn() is not { } turn)
            {
                break;
            }

            lines.AddRange(turn.Events.Select(happened => happened.Description));
            turn.End();
        }

        return lines;
    }

    internal static (Encounter Encounter, Creature Victim, Creature Other) Skirmish(
        int hurtTo, int constitution = 12, int[]? rolls = null, int healRanks = 0)
    {
        var field = new Battlefield(8, 4);

        var victim = new Creature(
            "Victim", new AbilityScores(12, 12, constitution, 10, 10, 10), 20, 3) { Allegiance = 1 };

        var other = new Creature("Other", new AbilityScores(12, 12, 12, 10, 14, 10), 30, 3)
        {
            Allegiance = 1,
            BaseAttackBonus = 3,
        };

        other.Skills.SetRanks(Skill.Heal, healRanks);
        other.Attacks.Add(WeaponAttack.Melee("club", "1d6", DamageType.Bludgeoning));

        field.Place(victim, 0, 0);
        field.Place(other, 1, 0);

        // One value for everything, initiative included. A looping sequence otherwise feeds
        // the initiative rolls back in as stabilisation checks, and the test starts depending
        // on how many turns happened to have gone by.
        var encounter = new Encounter(
            [victim, other],
            new SequenceRandom(true, rolls ?? [10]),
            rules: null,
            battlefield: field);

        victim.HitPoints.Take(victim.HitPoints.Maximum - hurtTo);

        return (encounter, victim, other);
    }
}

public class StabilisingTests
{
    [Fact]
    public void KneelingBesideThemStopsTheBleeding()
    {
        // Ten fails the victim's own check at -3 and clears the healer's DC 15 comfortably.
        var (encounter, victim, healer) = BleedingOutTests.Skirmish(
            hurtTo: -3, rolls: [10], healRanks: 8);

        var turn = encounter.BeginNextTurn()!;
        while (!ReferenceEquals(turn.Actor, healer))
        {
            turn.End();
            turn = encounter.BeginNextTurn()!;
        }

        var result = Assert.IsType<StabiliseResult>(turn.Take(new StabiliseAction(victim)));

        Assert.True(result.Stabilised);
        Assert.True(Bleeding.IsStable(victim));
        Assert.Contains("stops bleeding", result.Description);
    }

    [Fact]
    public void YouCannotHelpSomebodyWhoIsNotDying()
    {
        var (encounter, _, healer) = BleedingOutTests.Skirmish(hurtTo: 5, healRanks: 8);
        var turn = encounter.BeginNextTurn()!;
        var upright = turn.Encounter.Order.Select(c => c.Creature).First(c => c.IsConscious);

        Assert.False(turn.CanTake(new StabiliseAction(upright)));
    }

    [Fact]
    public void NorSomebodyAlreadyStable()
    {
        var (encounter, victim, healer) = BleedingOutTests.Skirmish(
            hurtTo: -1, rolls: [20], healRanks: 8);

        BleedingOutTests.Advance(encounter, rounds: 2);

        Assert.True(Bleeding.IsStable(victim));

        var turn = encounter.BeginNextTurn()!;
        Assert.False(turn.CanTake(new StabiliseAction(victim)));
    }

    [Fact]
    public void ItIsAStandardActionAndCostsOne()
    {
        var (encounter, victim, healer) = BleedingOutTests.Skirmish(
            hurtTo: -3, rolls: [10], healRanks: 8);

        var turn = encounter.BeginNextTurn()!;
        while (!ReferenceEquals(turn.Actor, healer))
        {
            turn.End();
            turn = encounter.BeginNextTurn()!;
        }

        turn.Take(new StabiliseAction(victim));

        Assert.False(turn.Budget.HasStandard);
    }
}
