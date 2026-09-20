using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Tests.Effects;

public class RegenerationTests
{
    /// <summary>Armour class 12, 78 hit points, regeneration 5 that fire and acid switch off.</summary>
    private static Creature Troll()
    {
        var troll = new Creature("Troll", new AbilityScores(21, 14, 23, 6, 9, 6), 42, 6);
        troll.Effects.Apply(new RegenerationEffect(
            "Regeneration", Duration.Permanent, 5, DamageType.Fire, DamageType.Acid));
        return troll;
    }

    private static Creature Fighter() =>
        new("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

    /// <summary>A flat twelve points, so only the attack roll needs scripting.</summary>
    private static WeaponAttack Greatsword() =>
        WeaponAttack.Create("greatsword", 10, "12", DamageType.Slashing);

    private static WeaponAttack Torch() =>
        WeaponAttack.Create("torch", 10, "8", DamageType.Fire);

    private static IRandomSource Unused() => new SequenceRandom(1);

    [Fact]
    public void SteelOnlyEverDealsNonlethalDamage()
    {
        var troll = Troll();

        var result = Strike.Resolve(Fighter(), Greatsword(), troll, new SequenceRandom(2));

        Assert.True(result.IsHit);
        Assert.Equal(12, result.NonlethalDealt);
        Assert.Equal(0, result.DamageDealt);
        Assert.Equal(78, troll.HitPoints.Current);
        Assert.Equal(12, troll.HitPoints.Nonlethal);
        Assert.True(troll.IsAlive);
    }

    [Fact]
    public void EnoughSteelKnocksItOutButDoesNotKillIt()
    {
        var troll = Troll();

        for (var i = 0; i < 8; i++)
        {
            Strike.Resolve(Fighter(), Greatsword(), troll, new SequenceRandom(2));
        }

        Assert.Equal(96, troll.HitPoints.Nonlethal);
        Assert.Equal(78, troll.HitPoints.Current);
        Assert.False(troll.IsConscious);
        Assert.True(troll.IsAlive);
    }

    [Fact]
    public void FireIsLethalAndStopsTheHealing()
    {
        var troll = Troll();

        var result = Strike.Resolve(Fighter(), Torch(), troll, new SequenceRandom(2));

        Assert.Equal(8, result.DamageDealt);
        Assert.Equal(0, result.NonlethalDealt);
        Assert.Equal(70, troll.HitPoints.Current);
        Assert.True(troll.Effects.Regeneration!.IsSuspended);
    }

    [Fact]
    public void TheSuspendedRoundHealsNothingAndTheNextOneResumes()
    {
        var troll = Troll();
        Strike.Resolve(Fighter(), Torch(), troll, new SequenceRandom(2));
        Assert.Equal(70, troll.HitPoints.Current);

        var suspended = troll.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Contains("suspended", Assert.Single(suspended).Description);
        Assert.Equal(70, troll.HitPoints.Current);
        Assert.False(troll.Effects.Regeneration!.IsSuspended);

        troll.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Equal(75, troll.HitPoints.Current);
    }

    [Fact]
    public void WhileSuspendedEvenSteelBites()
    {
        var troll = Troll();
        Strike.Resolve(Fighter(), Torch(), troll, new SequenceRandom(2));

        var result = Strike.Resolve(Fighter(), Greatsword(), troll, new SequenceRandom(2));

        Assert.Equal(12, result.DamageDealt);
        Assert.Equal(0, result.NonlethalDealt);
        Assert.Equal(58, troll.HitPoints.Current);
    }

    [Fact]
    public void ItClosesNonlethalWoundsBeforeRealOnes()
    {
        var troll = Troll();
        troll.HitPoints.Take(8);
        troll.HitPoints.TakeNonlethal(12);

        troll.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Equal(7, troll.HitPoints.Nonlethal);
        Assert.Equal(70, troll.HitPoints.Current);

        troll.Effects.Advance(Duration.Rounds(2), Unused());

        Assert.Equal(0, troll.HitPoints.Nonlethal);
        Assert.Equal(73, troll.HitPoints.Current);
    }

    [Fact]
    public void AFlamingSwordSplitsTheBlowInTwo()
    {
        var troll = Troll();
        var flaming = new WeaponAttack(
            "flaming greatsword",
            WeaponAttack.Create("x", 10, "1", DamageType.Slashing).Attack,
            DamagePacket.Of(
                DamageComponent.Weapon("10", DamageType.Slashing),
                DamageComponent.Extra("4", DamageType.Fire)));

        var result = Strike.Resolve(Fighter(), flaming, troll, new SequenceRandom(2));

        // Only the fire half is real.
        Assert.Equal(4, result.DamageDealt);
        Assert.Equal(10, result.NonlethalDealt);
        Assert.Equal(74, troll.HitPoints.Current);
        Assert.Equal(10, troll.HitPoints.Nonlethal);
        Assert.True(troll.Effects.Regeneration!.IsSuspended);
    }

    [Fact]
    public void RegenerationThatNothingSuspendsAbsorbsEverything()
    {
        var thing = new Creature("Thing", AbilityScores.All(12), 30, 3);
        thing.Effects.Apply(new RegenerationEffect("Regeneration", Duration.Permanent, 5));

        var result = Strike.Resolve(Fighter(), Torch(), thing, new SequenceRandom(2));

        Assert.Equal(0, result.DamageDealt);
        Assert.Equal(8, result.NonlethalDealt);
    }

    [Fact]
    public void ACreatureWithoutRegenerationIsUnaffected()
    {
        var goblin = new Creature("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 30, 4);

        var result = Strike.Resolve(Fighter(), Greatsword(), goblin, new SequenceRandom(2));

        Assert.Equal(12, result.DamageDealt);
        Assert.Equal(0, result.NonlethalDealt);
        Assert.Null(goblin.Effects.Regeneration);
    }

    [Fact]
    public void TheLogSaysWhyTheTrollGotBackUp()
    {
        var troll = Troll();

        var result = Strike.Resolve(Fighter(), Greatsword(), troll, new SequenceRandom(2));

        Assert.Contains("12 of it nonlethal", result.ToString());
    }

    [Fact]
    public void RejectsAnImpossibleRegeneration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RegenerationEffect("Regeneration", Duration.Permanent, 0));
    }
}
