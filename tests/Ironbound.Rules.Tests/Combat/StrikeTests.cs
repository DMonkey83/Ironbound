using Ironbound.Rules;
using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Combat;

public class CreatureTests
{
    [Fact]
    public void ArmourClassAndHitPointsAreWiredToTheAbilityScores()
    {
        var creature = new Creature("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

        Assert.Equal(12, creature.ArmorClass.Total);   // 10 + 2 from Dexterity 14
        Assert.Equal(52, creature.HitPoints.Maximum);  // 40 + 2 per hit die
        Assert.True(creature.IsAlive);
    }

    [Fact]
    public void BuffingDexterityMovesArmourClass()
    {
        var creature = new Creature("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

        creature.Abilities.Dexterity.Modifiers.Add(4, BonusType.Enhancement, "Cat's Grace");

        Assert.Equal(14, creature.ArmorClass.Total);
    }

    [Fact]
    public void RollBuildsHitPointsFromTheHitDie()
    {
        var creature = Creature.Roll(
            "Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 10, 6, new SequenceRandom(1));

        Assert.Equal(52, creature.HitPoints.Maximum);
    }

    [Fact]
    public void FormatsAsAStatBlockLine()
    {
        var creature = new Creature("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 5, 1);

        Assert.Equal("Goblin — AC 12, touch 12, flat-footed 10 — 5/5 hp", creature.ToString());
    }

    [Fact]
    public void RejectsImpossibleConstruction()
    {
        var abilities = new AbilityScores(11, 15, 11, 10, 9, 6);

        Assert.Throws<ArgumentException>(() => new Creature(" ", abilities, 5, 1));
        Assert.Throws<ArgumentNullException>(() => new Creature("Goblin", null!, 5, 1));
    }
}

public class WeaponAttackTests
{
    [Fact]
    public void CreateBuildsTheAttackAndTheDamageTogether()
    {
        var weapon = WeaponAttack.Create("longsword", 9, "1d8+6", DamageType.Slashing);

        Assert.Equal(9, weapon.Attack.Modifiers.Total);
        Assert.Equal(CriticalProfile.Standard, weapon.Attack.Critical);
        Assert.Equal("1d8+6 slashing", weapon.Damage.ToString());
    }

    [Fact]
    public void FormatsLikeAStatBlockEntry()
    {
        var weapon = WeaponAttack.Create(
            "longsword", 9, "1d8+6", DamageType.Slashing, new CriticalProfile(19, 2));

        Assert.Equal("longsword +9 (1d8+6 slashing, 19-20/x2)", weapon.ToString());
    }

    [Fact]
    public void RejectsMissingParts()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WeaponAttack("longsword", null!, DamagePacket.Weapon("1d8", DamageType.Slashing)));
        Assert.Throws<ArgumentNullException>(() => new WeaponAttack("longsword", new Attack(), null!));
    }
}

public class StrikeTests
{
    /// <summary>Unarmoured, Dexterity 14: AC 12. Hit points 52.</summary>
    private static Creature Fighter() =>
        new("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);

    /// <summary>Unarmoured, Dexterity 15: AC 12, touch 12, flat-footed 10. Hit points 5.</summary>
    private static Creature Goblin() =>
        new("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 5, 1);

    private static WeaponAttack Longsword() => WeaponAttack.Create(
        "longsword", 9, "1d8+6", DamageType.Slashing, new CriticalProfile(19, 2));

    [Fact]
    public void AMissRollsNoDamageDiceAtAll()
    {
        var goblin = Goblin();
        var random = new SequenceRandom(2);   // 2 + 9 = 11 against AC 12

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, random);

        Assert.False(result.IsHit);
        Assert.Null(result.Damage);
        Assert.Null(result.Applied);
        Assert.Equal(0, result.DamageDealt);
        Assert.Equal(5, goblin.HitPoints.Current);

        // The stray draw would desynchronise every later roll in a replay.
        Assert.Equal(1, random.Consumed);
    }

    [Fact]
    public void AHitRollsDamageAndTheTargetLosesIt()
    {
        var goblin = Goblin();

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1));

        Assert.True(result.IsHit);
        Assert.Equal(7, result.Damage!.Total);
        Assert.Equal(7, result.DamageDealt);
        Assert.Equal(-2, goblin.HitPoints.Current);
    }

    [Fact]
    public void ExactlyMatchingArmourClassConnects()
    {
        var result = Strike.Resolve(Fighter(), Longsword(), Goblin(), new SequenceRandom(3, 1));

        Assert.Equal(12, result.Attack.TargetArmorClass);
        Assert.Equal(12, result.Attack.Total);
        Assert.True(result.IsHit);
    }

    [Fact]
    public void ACriticalCarriesItsMultiplierThroughToTheDamage()
    {
        var goblin = Goblin();

        // Natural 20, confirmed on a natural 20, then two weapon rolls.
        var result = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(20, 20, 1, 2));

        Assert.True(result.IsCritical);
        Assert.Equal(2, result.Damage!.CriticalMultiplier);
        Assert.Equal(15, result.Damage.Total);   // (1+6) + (2+6)
    }

