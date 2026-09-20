using Ironbound.Rules.Abilities;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Defense;

public class ArmorClassTests
{
    private static ArmorClass WithDexterity(int score) =>
        new(new AbilityScore(Ability.Dexterity, score));

    [Fact]
    public void AnUnarmouredAverageCreatureIsTen()
    {
        var armorClass = WithDexterity(10);

        Assert.Equal(10, armorClass.Total);
        Assert.Equal(10, armorClass.Touch);
        Assert.Equal(10, armorClass.FlatFooted);
    }

    [Fact]
    public void DexterityBonusCountsAndIsLostWhenFlatFooted()
    {
        var armorClass = WithDexterity(14);

        Assert.Equal(12, armorClass.Total);
        Assert.Equal(12, armorClass.Touch);
        Assert.Equal(10, armorClass.FlatFooted);
    }

    [Fact]
    public void DexterityPenaltyAppliesEvenWhenFlatFooted()
    {
        var armorClass = WithDexterity(6);

        Assert.Equal(8, armorClass.Total);
        Assert.Equal(8, armorClass.FlatFooted);
    }

    [Fact]
    public void ArmourIsIgnoredByTouchAttacksButKeptWhenFlatFooted()
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");

        Assert.Equal(16, armorClass.Total);
        Assert.Equal(10, armorClass.Touch);
        Assert.Equal(16, armorClass.FlatFooted);
    }

    [Theory]
    [InlineData(BonusType.Shield, "Heavy Steel Shield")]
    [InlineData(BonusType.NaturalArmor, "Barkskin")]
    public void ShieldsAndNaturalArmourAlsoMissFromTouchAc(BonusType type, string source)
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(2, type, source);

        Assert.Equal(12, armorClass.Total);
        Assert.Equal(10, armorClass.Touch);
    }

    [Theory]
    [InlineData(BonusType.Deflection, "Ring of Protection")]
    [InlineData(BonusType.Size, "Small")]
    [InlineData(BonusType.Insight, "Foresight")]
    public void DeflectionSizeAndInsightSurviveATouchAttack(BonusType type, string source)
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(1, type, source);

        Assert.Equal(11, armorClass.Total);
        Assert.Equal(11, armorClass.Touch);
    }

    [Fact]
    public void DodgeIsLostAlongsideDexterity()
    {
        var armorClass = WithDexterity(14);
        armorClass.Modifiers.Add(1, BonusType.Dodge, "Dodge Feat");

        Assert.Equal(13, armorClass.Total);
        Assert.Equal(13, armorClass.Touch);
        Assert.Equal(10, armorClass.FlatFooted);
    }

    [Fact]
    public void ArmourCapsTheDexterityBonus()
    {
        var armorClass = WithDexterity(18);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.CapDexterity("Breastplate", 3);

        Assert.Equal(19, armorClass.Total);
        Assert.Equal(13, armorClass.Touch);
        Assert.Equal(16, armorClass.FlatFooted);
    }

    [Fact]
    public void ACapAboveYourDexterityChangesNothing()
    {
        var armorClass = WithDexterity(14);
        armorClass.CapDexterity("Leather", 6);

        Assert.Equal(12, armorClass.Total);
    }

    [Fact]
    public void ACapDoesNotSoftenADexterityPenalty()
    {
        var armorClass = WithDexterity(6);
        armorClass.CapDexterity("Full Plate", 1);

        Assert.Equal(8, armorClass.Total);
    }

    [Fact]
    public void TheLowestCapWins()
    {
        var armorClass = WithDexterity(18);
        armorClass.CapDexterity("Breastplate", 3);
        armorClass.CapDexterity("Tower Shield", 2);

        Assert.Equal(2, armorClass.MaxDexterityBonus);
        Assert.Equal(12, armorClass.Total);
    }

    [Fact]
    public void DroppingOneCapRestoresTheNextTightestOne()
    {
        var armorClass = WithDexterity(18);
        armorClass.CapDexterity("Breastplate", 3);
        armorClass.CapDexterity("Tower Shield", 2);

        Assert.True(armorClass.RemoveDexterityCap("Tower Shield"));

        // Not uncapped: the breastplate is still on.
        Assert.Equal(3, armorClass.MaxDexterityBonus);
        Assert.Equal(13, armorClass.Total);
    }

    [Fact]
    public void RemovingTheLastCapUncapsDexterity()
    {
        var armorClass = WithDexterity(18);
        armorClass.CapDexterity("Breastplate", 3);

        armorClass.RemoveDexterityCap("Breastplate");

        Assert.Null(armorClass.MaxDexterityBonus);
        Assert.Equal(14, armorClass.Total);
    }

    [Fact]
    public void TwoArmourBonusesDoNotStack()
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(4, BonusType.Armor, "Mage Armor");
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");

        Assert.Equal(16, armorClass.Total);
    }

    [Fact]
    public void AnExcludedModifierCannotSuppressOne()
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.Modifiers.Add(2, BonusType.Deflection, "Ring of Protection");

        // The breastplate is bypassed, so touch AC keeps the deflection bonus in full.
        Assert.Equal(12, armorClass.Touch);
    }

    [Fact]
    public void AFullyEquippedFighterAddsUp()
    {
        var armorClass = WithDexterity(14);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.CapDexterity("Breastplate", 3);
        armorClass.Modifiers.Add(2, BonusType.Shield, "Heavy Steel Shield");
        armorClass.Modifiers.Add(1, BonusType.Deflection, "Ring of Protection");
        armorClass.Modifiers.Add(1, BonusType.Dodge, "Dodge Feat");

        Assert.Equal(22, armorClass.Total);
        Assert.Equal(14, armorClass.Touch);
        Assert.Equal(19, armorClass.FlatFooted);
        Assert.Equal(11, armorClass.Value(DefenseOptions.TouchAttack | DefenseOptions.DexterityDenied));
    }

    [Fact]
    public void BuffsAndPenaltiesFlowThroughFromTheAbilityScore()
    {
        var dexterity = new AbilityScore(Ability.Dexterity, 14);
        var armorClass = new ArmorClass(dexterity);
        Assert.Equal(12, armorClass.Total);

        dexterity.Modifiers.Add(4, BonusType.Enhancement, "Cat's Grace");
        Assert.Equal(14, armorClass.Total);

        dexterity.Modifiers.RemoveAllFrom("Cat's Grace");
        Assert.Equal(12, armorClass.Total);
    }

    [Fact]
    public void ACreatureWithNoDexterityScoreGetsNothingFromIt()
    {
        var armorClass = new ArmorClass(AbilityScore.NonAbility(Ability.Dexterity));
        armorClass.Modifiers.Add(4, BonusType.NaturalArmor, "Ooze");

        Assert.Equal(14, armorClass.Total);
        Assert.Equal(10, armorClass.Touch);
        Assert.Equal(14, armorClass.FlatFooted);
    }

    [Theory]
    [InlineData(DefenseOptions.None)]
    [InlineData(DefenseOptions.TouchAttack)]
    [InlineData(DefenseOptions.DexterityDenied)]
    [InlineData(DefenseOptions.TouchAttack | DefenseOptions.DexterityDenied)]
    public void ExplainAccountsForTheWholeNumber(DefenseOptions options)
    {
        var armorClass = WithDexterity(18);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.CapDexterity("Breastplate", 3);
        armorClass.Modifiers.Add(4, BonusType.Armor, "Mage Armor");
        armorClass.Modifiers.Add(1, BonusType.Deflection, "Ring of Protection");
        armorClass.Modifiers.Add(1, BonusType.Dodge, "Dodge Feat");

        Assert.Equal(armorClass.Value(options), armorClass.Explain(options).Total);
    }

    [Fact]
    public void ExplainNamesTheArmourThatCappedDexterity()
    {
        var armorClass = WithDexterity(18);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.CapDexterity("Breastplate", 3);

        Assert.Equal(
            "+10 untyped (Base) +3 untyped (Dexterity (capped at +3)) +6 armor (Breastplate) = 19",
            armorClass.Explain().ToString());
    }

    [Fact]
    public void ExplainLeavesOutWhatTheAttackBypasses()
    {
        var armorClass = WithDexterity(10);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.Modifiers.Add(1, BonusType.Deflection, "Ring of Protection");

        var touch = armorClass.Explain(DefenseOptions.TouchAttack);

        Assert.DoesNotContain(touch.Entries, e => e.Modifier.Source == "Breastplate");
        Assert.Equal(11, touch.Total);
    }

    [Fact]
    public void FormatsAsAStatBlockLine()
    {
        var armorClass = WithDexterity(18);
        armorClass.Modifiers.Add(6, BonusType.Armor, "Breastplate");
        armorClass.CapDexterity("Breastplate", 3);

        Assert.Equal("AC 19, touch 13, flat-footed 16", armorClass.ToString());
    }
}
