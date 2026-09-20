using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Modifiers;

public class ModifierStackTests
{
    [Fact]
    public void EmptyStackTotalsZero()
    {
        Assert.Equal(0, new ModifierStack().Total);
    }

    [Fact]
    public void SingleModifierApplies()
    {
        var stack = new ModifierStack { new Modifier(4, BonusType.Armor, "Mage Armor") };

        Assert.Equal(4, stack.Total);
    }

    [Fact]
    public void SameTypeBonusesDoNotStack_OnlyLargestCounts()
    {
        var stack = new ModifierStack
        {
            new Modifier(4, BonusType.Armor, "Mage Armor"),
            new Modifier(6, BonusType.Armor, "Breastplate"),
        };

        Assert.Equal(6, stack.Total);
    }

    [Fact]
    public void SameTypeBonusesOfEqualValue_CountOnce()
    {
        var stack = new ModifierStack
        {
            new Modifier(2, BonusType.Luck, "Divine Favor"),
            new Modifier(2, BonusType.Luck, "Lucky Charm"),
        };

        Assert.Equal(2, stack.Total);

        // The earlier one wins the tie, so the result never flickers between saves.
        var breakdown = stack.Explain();
        Assert.True(breakdown.Entries[0].Applied);
        Assert.False(breakdown.Entries[1].Applied);
        Assert.Equal("Divine Favor", breakdown.Entries[1].SuppressedBy);
    }

    [Fact]
    public void DifferentTypesStack()
    {
        var stack = new ModifierStack
        {
            new Modifier(4, BonusType.Armor, "Mage Armor"),
            new Modifier(2, BonusType.Deflection, "Ring of Protection"),
            new Modifier(1, BonusType.NaturalArmor, "Barkskin"),
        };

        Assert.Equal(7, stack.Total);
    }

    [Fact]
    public void UntypedBonusesStackWithEachOther()
    {
        var stack = new ModifierStack
        {
            Modifier.Untyped(10, "Base"),
            Modifier.Untyped(3, "Dexterity"),
            Modifier.Untyped(1, "Class Feature"),
        };

        Assert.Equal(14, stack.Total);
    }

    [Fact]
    public void DodgeBonusesStack_EvenFromTheSameSource()
    {
        var stack = new ModifierStack
        {
            new Modifier(1, BonusType.Dodge, "Dodge Feat"),
            new Modifier(1, BonusType.Dodge, "Haste"),
            new Modifier(1, BonusType.Dodge, "Haste"),
        };

        Assert.Equal(3, stack.Total);
    }

    [Fact]
    public void CircumstanceBonusesStackAcrossSources()
    {
        var stack = new ModifierStack
        {
            new Modifier(2, BonusType.Circumstance, "High Ground"),
            new Modifier(2, BonusType.Circumstance, "Flanking"),
        };

        Assert.Equal(4, stack.Total);
    }

    [Fact]
    public void CircumstanceBonusesFromOneSourceCountOnce()
    {
        var stack = new ModifierStack
        {
            new Modifier(2, BonusType.Circumstance, "High Ground"),
            new Modifier(3, BonusType.Circumstance, "High Ground"),
            new Modifier(2, BonusType.Circumstance, "Flanking"),
        };

        Assert.Equal(5, stack.Total);
    }

    [Fact]
    public void PenaltiesAlwaysStack_EvenWhenSameType()
    {
        var stack = new ModifierStack
        {
            new Modifier(-2, BonusType.Size, "Enlarge Person"),
            new Modifier(-2, BonusType.Size, "Clumsy Growth"),
        };

        Assert.Equal(-4, stack.Total);
    }

    [Fact]
    public void PenaltyDoesNotSuppressBonusOfSameType()
    {
        var stack = new ModifierStack
        {
            new Modifier(4, BonusType.Enhancement, "Bull's Strength"),
            new Modifier(-2, BonusType.Enhancement, "Cursed Gauntlets"),
        };

        Assert.Equal(2, stack.Total);
    }

    [Fact]
    public void BonusDoesNotSuppressPenaltyOfSameType()
    {
        var stack = new ModifierStack
        {
            new Modifier(-1, BonusType.Morale, "Shaken"),
            new Modifier(3, BonusType.Morale, "Bless"),
            new Modifier(1, BonusType.Morale, "Inspire Courage"),
        };

        // -1 penalty applies, the two morale bonuses resolve to the larger one.
        Assert.Equal(2, stack.Total);
    }

