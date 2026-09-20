using Ironbound.Rules.Abilities;
using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Effects;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Combat;

public class CombineTests
{
    [Fact]
    public void AMagicWeaponSpellOnAMagicSwordDoesNotStack()
    {
        var wielder = new ModifierStack();
        wielder.Add(1, BonusType.Enhancement, "Magic Weapon");

        var weapon = new ModifierStack();
        weapon.Add(1, BonusType.Enhancement, "+1 Longsword");

        // Adding the two totals would give 2. Resolving them together gives the right answer.
        Assert.Equal(1, ModifierStack.Combine(wielder, weapon).Total);
    }

    [Fact]
    public void TheLargerEnhancementWinsWhicheverStackItCameFrom()
    {
        var wielder = new ModifierStack();
        wielder.Add(3, BonusType.Enhancement, "Greater Magic Weapon");

        var weapon = new ModifierStack();
        weapon.Add(1, BonusType.Enhancement, "+1 Longsword");

        Assert.Equal(3, ModifierStack.Combine(wielder, weapon).Total);
        Assert.Equal(3, ModifierStack.Combine(weapon, wielder).Total);
    }

    [Fact]
    public void DifferentTypesFromDifferentStacksStillAdd()
    {
        var wielder = new ModifierStack();
        wielder.Add(1, BonusType.Morale, "Bless");
        wielder.Add(6, BonusType.Untyped, "Base Attack Bonus");

        var weapon = new ModifierStack();
        weapon.Add(1, BonusType.Enhancement, "+1 Longsword");

        Assert.Equal(8, ModifierStack.Combine(wielder, weapon).Total);
    }

    [Fact]
    public void TiesStillBreakOnOrderAcrossStacks()
    {
        var first = new ModifierStack();
        first.Add(2, BonusType.Luck, "Divine Favor");

        var second = new ModifierStack();
        second.Add(2, BonusType.Luck, "Lucky Charm");

        var breakdown = ModifierStack.Combine(first, second);

        Assert.Equal(2, breakdown.Total);
        Assert.Equal("Divine Favor", Assert.Single(breakdown.Applied).Modifier.Source);
    }

    [Fact]
    public void CombiningOneStackMatchesExplainingIt()
    {
        var stack = new ModifierStack();
        stack.Add(6, BonusType.Armor, "Breastplate");
        stack.Add(4, BonusType.Armor, "Mage Armor");

        Assert.Equal(stack.Explain().Total, ModifierStack.Combine(stack).Total);
    }

    [Fact]
    public void CombiningNothingIsZero()
    {
        Assert.Equal(0, ModifierStack.Combine().Total);
    }
}

public class AbilityDamageScaleTests
{
    private static WeaponAttack Scaled(AbilityDamageScale scale) =>
        WeaponAttack.Melee("blade", "1d8", DamageType.Slashing, scale: scale);

    [Theory]
    [InlineData(AbilityDamageScale.None, 4, 0)]
    [InlineData(AbilityDamageScale.Half, 4, 2)]
    [InlineData(AbilityDamageScale.Full, 4, 4)]
    [InlineData(AbilityDamageScale.OneAndAHalf, 4, 6)]
    [InlineData(AbilityDamageScale.OneAndAHalf, 5, 7)]
    [InlineData(AbilityDamageScale.Half, 5, 2)]
    public void ScalesAndFloors(AbilityDamageScale scale, int modifier, int expected)
    {
        Assert.Equal(expected, Scaled(scale).ScaleDamage(modifier));
    }

    [Theory]
    [InlineData(AbilityDamageScale.Half, -1, -1)]
    [InlineData(AbilityDamageScale.OneAndAHalf, -1, -2)]
    [InlineData(AbilityDamageScale.OneAndAHalf, -3, -5)]
    public void FlooringGoesTowardNegativeInfinityForPenaltiesToo(
        AbilityDamageScale scale, int modifier, int expected)
    {
        Assert.Equal(expected, Scaled(scale).ScaleDamage(modifier));
    }
}

