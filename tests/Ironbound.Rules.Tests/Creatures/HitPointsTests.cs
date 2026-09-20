using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Creatures;

public class HitPointsTests
{
    private static AbilityScore Constitution(int score) => new(Ability.Constitution, score);

    /// <summary>Two hit dice, 10 rolled, Constitution 14: a maximum of 14.</summary>
    private static HitPoints Sample(RuleOptions? rules = null) =>
        new(baseHitPoints: 10, hitDice: 2, Constitution(14), rules);

    [Fact]
    public void MaximumCountsConstitutionOncePerHitDie()
    {
        Assert.Equal(14, Sample().Maximum);
        Assert.Equal(14, Sample().Current);
    }

    [Fact]
    public void MaximumNeverFallsBelowOne()
    {
        var hitPoints = new HitPoints(baseHitPoints: 1, hitDice: 2, Constitution(1));

        Assert.Equal(1, hitPoints.Maximum);
    }

    [Fact]
    public void AConstitutionBuffRaisesCurrentAndMaximumTogether()
    {
        var constitution = Constitution(14);
        var hitPoints = new HitPoints(10, 2, constitution);
        hitPoints.Take(4);
        Assert.Equal(10, hitPoints.Current);

        constitution.Modifiers.Add(4, BonusType.Enhancement, "Bear's Endurance");

        // Two hit dice at +2 more each: the wound stays 4 points deep.
        Assert.Equal(18, hitPoints.Maximum);
        Assert.Equal(14, hitPoints.Current);
        Assert.Equal(4, hitPoints.Damage);
    }

    [Fact]
    public void LosingTheBuffTakesTheHitPointsBackWithoutDeepeningTheWound()
    {
        var constitution = Constitution(14);
        var hitPoints = new HitPoints(10, 2, constitution);
        constitution.Modifiers.Add(4, BonusType.Enhancement, "Bear's Endurance");
        hitPoints.Take(4);

        constitution.Modifiers.RemoveAllFrom("Bear's Endurance");

        Assert.Equal(14, hitPoints.Maximum);
        Assert.Equal(10, hitPoints.Current);
    }

    [Fact]
    public void ConstitutionDrainCanBeFatal()
    {
        var constitution = Constitution(14);
        var hitPoints = new HitPoints(10, 2, constitution);
        hitPoints.Take(12);
        Assert.Equal(HitPointState.Healthy, hitPoints.State);

        constitution.Base = 4;

        // Maximum falls to 4, so 12 damage is now 8 past a death threshold of -4.
        Assert.Equal(4, hitPoints.Maximum);
        Assert.Equal(-8, hitPoints.Current);
        Assert.Equal(HitPointState.Dead, hitPoints.State);
    }

    [Fact]
    public void DamageAndHealingMove()
    {
        var hitPoints = Sample();

        Assert.Equal(new DamageApplication(0, 6), hitPoints.Take(6));
        Assert.Equal(8, hitPoints.Current);

        Assert.Equal(4, hitPoints.Heal(4));
        Assert.Equal(12, hitPoints.Current);
    }

    [Fact]
    public void HealingStopsAtFull()
    {
        var hitPoints = Sample();
        hitPoints.Take(3);

        Assert.Equal(3, hitPoints.Heal(100));
        Assert.Equal(14, hitPoints.Current);
        Assert.Equal(0, hitPoints.Damage);
    }

    [Fact]
    public void NothingHealsTheDead()
    {
        var hitPoints = Sample();
        hitPoints.Take(100);
        Assert.Equal(HitPointState.Dead, hitPoints.State);

        Assert.Equal(0, hitPoints.Heal(50));
    }

    [Fact]
    public void NegativeAndZeroAmountsDoNothing()
    {
        var hitPoints = Sample();

        Assert.Equal(default, hitPoints.Take(0));
        Assert.Equal(default, hitPoints.Take(-5));
        Assert.Equal(0, hitPoints.Heal(0));
        Assert.Equal(14, hitPoints.Current);
    }

    // ---- temporary hit points ----

    [Fact]
    public void TemporaryHitPointsAreSpentFirst()
    {
        var hitPoints = Sample();
        hitPoints.GrantTemporary(5);

        var applied = hitPoints.Take(8);

        Assert.Equal(new DamageApplication(5, 3), applied);
        Assert.Equal(8, applied.Total);
        Assert.Equal(0, hitPoints.Temporary);
        Assert.Equal(11, hitPoints.Current);
    }

    [Fact]
    public void TemporaryHitPointsDoNotStack()
    {
        var hitPoints = Sample();

        hitPoints.GrantTemporary(5);
        hitPoints.GrantTemporary(3);
        Assert.Equal(5, hitPoints.Temporary);

        hitPoints.GrantTemporary(8);
        Assert.Equal(8, hitPoints.Temporary);
    }

    [Fact]
    public void HealingDoesNotBringTemporaryHitPointsBack()
    {
        var hitPoints = Sample();
        hitPoints.GrantTemporary(5);
        hitPoints.Take(9);

        hitPoints.Heal(20);

        Assert.Equal(14, hitPoints.Current);
        Assert.Equal(0, hitPoints.Temporary);
    }

    // ---- states ----

    [Theory]
    [InlineData(0, HitPointState.Healthy)]
    [InlineData(13, HitPointState.Healthy)]
    [InlineData(14, HitPointState.Disabled)]
    [InlineData(15, HitPointState.Dying)]
    [InlineData(27, HitPointState.Dying)]
    [InlineData(28, HitPointState.Dead)]
    [InlineData(100, HitPointState.Dead)]
    public void StateFollowsTheWound(int damage, HitPointState state)
    {
        var hitPoints = Sample();
        hitPoints.Take(damage);

        Assert.Equal(state, hitPoints.State);
    }

