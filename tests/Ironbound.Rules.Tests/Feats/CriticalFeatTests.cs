using Ironbound.Rules.Combat;
using Ironbound.Rules.Conditions;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Tests.Classes;

namespace Ironbound.Rules.Tests.Feats;

/// <summary>What the critical feats do once a critical hit is confirmed.</summary>
public class CriticalFeatTests
{
    /// <summary>A natural 20, a natural 20 to confirm, and fours on both of the sword's dice.</summary>
    private static readonly int[] Critical = [20, 20, 4, 4];

    private static Creature Brute(string feats, string weapon = "longsword") =>
        ClassKit.Make("fighter", 17, $"\"items\": [\"{weapon}\"], \"feats\": [\"critical-focus\", {feats}]");

    private static StrikeResult Crit(Creature brute, Creature target, params int[] after) =>
        Strike.Resolve(brute, brute.MeleeAttack!, target, new SequenceRandom([.. Critical, .. after]));

    private static Creature Target() => ClassKit.Dummy("Target", hitPoints: 300);

    [Fact]
    public void BleedingCriticalOpensTwoD6AWoundAndStacks()
    {
        var brute = Brute("\"bleeding-critical\"");
        var target = Target();

        Assert.True(Crit(brute, target).IsCritical);
        Assert.Equal("2d6", ((DamageOverTimeEffect)target.Effects.Find(CriticalFeats.BleedLabel)!).Amount.ToString());

        Crit(brute, target);
        Assert.Equal("4d6", ((DamageOverTimeEffect)target.Effects.Find(CriticalFeats.BleedLabel)!).Amount.ToString());

        // Magical healing closes it.
        Assert.True(Bleeds.Stop(target));
        Assert.False(target.Effects.Has(CriticalFeats.BleedLabel));
    }

    [Fact]
    public void BleedingCriticalNeedsAnEdgeOrAPoint()
    {
        var brute = Brute("\"bleeding-critical\"", "light-mace");
        var target = Target();

        Assert.True(Strike.Resolve(brute, brute.MeleeAttack!, target, new SequenceRandom(20, 20, 3, 3)).IsCritical);
        Assert.False(target.Effects.Has(CriticalFeats.BleedLabel));
    }

    [Fact]
    public void SickeningCriticalSickensForAMinuteAndMoreHitsAddToIt()
    {
        var brute = Brute("\"sickening-critical\"");
        var target = Target();

        Crit(brute, target);
        Assert.True(target.Has(Condition.Sickened));
        Assert.Equal(Duration.Rounds(10).Ticks, target.Effects.Find("Sickening Critical")!.TicksRemaining);

        Crit(brute, target);
        Assert.Equal(Duration.Rounds(20).Ticks, target.Effects.Find("Sickening Critical")!.TicksRemaining);
    }

    [Theory]
    [InlineData(2, 3, 4)]
    [InlineData(20, 3, 1)]
    public void StaggeringCriticalStaggersForD4Plus1OrOneRoundOnASave(int save, int die, int rounds)
    {
        var brute = Brute("\"staggering-critical\"");
        var target = Target();

        var strike = Crit(brute, target, save, die);

        Assert.True(target.Has(Condition.Staggered));
        Assert.Equal(Duration.Rounds(rounds).Ticks, target.Effects.Find("Staggering Critical")!.TicksRemaining);
        Assert.Contains($"staggered for {rounds} round(s)", string.Join("; ", strike.Notes));
    }

    [Theory]
    [InlineData(2, Condition.Stunned)]
    [InlineData(20, Condition.Staggered)]
    public void StunningCriticalStunsOrOnASaveStaggers(int save, Condition result)
    {
        var brute = Brute("\"staggering-critical\", \"stunning-critical\"");
        var target = Target();

        Crit(brute, target, save, 2);

        Assert.True(target.Has(result));
        Assert.Equal(Duration.Rounds(2).Ticks, target.Effects.Find("Stunning Critical")!.TicksRemaining);
    }

    [Fact]
    // A natural 20 is what saves a dummy against DC 27: the saves above that pass roll one.
    public void TheSaveIsTenPlusTheAttackersBaseAttack()
    {
        Assert.Equal(27, CriticalFeats.DifficultyClass(Brute("\"stunning-critical\"")));
    }

    [Fact]
    public void TiringCriticalFatiguesAndExhaustingCriticalExhausts()
    {
        var tiring = Brute("\"tiring-critical\"");
        var exhausting = Brute("\"tiring-critical\", \"exhausting-critical\"");
        var first = Target();
        var second = Target();

        Crit(tiring, first);
        Crit(exhausting, second);

        Assert.True(first.Has(Condition.Fatigued));
        Assert.True(second.Has(Condition.Exhausted));
    }

    [Fact]
    public void TiringCriticalDoesNothingMoreToTheAlreadyTired()
    {
        var brute = Brute("\"tiring-critical\"");
        var target = Target();
        target.Effects.Apply(ConditionInfo.Effect(Condition.Fatigued, Duration.Permanent));

        var strike = Crit(brute, target);

        Assert.Empty(strike.Notes);
    }

    [Theory]
    [InlineData(2, Condition.Blinded)]
    [InlineData(20, Condition.Dazzled)]
    public void BlindingCriticalBlindsOrOnASaveDazzles(int save, Condition result)
    {
        var brute = Brute("\"blinding-critical\"");
        var target = Target();

        Crit(brute, target, save, 3);

        Assert.True(target.Has(result));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(20, false)]
    public void DeafeningCriticalDeafensForGoodOrForARound(int save, bool forGood)
    {
        var brute = Brute("\"deafening-critical\"");
        var target = Target();

        Crit(brute, target, save);

        Assert.True(target.Has(Condition.Deafened));
        Assert.Equal(forGood, target.Effects.Find("Deafening Critical")!.Duration.IsPermanent);
    }

    [Fact]
    public void OneCriticalCarriesOneFeatAndTwoWithCriticalMastery()
    {
        var single = Brute("\"sickening-critical\", \"tiring-critical\"");
        var master = Brute("\"sickening-critical\", \"tiring-critical\", \"critical-mastery\"");
        var first = Target();
        var second = Target();

        Crit(single, first);
        Crit(master, second);

        // Sickening is the stronger of the two, so it is the one a single critical carries.
        Assert.True(first.Has(Condition.Sickened));
        Assert.False(first.Has(Condition.Fatigued));
        Assert.True(second.Has(Condition.Sickened));
        Assert.True(second.Has(Condition.Fatigued));
    }

    [Fact]
    public void AnOrdinaryHitCarriesNone()
    {
        var brute = Brute("\"sickening-critical\"");
        var target = Target();

        Strike.Resolve(brute, brute.MeleeAttack!, target, new SequenceRandom(15, 4));

        Assert.False(target.Has(Condition.Sickened));
    }
}