    [Fact]
    public void TotalIsIndependentOfInsertionOrder()
    {
        var forwards = new ModifierStack
        {
            new Modifier(2, BonusType.Enhancement, "Potion"),
            new Modifier(5, BonusType.Enhancement, "Weapon"),
            new Modifier(1, BonusType.Dodge, "Feat"),
        };

        var backwards = new ModifierStack
        {
            new Modifier(1, BonusType.Dodge, "Feat"),
            new Modifier(5, BonusType.Enhancement, "Weapon"),
            new Modifier(2, BonusType.Enhancement, "Potion"),
        };

        Assert.Equal(forwards.Total, backwards.Total);
        Assert.Equal(6, forwards.Total);
    }

    [Fact]
    public void RemoveAllFromDropsEveryModifierOfThatSource()
    {
        var stack = new ModifierStack
        {
            new Modifier(4, BonusType.Armor, "Mage Armor"),
            new Modifier(1, BonusType.Dodge, "Haste"),
            new Modifier(1, BonusType.Untyped, "Haste"),
        };
        Assert.Equal(6, stack.Total);

        var removed = stack.RemoveAllFrom("Haste");

        Assert.Equal(2, removed);
        Assert.Equal(4, stack.Total);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void RemoveAllFromUnknownSourceChangesNothing()
    {
        var stack = new ModifierStack { new Modifier(4, BonusType.Armor, "Mage Armor") };

        Assert.Equal(0, stack.RemoveAllFrom("Shield of Faith"));
        Assert.Equal(4, stack.Total);
    }

    [Fact]
    public void RemovingASuppressorLetsTheLesserBonusApply()
    {
        var stack = new ModifierStack
        {
            new Modifier(4, BonusType.Armor, "Mage Armor"),
            new Modifier(6, BonusType.Armor, "Breastplate"),
        };
        Assert.Equal(6, stack.Total);

        stack.RemoveAllFrom("Breastplate");

        Assert.Equal(4, stack.Total);
    }

    [Fact]
    public void ClearEmptiesTheStack()
    {
        var stack = new ModifierStack { new Modifier(4, BonusType.Armor, "Mage Armor") };

        stack.Clear();

        Assert.Equal(0, stack.Count);
        Assert.Equal(0, stack.Total);
    }

    [Fact]
    public void ExplainListsEveryModifierAndWhySuppressedOnesLost()
    {
        var stack = new ModifierStack
        {
            Modifier.Untyped(10, "Base"),
            new Modifier(4, BonusType.Armor, "Mage Armor"),
            new Modifier(6, BonusType.Armor, "Breastplate"),
        };

        var breakdown = stack.Explain();

        Assert.Equal(3, breakdown.Entries.Count);
        Assert.Equal(16, breakdown.Total);
        Assert.Equal(2, breakdown.Applied.Count());
        var lost = Assert.Single(breakdown.Suppressed);
        Assert.Equal("Mage Armor", lost.Modifier.Source);
        Assert.Equal("Breastplate", lost.SuppressedBy);
    }

    [Fact]
    public void ExplainAgreesWithTotal()
    {
        var stack = new ModifierStack
        {
            Modifier.Untyped(10, "Base"),
            new Modifier(2, BonusType.Circumstance, "Flanking"),
            new Modifier(2, BonusType.Circumstance, "Flanking"),
            new Modifier(-4, BonusType.Untyped, "Prone"),
            new Modifier(1, BonusType.Dodge, "Feat"),
        };

        Assert.Equal(stack.Total, stack.Explain().Total);
        Assert.Equal(9, stack.Total);
    }

    [Fact]
    public void ModifierFormatsForTheCombatLog()
    {
        Assert.Equal("+4 armor (Mage Armor)", new Modifier(4, BonusType.Armor, "Mage Armor").ToString());
        Assert.Equal("-2 size (Enlarge)", new Modifier(-2, BonusType.Size, "Enlarge").ToString());
        Assert.Equal("+1 natural armor (Barkskin)", new Modifier(1, BonusType.NaturalArmor, "Barkskin").ToString());
    }

    [Fact]
    public void ZeroValuedModifierAppliesAndContributesNothing()
    {
        var stack = new ModifierStack
        {
            new Modifier(0, BonusType.Competence, "Masterwork Tool"),
            new Modifier(3, BonusType.Competence, "Heroism"),
        };

        Assert.Equal(3, stack.Total);
    }
}