    [Fact]
    public void DroppingAndKillingAreReportedSeparately()
    {
        var goblin = Goblin();

        var dropped = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1));
        Assert.True(dropped.Dropped);
        Assert.False(dropped.Killed);
        Assert.Equal(HitPointState.Healthy, dropped.StateBefore);
        Assert.Equal(HitPointState.Dying, dropped.StateAfter);

        var killed = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 8));
        Assert.True(killed.Killed);
        Assert.Equal(HitPointState.Dead, killed.StateAfter);
        Assert.False(goblin.IsAlive);
    }

    [Fact]
    public void TemporaryHitPointsAbsorbTheBlow()
    {
        var goblin = Goblin();
        goblin.HitPoints.GrantTemporary(10);

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1));

        Assert.Equal(7, result.Damage!.Total);
        Assert.Equal(0, result.DamageDealt);
        Assert.Equal(5, goblin.HitPoints.Current);
        Assert.Equal(3, goblin.HitPoints.Temporary);
    }

    [Fact]
    public void AFlatFootedTargetIsEasierToHit()
    {
        var goblin = Goblin();

        // 1 + 9 = 10: short of AC 12, enough for flat-footed AC 10.
        Assert.False(Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(2)).IsHit);
        Assert.True(Strike
            .Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(2, 1), DefenseOptions.DexterityDenied)
            .IsHit);
    }

    [Fact]
    public void ATouchAttackResolvesAgainstTouchArmourClass()
    {
        var goblin = Goblin();
        goblin.ArmorClass.Modifiers.Add(4, BonusType.Armor, "Studded Leather");

        var ray = WeaponAttack.Create("scorching ray", 5, "4d6", DamageType.Fire);
        ray.Attack.TargetsTouchArmorClass = true;

        var result = Strike.Resolve(Fighter(), ray, goblin, new SequenceRandom(7, 1, 1, 1, 1));

        Assert.Equal(12, result.Attack.TargetArmorClass);   // armour bypassed
        Assert.True(result.IsHit);
        Assert.Equal(4, result.Damage!.Total);
    }

    [Fact]
    public void BuffingTheTargetCanTurnAHitIntoAMiss()
    {
        var goblin = Goblin();
        Assert.True(Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 1)).IsHit);

        goblin.Abilities.Dexterity.Modifiers.Add(4, BonusType.Enhancement, "Cat's Grace");

        Assert.False(Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3)).IsHit);
    }

    [Fact]
    public void BuffingConstitutionCanSurviveTheBlowThatWouldKill()
    {
        var goblin = Goblin();
        goblin.HitPoints.Take(4);   // 1 hit point left, death at -11

        goblin.Abilities.Constitution.Modifiers.Add(4, BonusType.Enhancement, "Bear's Endurance");

        var result = Strike.Resolve(Fighter(), Longsword(), goblin, new SequenceRandom(3, 8));

        Assert.Equal(14, result.Damage!.Total);
        Assert.False(result.Killed);   // maximum rose to 7, death threshold to -15
        Assert.Equal(HitPointState.Dying, result.StateAfter);
    }

    [Fact]
    public void FormatsTheWholeExchangeForTheCombatLog()
    {
        Assert.Equal(
            "Fighter attacks Goblin (longsword): d20 [3] +9 = 12 vs AC 12 — hit; "
            + "7 slashing (1d8+6: [1] +6 = 7) = 7 damage; Goblin -2/5 hp, dying",
            Strike.Resolve(Fighter(), Longsword(), Goblin(), new SequenceRandom(3, 1)).ToString());

        Assert.Equal(
            "Fighter attacks Goblin (longsword): d20 [2] +9 = 11 vs AC 12 — miss",
            Strike.Resolve(Fighter(), Longsword(), Goblin(), new SequenceRandom(2)).ToString());
    }

    [Fact]
    public void RejectsMissingArguments()
    {
        var fighter = Fighter();
        var goblin = Goblin();
        var weapon = Longsword();
        var random = new SequenceRandom(3, 1);

        Assert.Throws<ArgumentNullException>(() => Strike.Resolve(null!, weapon, goblin, random));
        Assert.Throws<ArgumentNullException>(() => Strike.Resolve(fighter, null!, goblin, random));
        Assert.Throws<ArgumentNullException>(() => Strike.Resolve(fighter, weapon, null!, random));
        Assert.Throws<ArgumentNullException>(() => Strike.Resolve(fighter, weapon, goblin, null!));
    }
}

