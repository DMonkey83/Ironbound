using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Combat;

public class AttackTests
{
    /// <summary>A target whose AC is the same number however it is attacked.</summary>
    private static ArmorClass Target(int armorClass)
    {
        var defense = new ArmorClass(new AbilityScore(Ability.Dexterity, 10));
        if (armorClass != ArmorClass.BaseValue)
        {
            defense.Modifiers.Add(armorClass - ArmorClass.BaseValue, BonusType.Untyped, "Test Padding");
        }

        return defense;
    }

    /// <summary>Dexterity 18 in a breastplate: AC 19, touch 13, flat-footed 16.</summary>
    private static ArmorClass Fighter()
    {
        var defense = new ArmorClass(new AbilityScore(Ability.Dexterity, 18));
        defense.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        defense.CapDexterity("Breastplate", 3);
        return defense;
    }

    private static Attack WithBonus(int bonus, CriticalProfile? critical = null)
    {
        var attack = new Attack();
        if (bonus != 0)
        {
            attack.Modifiers.Add(bonus, BonusType.Untyped, "Attack Bonus");
        }

        if (critical is { } profile)
        {
            attack.Critical = profile;
        }

        return attack;
    }

    // ---- basics ----

    [Fact]
    public void HitsWhenTheTotalBeatsArmorClass()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(17));

        Assert.Equal(AttackOutcome.Hit, result.Outcome);
        Assert.Equal(24, result.Total);
        Assert.Equal(19, result.TargetArmorClass);
    }

    [Fact]
    public void MissesWhenTheTotalFallsShort()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(8));

        Assert.Equal(AttackOutcome.Miss, result.Outcome);
        Assert.False(result.IsHit);
        Assert.Equal(15, result.Total);
    }

    [Fact]
    public void MatchingArmorClassExactlyHits()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(12));

        Assert.Equal(19, result.Total);
        Assert.True(result.IsHit);
    }

    [Fact]
    public void TheBonusComesFromTheModifierStackAndObeysStacking()
    {
        var attack = new Attack();
        attack.Modifiers.Add(2, BonusType.Enhancement, "Magic Weapon");
        attack.Modifiers.Add(3, BonusType.Enhancement, "Greater Magic Weapon");
        attack.Modifiers.Add(1, BonusType.Morale, "Bless");

        var result = attack.Resolve(Target(10), new SequenceRandom(5));

        // Enhancement does not stack: +3, not +5. Morale is a different type, so it adds.
        Assert.Equal(4, result.Bonus.Total);
        Assert.Equal(9, result.Total);
    }

    [Fact]
    public void OneAttackConsumesOneRollWhenNothingThreatens()
    {
        var random = new SequenceRandom(11);

        WithBonus(0).Resolve(Target(19), random);

        Assert.Equal(1, random.Consumed);
    }

    // ---- automatic hit and miss ----

    [Fact]
    public void ANaturalOneMissesNoMatterTheBonus()
    {
        var result = WithBonus(50).Resolve(Target(2), new SequenceRandom(1));

        Assert.Equal(AttackOutcome.Miss, result.Outcome);
        Assert.True(result.IsAutomatic);
        Assert.False(result.Threatened);
    }

    [Fact]
    public void ANaturalTwentyHitsNoMatterTheArmorClass()
    {
        var result = WithBonus(0).Resolve(Target(40), new SequenceRandom(20, 5));

        Assert.True(result.IsHit);
        Assert.True(result.Threatened);
    }

    // ---- threat and confirmation ----

    [Fact]
    public void AConfirmedThreatIsACriticalHit()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(20, 14));

        Assert.Equal(AttackOutcome.CriticalHit, result.Outcome);
        Assert.True(result.IsCritical);
        Assert.Equal(2, result.CriticalMultiplier);
        Assert.Equal(14, result.ConfirmationNatural);
        Assert.Equal(21, result.ConfirmationTotal);
    }

    [Fact]
    public void AnUnconfirmedThreatIsAnOrdinaryHit()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(20, 2));

        Assert.Equal(AttackOutcome.Hit, result.Outcome);
        Assert.True(result.Threatened);
        Assert.True(result.ThreatenedButNotConfirmed);
        Assert.Equal(1, result.CriticalMultiplier);
    }

    [Fact]
    public void AKeenWeaponThreatensOnNineteen()
    {
        var result = WithBonus(7, new CriticalProfile(19, 2))
            .Resolve(Target(19), new SequenceRandom(19, 14));

        Assert.Equal(AttackOutcome.CriticalHit, result.Outcome);
    }

    [Fact]
    public void AnOrdinaryWeaponDoesNotThreatenOnNineteen()
    {
        var random = new SequenceRandom(19);

        var result = WithBonus(7).Resolve(Target(19), random);

        Assert.Equal(AttackOutcome.Hit, result.Outcome);
        Assert.False(result.Threatened);
        Assert.Null(result.ConfirmationNatural);
        Assert.Equal(1, random.Consumed);
    }

    [Fact]
    public void ARollInTheThreatRangeThatMissesRollsNoConfirmation()
    {
        var random = new SequenceRandom(19);

        var result = WithBonus(7, new CriticalProfile(19, 2)).Resolve(Target(30), random);

        Assert.Equal(AttackOutcome.Miss, result.Outcome);
        Assert.False(result.Threatened);

        // The stray draw would desynchronise every later roll in a replay.
        Assert.Equal(1, random.Consumed);
    }

    [Fact]
    public void ANaturalOneOnTheConfirmationFails()
    {
        var result = WithBonus(50).Resolve(Target(19), new SequenceRandom(20, 1));

        Assert.Equal(AttackOutcome.Hit, result.Outcome);
        Assert.True(result.ThreatenedButNotConfirmed);
    }

    [Fact]
    public void ANaturalTwentyOnTheConfirmationSucceedsAgainstAnyArmorClass()
    {
        var result = WithBonus(0).Resolve(Target(40), new SequenceRandom(20, 20));

        Assert.Equal(AttackOutcome.CriticalHit, result.Outcome);
    }

    [Fact]
    public void TheMultiplierComesFromTheWeapon()
    {
        var result = WithBonus(7, new CriticalProfile(20, 4))
            .Resolve(Target(19), new SequenceRandom(20, 14));

        Assert.Equal(4, result.CriticalMultiplier);
    }

    [Fact]
    public void ANormalHitReportsAMultiplierOfOneSoDamageCanMultiplyBlindly()
    {
        var result = WithBonus(7).Resolve(Target(19), new SequenceRandom(17));

        Assert.Equal(1, result.CriticalMultiplier);
    }

    // ---- how the attack meets the defence ----

    [Fact]
    public void ATouchAttackResolvesAgainstTouchArmorClass()
    {
        var attack = WithBonus(0);
        attack.TargetsTouchArmorClass = true;

        var result = attack.Resolve(Fighter(), new SequenceRandom(13));

        Assert.Equal(13, result.TargetArmorClass);
        Assert.True(result.IsHit);
        Assert.Equal(DefenseOptions.TouchAttack, result.Options);
    }

    [Fact]
    public void AFlatFootedDefenderUsesFlatFootedArmorClass()
    {
        var result = WithBonus(0)
            .Resolve(Fighter(), new SequenceRandom(16), DefenseOptions.DexterityDenied);

        Assert.Equal(16, result.TargetArmorClass);
        Assert.True(result.IsHit);
    }

    [Fact]
    public void ATouchAttackOnAFlatFootedDefenderCombinesBoth()
    {
        var attack = WithBonus(0);
        attack.TargetsTouchArmorClass = true;

        var result = attack.Resolve(Fighter(), new SequenceRandom(10), DefenseOptions.DexterityDenied);

        // No armour, no Dexterity: just the base 10.
        Assert.Equal(10, result.TargetArmorClass);
        Assert.True(result.IsHit);
        Assert.Equal(DefenseOptions.TouchAttack | DefenseOptions.DexterityDenied, result.Options);
    }

    [Fact]
    public void BypassingArmourIsWhatTurnsTheMissIntoAHit()
    {
        var attack = WithBonus(0);

        Assert.False(attack.Resolve(Fighter(), new SequenceRandom(14)).IsHit);

        attack.TargetsTouchArmorClass = true;
        Assert.True(attack.Resolve(Fighter(), new SequenceRandom(14)).IsHit);
    }

    [Fact]
    public void BuffsOnTheDefendersDexterityReachTheAttackRoll()
    {
        var dexterity = new AbilityScore(Ability.Dexterity, 10);
        var defense = new ArmorClass(dexterity);

        Assert.True(WithBonus(0).Resolve(defense, new SequenceRandom(10)).IsHit);

        dexterity.Modifiers.Add(4, BonusType.Enhancement, "Cat's Grace");

        Assert.False(WithBonus(0).Resolve(defense, new SequenceRandom(10)).IsHit);
    }

    // ---- the log line ----

    [Fact]
    public void FormatsAnOrdinaryHitAndMiss()
    {
        Assert.Equal(
            "d20 [17] +7 = 24 vs AC 19 — hit",
            WithBonus(7).Resolve(Target(19), new SequenceRandom(17)).ToString());

        Assert.Equal(
            "d20 [8] +7 = 15 vs AC 19 — miss",
            WithBonus(7).Resolve(Target(19), new SequenceRandom(8)).ToString());
    }

    [Fact]
    public void FormatsAnAutomaticMiss()
    {
        Assert.Equal(
            "d20 [1] — automatic miss",
            WithBonus(7).Resolve(Target(19), new SequenceRandom(1)).ToString());
    }

    [Fact]
    public void FormatsAThreatThatFailsToConfirm()
    {
        Assert.Equal(
            "d20 [19] +7 = 26 vs AC 19 — hit, threat; confirm d20 [2] +7 = 9 vs AC 19 — not confirmed",
            WithBonus(7, new CriticalProfile(19, 2))
                .Resolve(Target(19), new SequenceRandom(19, 2))
                .ToString());
    }

    [Fact]
    public void FormatsAConfirmedCritical()
    {
        Assert.Equal(
            "d20 [20] — automatic hit, threat; confirm d20 [14] +7 = 21 vs AC 19 — critical hit (x2)",
            WithBonus(7).Resolve(Target(19), new SequenceRandom(20, 14)).ToString());
    }

    [Fact]
    public void FormatsNamesTheArmorClassItUsed()
    {
        var attack = WithBonus(0);
        attack.TargetsTouchArmorClass = true;

        Assert.Equal(
            "d20 [13] +0 = 13 vs touch AC 13 — hit",
            attack.Resolve(Fighter(), new SequenceRandom(13)).ToString());

        Assert.Equal(
            "touch flat-footed AC",
            AttackResult.DescribeArmorClass(DefenseOptions.TouchAttack | DefenseOptions.DexterityDenied));
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WithBonus(0).Resolve(null!, new SequenceRandom(10)));
        Assert.Throws<ArgumentNullException>(() => WithBonus(0).Resolve(Target(10), null!));
    }
}
