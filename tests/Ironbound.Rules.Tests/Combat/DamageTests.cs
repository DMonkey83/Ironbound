using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Combat;

public class DamageTypeTests
{
    [Theory]
    [InlineData(DamageType.Bludgeoning, true, false)]
    [InlineData(DamageType.Piercing, true, false)]
    [InlineData(DamageType.Slashing, true, false)]
    [InlineData(DamageType.Fire, false, true)]
    [InlineData(DamageType.Cold, false, true)]
    [InlineData(DamageType.Acid, false, true)]
    [InlineData(DamageType.Electricity, false, true)]
    [InlineData(DamageType.Sonic, false, true)]
    [InlineData(DamageType.Force, false, false)]
    [InlineData(DamageType.Negative, false, false)]
    [InlineData(DamageType.Untyped, false, false)]
    public void ClassifiesWhatMitigationWillCareAbout(DamageType type, bool physical, bool energy)
    {
        Assert.Equal(physical, DamageTypes.IsPhysical(type));
        Assert.Equal(energy, DamageTypes.IsEnergy(type));
    }

    [Fact]
    public void NamesReadLikeATooltip()
    {
        Assert.Equal("slashing", DamageTypes.Name(DamageType.Slashing));
        Assert.Equal("fire", DamageTypes.Name(DamageType.Fire));
    }
}

public class DamageComponentTests
{
    [Fact]
    public void WeaponDamageMultipliesOnACritical()
    {
        Assert.True(DamageComponent.Weapon("1d8+4", DamageType.Slashing).MultipliedOnCritical);
    }

    [Fact]
    public void ExtraDamageDoesNot()
    {
        Assert.False(DamageComponent.Extra("1d6", DamageType.Fire).MultipliedOnCritical);
    }

    [Fact]
    public void ParsesItsAmountFromText()
    {
        var component = DamageComponent.Weapon("1d8+4", DamageType.Slashing);

        Assert.Equal(DiceExpression.Parse("1d8+4"), component.Amount);
    }

    [Fact]
    public void FormatsForATooltip()
    {
        Assert.Equal("1d8+4 slashing", DamageComponent.Weapon("1d8+4", DamageType.Slashing).ToString());
    }

    [Fact]
    public void RejectsAMissingAmount()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DamageComponent(null!, DamageType.Slashing, true));
    }
}

public class DamagePacketTests
{
    /// <summary>A +1 flaming longsword in the hands of an 18-Strength fighter.</summary>
    private static DamagePacket FlamingLongsword() => DamagePacket.Of(
        DamageComponent.Weapon("1d8+5", DamageType.Slashing),
        DamageComponent.Extra("1d6", DamageType.Fire));

    [Fact]
    public void AnEmptyPacketDealsNothing()
    {
        var roll = new DamagePacket().Roll(new SequenceRandom(1));

        Assert.Equal(0, roll.Total);
        Assert.Equal("0 damage", roll.ToString());
        Assert.Equal("no damage", new DamagePacket().ToString());
    }

    [Fact]
    public void AnOrdinaryHitRollsEachComponentOnce()
    {
        var random = new SequenceRandom(6, 3);

        var roll = FlamingLongsword().Roll(random);

        Assert.Equal(11, roll.AmountOf(DamageType.Slashing));
        Assert.Equal(3, roll.AmountOf(DamageType.Fire));
        Assert.Equal(14, roll.Total);
        Assert.Equal(2, random.Consumed);
    }

    [Fact]
    public void ACriticalRerollsTheWeaponButNotTheFlamingDamage()
    {
        // Two swings of the blade (6 then 5) and a single burst of fire (3).
        var random = new SequenceRandom(6, 5, 3);

        var roll = FlamingLongsword().Roll(random, criticalMultiplier: 2);

        Assert.Equal(21, roll.AmountOf(DamageType.Slashing));
        Assert.Equal(3, roll.AmountOf(DamageType.Fire));
        Assert.Equal(24, roll.Total);
        Assert.Equal(3, random.Consumed);
    }

    [Fact]
    public void ATripleCriticalRollsTheWeaponThreeTimes()
    {
        var random = new SequenceRandom(1, 1, 1, 6);

        var roll = DamagePacket.Of(
            DamageComponent.Weapon("1d8", DamageType.Slashing),
            DamageComponent.Extra("1d6", DamageType.Fire)).Roll(random, criticalMultiplier: 3);

        Assert.Equal(3, roll.AmountOf(DamageType.Slashing));
        Assert.Equal(6, roll.AmountOf(DamageType.Fire));
        Assert.Equal(3, roll.Entries.Count(e => e.Type == DamageType.Slashing));
    }

    [Fact]
    public void SneakAttackIsNotMultipliedEither()
    {
        var random = new SequenceRandom(4, 4, 1, 1, 1);

        var roll = DamagePacket.Of(
            DamageComponent.Weapon("1d6", DamageType.Piercing),
            DamageComponent.Extra("3d6", DamageType.Piercing)).Roll(random, criticalMultiplier: 2);

        // Two weapon dice plus three sneak dice, all piercing: 4 + 4 + 1 + 1 + 1.
        Assert.Equal(11, roll.AmountOf(DamageType.Piercing));
        Assert.Equal(5, random.Consumed);
    }

