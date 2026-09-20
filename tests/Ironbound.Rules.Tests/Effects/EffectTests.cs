using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Effects;

public class EffectTests
{
    /// <summary>Strength 18, Constitution 14, six hit dice: 52 hit points, AC 12.</summary>
    private static Creature Fighter() =>
        new("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

    private static IRandomSource Unused() => new SequenceRandom(1);

    private static ModifierEffect BullsStrength(Duration duration, int amount = 4) =>
        new ModifierEffect("Bull's Strength", duration)
            .GrantsToAbility(Ability.Strength, amount, BonusType.Enhancement);

    // ---- applying and expiring ----

    [Fact]
    public void ApplyingGrantsTheModifierImmediately()
    {
        var fighter = Fighter();

        var applied = fighter.Effects.Apply(BullsStrength(Duration.Minutes(1)));

        Assert.Equal(EffectEventKind.Applied, applied.Kind);
        Assert.Equal(22, fighter.Abilities.Strength.Score);
        Assert.True(fighter.Effects.Has("Bull's Strength"));
    }

    [Fact]
    public void ItSurvivesUntilTheVeryLastTick()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Minutes(1)));

        Assert.Empty(fighter.Effects.Advance(Duration.FromTicks(599), Unused()));
        Assert.Equal(22, fighter.Abilities.Strength.Score);

        var events = fighter.Effects.Advance(Duration.FromTicks(1), Unused());

        Assert.Equal(EffectEventKind.Expired, Assert.Single(events).Kind);
        Assert.Equal(18, fighter.Abilities.Strength.Score);
        Assert.False(fighter.Effects.Has("Bull's Strength"));
    }

    [Fact]
    public void ExpiryActuallyTakesTheModifierOutOfTheStack()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Rounds(1)));
        Assert.Equal(1, fighter.Abilities.Strength.Modifiers.Count);

        fighter.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Equal(0, fighter.Abilities.Strength.Modifiers.Count);
    }

    [Fact]
    public void APermanentEffectNeverRunsOut()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Permanent));

        Assert.Empty(fighter.Effects.Advance(Duration.Days(1), Unused()));
        Assert.Equal(22, fighter.Abilities.Strength.Score);
    }

    [Fact]
    public void DispellingCleansUpExactlyLikeExpiry()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Days(1)));

        var removed = fighter.Effects.Remove("Bull's Strength");

        Assert.Equal(EffectEventKind.Removed, removed!.Value.Kind);
        Assert.Equal(18, fighter.Abilities.Strength.Score);
        Assert.Equal(0, fighter.Abilities.Strength.Modifiers.Count);
    }

    [Fact]
    public void RemovingSomethingAbsentIsNotAnError()
    {
        Assert.Null(Fighter().Effects.Remove("Haste"));
    }

    [Fact]
    public void ReapplyingRefreshesRatherThanDoubling()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Rounds(1)));
        fighter.Effects.Advance(Duration.FromTicks(30), Unused());

        fighter.Effects.Apply(BullsStrength(Duration.Rounds(1)));

        Assert.Equal(1, fighter.Effects.Count);
        Assert.Equal(1, fighter.Abilities.Strength.Modifiers.Count);

        // The clock restarted, so the original half-round is gone.
        Assert.Empty(fighter.Effects.Advance(Duration.FromTicks(59), Unused()));
        Assert.Equal(22, fighter.Abilities.Strength.Score);
    }

    [Fact]
    public void ClearingEndsEverything()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Days(1)));
        fighter.Effects.Apply(new ModifierEffect("Shield of Faith", Duration.Days(1))
            .GrantsToArmorClass(4, BonusType.Deflection));

        fighter.Effects.Clear();

        Assert.Equal(0, fighter.Effects.Count);
        Assert.Equal(18, fighter.Abilities.Strength.Score);
        Assert.Equal(12, fighter.ArmorClass.Total);
    }

    // ---- the case a naive implementation gets wrong ----

    [Fact]
    public void WhenTheBiggerBuffEndsTheSmallerOneTakesOver()
    {
        var fighter = Fighter();

        fighter.Effects.Apply(new ModifierEffect("Belt of Giant Strength", Duration.Permanent)
            .GrantsToAbility(Ability.Strength, 2, BonusType.Enhancement));
        fighter.Effects.Apply(BullsStrength(Duration.Rounds(1)));

        // Same bonus type, so only the larger counts while both are up.
        Assert.Equal(22, fighter.Abilities.Strength.Score);

        fighter.Effects.Advance(Duration.Rounds(1), Unused());

        // The belt is still on and is now the best enhancement bonus available.
        Assert.Equal(20, fighter.Abilities.Strength.Score);
        Assert.Equal(1, fighter.Effects.Count);
    }

    [Fact]
    public void AnEffectMayGrantSeveralModifiersAndTakesThemAllBack()
    {
        var fighter = Fighter();

        fighter.Effects.Apply(new ModifierEffect("Haste", Duration.Rounds(1))
            .GrantsToArmorClass(1, BonusType.Dodge)
            .GrantsToAbility(Ability.Dexterity, 2, BonusType.Untyped));

        Assert.Equal(14, fighter.ArmorClass.Total);   // 12 base, +1 dodge, +1 from Dexterity 16

        fighter.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Equal(12, fighter.ArmorClass.Total);
        Assert.Equal(0, fighter.ArmorClass.Modifiers.Count);
        Assert.Equal(0, fighter.Abilities.Dexterity.Modifiers.Count);
    }

    // ---- periodic ----

    [Fact]
    public void BleedFiresOncePerRoundAndNotBetween()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(new DamageOverTimeEffect(
            "Bleed", Duration.Rounds(3), "1d6", DamageType.Slashing));

        Assert.Empty(fighter.Effects.Advance(Duration.FromTicks(59), new SequenceRandom(3)));
        Assert.Equal(52, fighter.HitPoints.Current);

        var events = fighter.Effects.Advance(Duration.FromTicks(1), new SequenceRandom(3));

        Assert.Equal(EffectEventKind.Ticked, Assert.Single(events).Kind);
        Assert.Equal(49, fighter.HitPoints.Current);
    }

    [Fact]
    public void AMinuteInOneCallFiresTenTimesNotOnce()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(new DamageOverTimeEffect(
            "Bleed", Duration.Minutes(1), "1d6", DamageType.Slashing));

        var events = fighter.Effects.Advance(Duration.Minutes(1), SequenceRandom.Always(1));

        Assert.Equal(10, events.Count(e => e.Kind == EffectEventKind.Ticked));
        Assert.Equal(42, fighter.HitPoints.Current);
    }

    [Fact]
    public void AnEffectEndingOnAFiringTickStillGetsThatTick()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(new DamageOverTimeEffect(
            "Bleed", Duration.Rounds(1), "1d6", DamageType.Slashing));

        var events = fighter.Effects.Advance(Duration.Rounds(1), new SequenceRandom(4));

        Assert.Equal(EffectEventKind.Ticked, events[0].Kind);
        Assert.Equal(EffectEventKind.Expired, events[1].Kind);
        Assert.Equal(48, fighter.HitPoints.Current);
    }

    [Fact]
    public void PeriodicDamageRunsThroughTheTargetsDefences()
    {
        var fighter = Fighter();
        fighter.Defenses.MakeImmuneTo(DamageType.Fire);
        fighter.Effects.Apply(new DamageOverTimeEffect(
            "Burning", Duration.Rounds(1), "1d6", DamageType.Fire));

        fighter.Effects.Advance(Duration.Rounds(1), new SequenceRandom(6));

        Assert.Equal(52, fighter.HitPoints.Current);
    }

    [Fact]
    public void FastHealingStopsAtFullHealthAndSaysNothingWhenItDoesNothing()
    {
        var fighter = Fighter();
        fighter.HitPoints.Take(7);
        fighter.Effects.Apply(new FastHealingEffect("Fast Healing", Duration.Rounds(3), 5));

        var events = fighter.Effects.Advance(Duration.Rounds(3), Unused());

        // 5 then 2, then nothing left to heal.
        Assert.Equal(2, events.Count(e => e.Kind == EffectEventKind.Ticked));
        Assert.Equal(52, fighter.HitPoints.Current);
    }

    // ---- advancing ----

    [Fact]
    public void AdvancingNothingDoesNothing()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Rounds(1)));

        Assert.Empty(fighter.Effects.Advance(Duration.Zero, Unused()));
        Assert.Equal(22, fighter.Abilities.Strength.Score);
    }

    [Fact]
    public void ALongRestIsOneJumpNotEightHundredThousandSteps()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(BullsStrength(Duration.Hours(1)));

        var events = fighter.Effects.Advance(Duration.Days(1), Unused());

        Assert.Equal(EffectEventKind.Expired, Assert.Single(events).Kind);
        Assert.Equal(18, fighter.Abilities.Strength.Score);
    }

    [Fact]
    public void SeveralEffectsExpireInTheOrderTheyWereApplied()
    {
        var fighter = Fighter();
        fighter.Effects.Apply(new ModifierEffect("First", Duration.Rounds(1))
            .GrantsToArmorClass(1, BonusType.Luck));
        fighter.Effects.Apply(new ModifierEffect("Second", Duration.Rounds(1))
            .GrantsToArmorClass(1, BonusType.Sacred));

        var events = fighter.Effects.Advance(Duration.Rounds(1), Unused());

        Assert.Equal(["First", "Second"], events.Select(e => e.Effect.Name));
    }

    [Fact]
    public void TimeCannotAdvanceByAPermanentSpan()
    {
        Assert.Throws<ArgumentException>(() =>
            Fighter().Effects.Advance(Duration.Permanent, Unused()));
    }

    [Fact]
    public void AdvanceNeedsARandomSource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Fighter().Effects.Advance(Duration.Rounds(1), null!));
    }

    // ---- composition with the rest of the engine ----

    [Fact]
    public void PoisonThatDrainsConstitutionCostsMaximumHitPoints()
    {
        var fighter = Fighter();
        Assert.Equal(52, fighter.HitPoints.Maximum);

        fighter.Effects.Apply(new ModifierEffect("Spider Venom", Duration.Rounds(6))
            .GrantsToAbility(Ability.Constitution, -6, BonusType.Untyped));

        // Constitution 14 -> 8, so +2 per hit die becomes -1 across six of them.
        Assert.Equal(34, fighter.HitPoints.Maximum);
        Assert.Equal(34, fighter.HitPoints.Current);

        fighter.Effects.Advance(Duration.Rounds(6), Unused());

        Assert.Equal(52, fighter.HitPoints.Maximum);
        Assert.Equal(52, fighter.HitPoints.Current);
    }

    [Fact]
    public void ABuffExpiringChangesWhetherTheNextAttackLands()
    {
        var goblin = new Creature("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 5, 1);
        var fighter = Fighter();
        var sword = WeaponAttack.Create("longsword", 9, "1d8+6", DamageType.Slashing);

        goblin.Effects.Apply(new ModifierEffect("Shield of Faith", Duration.Rounds(1))
            .GrantsToArmorClass(4, BonusType.Deflection));

        // 3 + 9 = 12: short of armour class 16 while the blessing holds.
        Assert.False(Strike.Resolve(fighter, sword, goblin, new SequenceRandom(3)).IsHit);

        goblin.Effects.Advance(Duration.Rounds(1), Unused());

        // The same roll against the same goblin, now unprotected at armour class 12.
        Assert.True(Strike.Resolve(fighter, sword, goblin, new SequenceRandom(3, 1)).IsHit);
    }

    [Fact]
    public void EffectsReadWellInALog()
    {
        var fighter = Fighter();
        var applied = fighter.Effects.Apply(BullsStrength(Duration.Minutes(1)));

        Assert.Equal("Fighter gains Bull's Strength (1 minute)", applied.ToString());
        Assert.Equal("Bull's Strength (1 minute left)", fighter.Effects.Active[0].ToString());

        fighter.Effects.Advance(Duration.Rounds(5), Unused());
        Assert.Equal("Bull's Strength (5 rounds left)", fighter.Effects.Active[0].ToString());

        var events = fighter.Effects.Advance(Duration.Rounds(5), Unused());
        Assert.Equal("Fighter: Bull's Strength ends", events[0].ToString());

        Assert.Equal("no effects", fighter.Effects.ToString());
    }
}