public class WielderBonusTests
{
    /// <summary>Strength 18, Dexterity 14, base attack bonus 6.</summary>
    private static Creature Fighter()
    {
        var fighter = new Creature("Fighter", new AbilityScores(18, 14, 14, 10, 12, 10), 40, 6);
        fighter.AttackModifiers.Add(6, BonusType.Untyped, "Base Attack Bonus");
        return fighter;
    }

    /// <summary>Natural armour brings it to 16, so a buff can decide the swing.</summary>
    private static Creature Goblin()
    {
        var goblin = new Creature("Goblin", new AbilityScores(11, 15, 11, 10, 9, 6), 30, 4);
        goblin.ArmorClass.Modifiers.Add(4, BonusType.NaturalArmor, "Hide");
        return goblin;
    }

    private static WeaponAttack Longsword() =>
        WeaponAttack.Melee("longsword", "1d8", DamageType.Slashing);

    private static ModifierEffect BullsStrength() =>
        new ModifierEffect("Bull's Strength", Duration.Minutes(1))
            .GrantsToAbility(Ability.Strength, 4, BonusType.Enhancement);

    [Fact]
    public void StrengthReachesBothTheAttackRollAndTheDamage()
    {
        var fighter = Fighter();
        var sword = Longsword();

        Assert.Equal(10, Strike.AttackBonus(fighter, sword).Total);   // 6 base attack, +4 Strength
        Assert.Equal(4, Strike.DamageBonus(fighter, sword).Total);
    }

    [Fact]
    public void BullsStrengthNowChangesMoreThanTheScore()
    {
        var fighter = Fighter();
        var sword = Longsword();

        fighter.Effects.Apply(BullsStrength());

        Assert.Equal(12, Strike.AttackBonus(fighter, sword).Total);
        Assert.Equal(6, Strike.DamageBonus(fighter, sword).Total);
    }

    [Fact]
    public void AndPutsThemBackWhenItEnds()
    {
        var fighter = Fighter();
        var sword = Longsword();
        fighter.Effects.Apply(BullsStrength());

        fighter.Effects.Advance(Duration.Minutes(1), new SequenceRandom(1));

        Assert.Equal(10, Strike.AttackBonus(fighter, sword).Total);
        Assert.Equal(4, Strike.DamageBonus(fighter, sword).Total);
    }

    [Fact]
    public void TwoHandedGetsHalfAgainOfStrength()
    {
        var fighter = Fighter();
        var greatsword = WeaponAttack.Melee(
            "greatsword", "2d6", DamageType.Slashing, scale: AbilityDamageScale.OneAndAHalf);

        Assert.Equal(6, Strike.DamageBonus(fighter, greatsword).Total);

        fighter.Effects.Apply(BullsStrength());
        Assert.Equal(9, Strike.DamageBonus(fighter, greatsword).Total);
    }

    [Fact]
    public void AFinessedBladeAttacksWithDexterityAndStillDamagesWithStrength()
    {
        var fighter = Fighter();
        var rapier = WeaponAttack.Melee(
            "rapier", "1d6", DamageType.Piercing, attackAbility: Ability.Dexterity);

        Assert.Equal(8, Strike.AttackBonus(fighter, rapier).Total);   // 6 base attack, +2 Dexterity
        Assert.Equal(4, Strike.DamageBonus(fighter, rapier).Total);
    }

    [Fact]
    public void BlessAddsToTheAttackRollButNotToDamage()
    {
        var fighter = Fighter();
        var sword = Longsword();

        fighter.Effects.Apply(new ModifierEffect("Bless", Duration.Minutes(1))
            .GrantsToAttack(1, BonusType.Morale));

        Assert.Equal(11, Strike.AttackBonus(fighter, sword).Total);
        Assert.Equal(4, Strike.DamageBonus(fighter, sword).Total);
    }

    [Fact]
    public void InspireCourageAddsToBoth()
    {
        var fighter = Fighter();
        var sword = Longsword();

        fighter.Effects.Apply(new ModifierEffect("Inspire Courage", Duration.Rounds(5))
            .GrantsToAttack(2, BonusType.Morale)
            .GrantsToDamage(2, BonusType.Morale));

        Assert.Equal(12, Strike.AttackBonus(fighter, sword).Total);
        Assert.Equal(6, Strike.DamageBonus(fighter, sword).Total);
    }