    [Fact]
    public void EachIterationIsNumberedSoTheLogCanShowBothSwings()
    {
        var roll = FlamingLongsword().Roll(new SequenceRandom(6, 5, 3), criticalMultiplier: 2);

        var slashing = roll.Entries.Where(e => e.Type == DamageType.Slashing).ToArray();
        Assert.Equal([0, 1], slashing.Select(e => e.Iteration));
        Assert.Equal([11, 10], slashing.Select(e => e.Amount));
    }

    [Fact]
    public void DamageIsKeptSplitByType()
    {
        var roll = FlamingLongsword().Roll(new SequenceRandom(6, 3));

        Assert.Equal([DamageType.Slashing, DamageType.Fire], roll.Types);
        Assert.Equal(2, roll.ByType.Count);
        Assert.Equal(0, roll.AmountOf(DamageType.Cold));
    }

    [Fact]
    public void AComponentNeverDealsLessThanOne()
    {
        // A crushing Strength penalty: 1d8-10 cannot reduce a hit to nothing.
        var roll = DamagePacket.Weapon("1d8-10", DamageType.Slashing).Roll(new SequenceRandom(1));

        Assert.Equal(1, roll.Total);
    }

    [Fact]
    public void TheFloorAppliesToEveryIterationOfACritical()
    {
        var roll = DamagePacket.Weapon("1d8-10", DamageType.Slashing)
            .Roll(new SequenceRandom(1, 2), criticalMultiplier: 2);

        Assert.Equal(2, roll.Total);
    }

    [Fact]
    public void ReportsItsRange()
    {
        var packet = FlamingLongsword();

        Assert.Equal(7, packet.Minimum);   // 1d8+5 at worst is 6, plus 1 fire
        Assert.Equal(19, packet.Maximum);  // 13 plus 6
    }

    [Fact]
    public void TheRangeRespectsTheMinimum()
    {
        var packet = DamagePacket.Weapon("1d8-10", DamageType.Slashing);

        Assert.Equal(1, packet.Minimum);
        Assert.Equal(1, packet.Maximum);
    }

    [Fact]
    public void ReportsExpectedDamageAtAnyMultiplier()
    {
        var packet = FlamingLongsword();

        Assert.Equal(13.0, packet.Average, 9);          // 9.5 slashing + 3.5 fire
        Assert.Equal(22.5, packet.AverageAt(2), 9);     // 19 slashing + 3.5 fire, not 26
        Assert.Equal(32.0, packet.AverageAt(3), 9);
    }

    [Fact]
    public void ComponentsCanBeAddedAndRemoved()
    {
        var packet = DamagePacket.Weapon("1d8+5", DamageType.Slashing);
        packet.Add(DamageComponent.Extra("1d6", DamageType.Fire));
        Assert.Equal(2, packet.Count);

        Assert.Equal(1, packet.RemoveAll(DamageType.Fire));

        Assert.Equal(1, packet.Count);
        Assert.Equal("1d8+5 slashing", packet.ToString());
    }

    [Fact]
    public void FormatsForATooltip()
    {
        Assert.Equal("1d8+5 slashing + 1d6 fire", FlamingLongsword().ToString());
    }

    [Fact]
    public void FormatsTheRollForTheCombatLog()
    {
        Assert.Equal(
            "11 slashing (1d8+5: [6] +5 = 11) + 3 fire (1d6: [3] = 3) = 14 damage",
            FlamingLongsword().Roll(new SequenceRandom(6, 3)).ToString());

        Assert.Equal(
            "21 slashing (1d8+5: [6] +5 = 11, 1d8+5: [5] +5 = 10) + 3 fire (1d6: [3] = 3) = 24 damage",
            FlamingLongsword().Roll(new SequenceRandom(6, 5, 3), criticalMultiplier: 2).ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsAnImpossibleMultiplier(int multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FlamingLongsword().Roll(new SequenceRandom(1), multiplier));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlamingLongsword().AverageAt(multiplier));
    }

    [Fact]
    public void RejectsAMissingRandomSource()
    {
        Assert.Throws<ArgumentNullException>(() => FlamingLongsword().Roll(null!));
    }

    [Fact]
    public void TakesItsMultiplierStraightFromTheAttackResult()
    {
        var defense = new ArmorClass(new AbilityScore(Ability.Dexterity, 10));
        var attack = new Attack();
        attack.Modifiers.Add(7, BonusType.Untyped, "Attack Bonus");

        // Natural 20, confirmed on a 14: a x2 critical.
        var result = attack.Resolve(defense, new SequenceRandom(20, 14));
        Assert.Equal(AttackOutcome.CriticalHit, result.Outcome);

        var damage = FlamingLongsword().Roll(new SequenceRandom(6, 5, 3), result.CriticalMultiplier);

        Assert.Equal(24, damage.Total);
        Assert.Equal(2, damage.CriticalMultiplier);
    }
}