    [Fact]
    public void DeathWaitsUntilConstitutionInNegatives()
    {
        Assert.Equal(-14, Sample().DeathThreshold);
    }

    [Fact]
    public void ACreatureWithNoConstitutionIsDestroyedAtZero()
    {
        var skeleton = new HitPoints(10, 2, AbilityScore.NonAbility(Ability.Constitution));

        Assert.Equal(10, skeleton.Maximum);
        Assert.Equal(0, skeleton.DeathThreshold);

        skeleton.Take(10);

        Assert.Equal(HitPointState.Dead, skeleton.State);
    }

    [Fact]
    public void WithoutDeathsDoorZeroIsTheEnd()
    {
        var hitPoints = Sample(new RuleOptions { DeathsDoor = false });
        Assert.Equal(0, hitPoints.DeathThreshold);

        hitPoints.Take(14);

        Assert.Equal(HitPointState.Dead, hitPoints.State);
    }

    [Fact]
    public void ConsciousnessTracksState()
    {
        var hitPoints = Sample();
        Assert.True(hitPoints.IsConscious);

        hitPoints.Take(14);
        Assert.True(hitPoints.IsConscious);   // disabled, but still up
        Assert.True(hitPoints.IsAlive);

        hitPoints.Take(1);
        Assert.False(hitPoints.IsConscious);  // dying
        Assert.True(hitPoints.IsAlive);
    }

    // ---- nonlethal ----

    [Fact]
    public void NonlethalDamageIsTrackedApartAndNeverKills()
    {
        var hitPoints = Sample();

        hitPoints.TakeNonlethal(100);

        Assert.Equal(14, hitPoints.Current);
        Assert.Equal(HitPointState.Healthy, hitPoints.State);
        Assert.True(hitPoints.IsAlive);
    }

    [Fact]
    public void MatchingNonlethalStaggersAndExceedingItKnocksOut()
    {
        var hitPoints = Sample();

        hitPoints.TakeNonlethal(14);
        Assert.True(hitPoints.IsStaggeredByNonlethal);
        Assert.False(hitPoints.IsUnconsciousFromNonlethal);
        Assert.True(hitPoints.IsConscious);

        hitPoints.TakeNonlethal(1);
        Assert.True(hitPoints.IsUnconsciousFromNonlethal);
        Assert.False(hitPoints.IsConscious);
    }

    [Fact]
    public void NonlethalHealsSeparately()
    {
        var hitPoints = Sample();
        hitPoints.TakeNonlethal(6);

        Assert.Equal(6, hitPoints.HealNonlethal(10));
        Assert.Equal(0, hitPoints.Nonlethal);
    }

    [Fact]
    public void RestingClearsEverythingButTemporary()
    {
        var hitPoints = Sample();
        hitPoints.Take(9);
        hitPoints.TakeNonlethal(3);

        hitPoints.Restore();

        Assert.Equal(14, hitPoints.Current);
        Assert.Equal(0, hitPoints.Nonlethal);
    }

    // ---- generation ----

    [Fact]
    public void AverageGenerationTakesHalfTheDiePlusOneAfterTheFirst()
    {
        var random = new SequenceRandom(1);

        // d10: 10 at first level, then five levels of 6.
        Assert.Equal(40, HitPoints.RollBase(10, 6, random));
        Assert.Equal(0, random.Consumed);
    }

    [Fact]
    public void MaximumGenerationTakesTheWholeDie()
    {
        var rules = new RuleOptions { HitPointGeneration = HitPointGeneration.Maximum };

        Assert.Equal(60, HitPoints.RollBase(10, 6, new SequenceRandom(1), rules));
    }

    [Fact]
    public void RolledGenerationRollsEveryDieButTheFirst()
    {
        var rules = new RuleOptions { HitPointGeneration = HitPointGeneration.Rolled };
        var random = new SequenceRandom(1, 1, 1, 1, 1);

        Assert.Equal(15, HitPoints.RollBase(10, 6, random, rules));
        Assert.Equal(5, random.Consumed);
    }

    [Fact]
    public void ASingleHitDieIsAlwaysMaximum()
    {
        var rules = new RuleOptions { HitPointGeneration = HitPointGeneration.Rolled };

        Assert.Equal(8, HitPoints.RollBase(8, 1, new SequenceRandom(1), rules));
    }

    // ---- reporting ----

    [Fact]
    public void FormatsForTheCharacterSheet()
    {
        var hitPoints = Sample();
        Assert.Equal("14/14 hp", hitPoints.ToString());

        hitPoints.Take(4);
        Assert.Equal("10/14 hp", hitPoints.ToString());

        hitPoints.GrantTemporary(5);
        Assert.Equal("10/14 hp (+5 temp)", hitPoints.ToString());

        hitPoints.TakeNonlethal(2);
        Assert.Equal("10/14 hp (+5 temp) (2 nonlethal)", hitPoints.ToString());

        hitPoints.RemoveTemporary();
        hitPoints.Take(12);
        Assert.Equal("-2/14 hp (2 nonlethal), dying", hitPoints.ToString());
    }

    [Fact]
    public void RejectsImpossibleConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HitPoints(-1, 1, Constitution(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HitPoints(10, 0, Constitution(10)));
        Assert.Throws<ArgumentNullException>(() => new HitPoints(10, 1, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Sample().Base = -1);
    }
}