public class RuleOptionsTests
{
    private static Creature Target(int armorClass)
    {
        var creature = new Creature("Target", AbilityScores.All(10), 100, 1);
        if (armorClass != 10)
        {
            creature.ArmorClass.Modifiers.Add(armorClass - 10, BonusType.Untyped, "Test Padding");
        }

        return creature;
    }

    private static Attack WithBonus(int bonus, CriticalProfile? critical = null)
    {
        var attack = new Attack();
        attack.Modifiers.Add(bonus, BonusType.Untyped, "Attack Bonus");
        if (critical is { } profile)
        {
            attack.Critical = profile;
        }

        return attack;
    }

    [Fact]
    public void TheDefaultsAreTheRulesAsWritten()
    {
        var rules = RuleOptions.Pathfinder;

        Assert.True(rules.ConfirmCriticals);
        Assert.True(rules.NaturalTwentyAlwaysHits);
        Assert.True(rules.NaturalOneAlwaysMisses);
        Assert.True(rules.DeathsDoor);
        Assert.Equal(HitPointGeneration.Average, rules.HitPointGeneration);
    }

    [Fact]
    public void ANaturalTwentyKeepsEveryFightWinnable()
    {
        // +0 against armour class 40: only the natural 20 rule can land this.
        var result = WithBonus(0).Resolve(Target(40).ArmorClass, new SequenceRandom(20, 5));

        Assert.True(result.IsHit);
    }

    [Fact]
    public void RemovingTheNaturalTwentyMakesArmourClassAbsolute()
    {
        var rules = new RuleOptions { NaturalTwentyAlwaysHits = false };

        var result = WithBonus(0).Resolve(
            Target(40).ArmorClass, new SequenceRandom(20), DefenseOptions.None, rules);

        Assert.False(result.IsHit);
        Assert.False(result.Threatened);
    }

    [Fact]
    public void RemovingTheNaturalOneLetsAnOverwhelmingBonusAlwaysConnect()
    {
        var rules = new RuleOptions { NaturalOneAlwaysMisses = false };

        var result = WithBonus(50).Resolve(
            Target(12).ArmorClass, new SequenceRandom(1, 1), DefenseOptions.None, rules);

        Assert.True(result.IsHit);
    }

    [Fact]
    public void SkippingConfirmationMakesEveryThreatACritical()
    {
        var rules = new RuleOptions { ConfirmCriticals = false };
        var random = new SequenceRandom(19);

        var result = WithBonus(9, new CriticalProfile(19, 2))
            .Resolve(Target(12).ArmorClass, random, DefenseOptions.None, rules);

        Assert.Equal(AttackOutcome.CriticalHit, result.Outcome);
        Assert.Equal(2, result.CriticalMultiplier);
        Assert.Null(result.ConfirmationNatural);
        Assert.Equal(1, random.Consumed);
    }

    [Fact]
    public void ThreatsStillNeedToHitWhenConfirmationIsOff()
    {
        var rules = new RuleOptions { ConfirmCriticals = false };

        var result = WithBonus(0, new CriticalProfile(19, 2))
            .Resolve(Target(30).ArmorClass, new SequenceRandom(19), DefenseOptions.None, rules);

        Assert.Equal(AttackOutcome.Miss, result.Outcome);
    }

    [Fact]
    public void ACreatureCarriesItsOptionsIntoItsHitPoints()
    {
        var rules = new RuleOptions { DeathsDoor = false };
        var creature = new Creature("Mook", AbilityScores.All(14), 10, 1);
        var brittle = new Creature("Mook", AbilityScores.All(14), 10, 1, rules);

        // Maximum is 12: one point past it.
        creature.HitPoints.Take(13);
        brittle.HitPoints.Take(13);

        Assert.Equal(HitPointState.Dying, creature.HitPoints.State);
        Assert.Equal(HitPointState.Dead, brittle.HitPoints.State);
    }
}
