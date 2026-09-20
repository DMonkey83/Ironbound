using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Modifiers;

public class FilteredModifierStackTests
{
    private static ModifierStack SampleStack() => new()
    {
        new Modifier(6, BonusType.Armor, "Breastplate"),
        new Modifier(4, BonusType.Armor, "Mage Armor"),
        new Modifier(2, BonusType.Deflection, "Ring of Protection"),
    };

    [Fact]
    public void FilteringToNothingTotalsZero()
    {
        Assert.Equal(0, SampleStack().TotalWhere(_ => false));
    }

    [Fact]
    public void AcceptingEverythingMatchesThePlainTotal()
    {
        var stack = SampleStack();

        Assert.Equal(stack.Total, stack.TotalWhere(_ => true));
    }

    [Fact]
    public void AnExcludedModifierCannotSuppressAnIncludedOne()
    {
        var stack = SampleStack();

        // Drop the breastplate from consideration; mage armour is then the best armour bonus.
        var total = stack.TotalWhere(m => m.Source != "Breastplate");

        Assert.Equal(6, total);
    }

    [Fact]
    public void ExcludedModifiersAreAbsentFromTheBreakdown()
    {
        var breakdown = SampleStack().Explain(m => m.Type != BonusType.Armor);

        Assert.Equal(2, breakdown.Total);
        var entry = Assert.Single(breakdown.Entries);
        Assert.Equal("Ring of Protection", entry.Modifier.Source);
    }

    [Fact]
    public void FilteringDoesNotDisturbTheCachedTotal()
    {
        var stack = SampleStack();
        Assert.Equal(8, stack.Total);

        stack.TotalWhere(_ => false);

        Assert.Equal(8, stack.Total);
    }

    [Fact]
    public void ANullFilterIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => SampleStack().TotalWhere(null!));
    }
}