    [Fact]
    public void AMagicWeaponSpellOnAMagicSwordGivesOneNotTwo()
    {
        var fighter = Fighter();
        var sword = Longsword();
        sword.Attack.Modifiers.Add(1, BonusType.Enhancement, "+1 Longsword");

        fighter.Effects.Apply(new ModifierEffect("Magic Weapon", Duration.Minutes(1))
            .GrantsToAttack(1, BonusType.Enhancement));

        Assert.Equal(11, Strike.AttackBonus(fighter, sword).Total);
    }

    [Fact]
    public void ABuffCanDecideWhetherTheSwingConnects()
    {
        var fighter = Fighter();
        var goblin = Goblin();
        var sword = Longsword();

        // 4 + 10 = 14 against armour class 16.
        Assert.False(Strike.Resolve(fighter, sword, goblin, new SequenceRandom(4)).IsHit);

        fighter.Effects.Apply(BullsStrength());

        // The same roll, now 4 + 12 = 16.
        Assert.True(Strike.Resolve(fighter, sword, goblin, new SequenceRandom(4, 5)).IsHit);
    }

    [Fact]
    public void TheDamageBonusIsFoldedIntoOneExpression()
    {
        var fighter = Fighter();

        var result = Strike.Resolve(fighter, Longsword(), Goblin(), new SequenceRandom(6, 5));

        Assert.Equal(9, result.Damage!.Total);
        Assert.Equal("1d8+4: [5] +4 = 9", result.Damage.Entries[0].Roll.ToString());
    }

    [Fact]
    public void TheFoldedBonusIsMultipliedOnACritical()
    {
        var fighter = Fighter();

        // Natural 20, confirmed, then two weapon rolls of 1d8+4.
        var result = Strike.Resolve(fighter, Longsword(), Goblin(), new SequenceRandom(20, 20, 5, 3));

        Assert.True(result.IsCritical);
        Assert.Equal(16, result.Damage!.Total);   // (5+4) + (3+4)
    }

    [Fact]
    public void AWeaponWithPreComputedNumbersIsLeftAlone()
    {
        var fighter = Fighter();
        var plain = WeaponAttack.Create("club", 3, "1d6+1", DamageType.Bludgeoning);

        // No derivation, so the creature's base attack bonus is all that joins the weapon's.
        Assert.Equal(9, Strike.AttackBonus(fighter, plain).Total);
        Assert.Equal(0, Strike.DamageBonus(fighter, plain).Total);

        var result = Strike.Resolve(fighter, plain, Goblin(), new SequenceRandom(8, 4));
        Assert.Equal("1d6+1: [4] +1 = 5", result.Damage!.Entries[0].Roll.ToString());
    }
}

public class DiceExpressionPlusTests
{
    [Theory]
    [InlineData("1d8", 6, "1d8+6")]
    [InlineData("1d8+2", 4, "1d8+6")]
    [InlineData("1d8+6", -6, "1d8")]
    [InlineData("2d6", 0, "2d6")]
    [InlineData("1d8-2", 2, "1d8")]
    [InlineData("1d8+1d6+1", 3, "1d8+1d6+4")]
    public void FoldsIntoAnyConstantAlreadyThere(string text, int amount, string expected)
    {
        Assert.Equal(expected, DiceExpression.Parse(text).Plus(amount).ToString());
    }

    [Fact]
    public void AConstantPlusAConstantIsAConstant()
    {
        Assert.Equal(DiceExpression.Constant(5), DiceExpression.Constant(3).Plus(2));
    }

    [Fact]
    public void TheRangeMovesWithIt()
    {
        var expression = DiceExpression.Parse("1d8").Plus(4);

        Assert.Equal(5, expression.Minimum);
        Assert.Equal(12, expression.Maximum);
        Assert.Equal(8.5, expression.Average, 9);
    }
}
