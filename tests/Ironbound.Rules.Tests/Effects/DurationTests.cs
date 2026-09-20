using Ironbound.Rules.Effects;

namespace Ironbound.Rules.Tests.Effects;

public class DurationTests
{
    [Fact]
    public void ARoundIsSixtyTicks()
    {
        Assert.Equal(60, Duration.TicksPerRound);
        Assert.Equal(60, Duration.Rounds(1).Ticks);
        Assert.Equal(600, Duration.Minutes(1).Ticks);
        Assert.Equal(36_000, Duration.Hours(1).Ticks);
        Assert.Equal(864_000, Duration.Days(1).Ticks);
    }

    [Fact]
    public void TenRoundsMakeAMinute()
    {
        Assert.Equal(Duration.Minutes(1), Duration.Rounds(10));
    }

    [Fact]
    public void PermanenceIsAFlagNotATickCount()
    {
        Assert.True(Duration.Permanent.IsPermanent);
        Assert.False(Duration.Zero.IsPermanent);
        Assert.True(Duration.Zero.IsZero);
        Assert.False(Duration.Permanent.IsZero);
    }

    [Theory]
    [InlineData(0, "instant")]
    [InlineData(18, "18 ticks")]
    [InlineData(60, "1 round")]
    [InlineData(180, "3 rounds")]
    [InlineData(600, "1 minute")]
    [InlineData(72_000, "2 hours")]
    [InlineData(864_000, "1 day")]
    public void FormatsUsingTheLargestUnitThatFits(int ticks, string text)
    {
        Assert.Equal(text, Duration.FromTicks(ticks).ToString());
    }

    [Fact]
    public void PermanentFormatsAsItself()
    {
        Assert.Equal("permanent", Duration.Permanent.ToString());
    }

    [Fact]
    public void PermanenceOutranksEveryFiniteSpan()
    {
        Assert.True(Duration.Permanent > Duration.Days(365));
        Assert.True(Duration.Rounds(2) > Duration.Rounds(1));
        Assert.True(Duration.Zero < Duration.FromTicks(1));
        Assert.Equal(0, Duration.Permanent.CompareTo(Duration.Permanent));
    }

    [Fact]
    public void ArithmeticAddsTicksAndPermanenceSwallowsEverything()
    {
        Assert.Equal(Duration.Rounds(3), Duration.Rounds(1) + Duration.Rounds(2));
        Assert.Equal(Duration.Rounds(1), Duration.Rounds(3) - Duration.Rounds(2));

        // Never negative.
        Assert.Equal(Duration.Zero, Duration.Rounds(1) - Duration.Rounds(5));

        Assert.True((Duration.Permanent + Duration.Rounds(1)).IsPermanent);
        Assert.True((Duration.Rounds(1) + Duration.Permanent).IsPermanent);
        Assert.True((Duration.Permanent - Duration.Days(1)).IsPermanent);
    }

    [Fact]
    public void RejectsNegativeSpans()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.FromTicks(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.Rounds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.Minutes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.Hours(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Duration.Days(-1));
    }
}
