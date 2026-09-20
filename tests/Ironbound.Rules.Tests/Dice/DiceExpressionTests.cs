using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Tests.Dice;

public class DiceExpressionTests
{
    [Theory]
    [InlineData("1d20", "1d20")]
    [InlineData("d20", "1d20")]
    [InlineData("2d6+3", "2d6+3")]
    [InlineData("2d6-1", "2d6-1")]
    [InlineData("  2d6 + 3 ", "2d6+3")]
    [InlineData("2D6+3", "2d6+3")]
    [InlineData("4d6KH3", "4d6kh3")]
    [InlineData("1d8+1d6+2", "1d8+1d6+2")]
    [InlineData("5", "5")]
    [InlineData("-3", "-3")]
    [InlineData("+2d6", "2d6")]
    public void ParsesAndRoundTrips(string text, string canonical)
    {
        Assert.Equal(canonical, DiceExpression.Parse(text).ToString());
    }

    [Fact]
    public void FlatTermsAreFoldedTogether()
    {
        Assert.Equal("1d6+4", DiceExpression.Parse("1d6+3+1").ToString());
        Assert.Equal("1d6", DiceExpression.Parse("1d6+2-2").ToString());
        Assert.Equal("2d6+5", DiceExpression.Parse("3+2d6+2").ToString());
    }

    [Fact]
    public void KeepingEveryDieNormalisesAway()
    {
        Assert.Equal("2d20", DiceExpression.Parse("2d20kh2").ToString());
    }

    [Fact]
    public void EqualFormulasCompareEqual()
    {
        Assert.Equal(DiceExpression.Parse("2d6+3"), DiceExpression.Parse(" 2D6 + 1 + 2 "));
        Assert.NotEqual(DiceExpression.Parse("2d6+3"), DiceExpression.Parse("2d6+4"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("2d")]
    [InlineData("d")]
    [InlineData("2d6+")]
    [InlineData("2d6kh")]
    [InlineData("2d6kx1")]
    [InlineData("4d6kh5")]
    [InlineData("4d6kh0")]
    [InlineData("0d6")]
    [InlineData("2d0")]
    [InlineData("2x6")]
    [InlineData("101d6")]
    [InlineData("1d1001")]
    public void RejectsMalformedInput(string text)
    {
        Assert.False(DiceExpression.TryParse(text, out _));
        Assert.Throws<FormatException>(() => DiceExpression.Parse(text));
    }

    [Fact]
    public void TryParseRejectsNullWithoutThrowing()
    {
        Assert.False(DiceExpression.TryParse(null, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("1d20", 1, 20)]
    [InlineData("2d6+3", 5, 15)]
    [InlineData("1d6-1", 0, 5)]
    [InlineData("4d6kh3", 3, 18)]
    [InlineData("2d20kl1", 1, 20)]
    [InlineData("1d8+1d6", 2, 14)]
    [InlineData("5", 5, 5)]
    public void ReportsItsRange(string text, int minimum, int maximum)
    {
        var expression = DiceExpression.Parse(text);

        Assert.Equal(minimum, expression.Minimum);
        Assert.Equal(maximum, expression.Maximum);
    }

    [Theory]
    [InlineData("1d20", 10.5)]
    [InlineData("2d6+3", 10.0)]
    [InlineData("1d8+1d6+2", 10.0)]
    [InlineData("4d6kh3", 15869.0 / 1296.0)]
    [InlineData("2d20kh1", 13.825)]
    [InlineData("2d20kl1", 7.175)]
    [InlineData("5", 5.0)]
    public void ReportsItsExpectedValue(string text, double average)
    {
        Assert.Equal(average, DiceExpression.Parse(text).Average, 9);
    }

    [Theory]
    [InlineData(4, 6, 3, KeepMode.Highest)]
    [InlineData(3, 4, 2, KeepMode.Lowest)]
    [InlineData(4, 4, 1, KeepMode.Highest)]
    [InlineData(2, 8, 1, KeepMode.Lowest)]
    [InlineData(5, 3, 2, KeepMode.Highest)]
    public void ExpectedValueMatchesExhaustiveEnumeration(int count, int sides, int keep, KeepMode mode)
    {
        var suffix = mode == KeepMode.Highest ? "kh" : "kl";
        var expression = DiceExpression.Parse($"{count}d{sides}{suffix}{keep}");

        Assert.Equal(BruteForceAverage(count, sides, keep, mode), expression.Average, 9);
    }

    /// <summary>Walks every outcome of the dice and averages them. Only viable for small dice pools.</summary>
    private static double BruteForceAverage(int count, int sides, int keep, KeepMode mode)
    {
        var values = new int[count];
        long total = 0;
        long outcomes = 0;

        void Recurse(int depth)
        {
            if (depth == count)
            {
                var sorted = (int[])values.Clone();
                Array.Sort(sorted);
                var kept = mode == KeepMode.Highest
                    ? sorted[^keep..]
                    : sorted[..keep];
                total += kept.Sum();
                outcomes++;
                return;
            }

            for (var face = 1; face <= sides; face++)
            {
                values[depth] = face;
                Recurse(depth + 1);
            }
        }

        Recurse(0);
        return (double)total / outcomes;
    }

    [Fact]
    public void RollsScriptedDiceAndAddsTheConstant()
    {
        var roll = DiceExpression.Parse("2d6+3").Roll(new SequenceRandom(4, 6));

        Assert.Equal(13, roll.Total);
        Assert.Equal(3, roll.Constant);
        Assert.Equal([4, 6], roll.Dice.Select(d => d.Value));
        Assert.All(roll.Dice, d => Assert.True(d.Counted));
    }

    [Fact]
    public void KeepHighestDropsTheLowestDie()
    {
        var roll = DiceExpression.Parse("4d6kh3").Roll(new SequenceRandom(5, 3, 2, 6));

        Assert.Equal(14, roll.Total);
        Assert.Equal([true, true, false, true], roll.Dice.Select(d => d.Counted));
    }

    [Fact]
    public void KeepLowestDropsTheHighestDie()
    {
        var roll = DiceExpression.Parse("2d20kl1").Roll(new SequenceRandom(18, 4));

        Assert.Equal(4, roll.Total);
        Assert.Equal([false, true], roll.Dice.Select(d => d.Counted));
    }

    [Fact]
    public void TiedDiceDropTheLaterOne()
    {
        var roll = DiceExpression.Parse("3d6kh2").Roll(new SequenceRandom(4, 4, 4));

        Assert.Equal(8, roll.Total);
        Assert.Equal([true, true, false], roll.Dice.Select(d => d.Counted));
    }

    [Fact]
    public void SubtractedDiceReduceTheTotal()
    {
        var roll = DiceExpression.Parse("1d8-1d6").Roll(new SequenceRandom(7, 2));

        Assert.Equal(5, roll.Total);
    }

    [Fact]
    public void ConstantsConsumeNoRandomness()
    {
        var random = new SequenceRandom(4, 6);

        var roll = DiceExpression.Parse("2d6+100").Roll(random);

        Assert.Equal(2, random.Consumed);
        Assert.Equal(110, roll.Total);
    }

    [Fact]
    public void DroppedDiceStillConsumeRandomness()
    {
        var random = new SequenceRandom(1, 2, 3, 4);

        DiceExpression.Parse("4d6kh1").Roll(random);

        Assert.Equal(4, random.Consumed);
    }

    [Fact]
    public void RollFormatsForTheCombatLog()
    {
        Assert.Equal(
            "2d6+3: [4, 6] +3 = 13",
            DiceExpression.Parse("2d6+3").Roll(new SequenceRandom(4, 6)).ToString());

        Assert.Equal(
            "4d6kh3: [5, 3, (2), 6] = 14",
            DiceExpression.Parse("4d6kh3").Roll(new SequenceRandom(5, 3, 2, 6)).ToString());

        Assert.Equal(
            "5: +5 = 5",
            DiceExpression.Parse("5").Roll(new SequenceRandom(1)).ToString());
    }

    [Fact]
    public void EveryRollLandsInsideTheReportedRange()
    {
        var random = new PcgRandom(seed: 20260920);
        foreach (var text in new[] { "1d20", "2d6+3", "4d6kh3", "2d20kl1", "1d8+1d6-2" })
        {
            var expression = DiceExpression.Parse(text);
            for (var i = 0; i < 2000; i++)
            {
                var total = expression.Roll(random).Total;
                Assert.InRange(total, expression.Minimum, expression.Maximum);
            }
        }
    }

    [Fact]
    public void ObservedAverageTracksTheComputedOne()
    {
        var expression = DiceExpression.Parse("4d6kh3");
        var random = new PcgRandom(seed: 7);

        var total = 0L;
        const int rolls = 200_000;
        for (var i = 0; i < rolls; i++)
        {
            total += expression.Roll(random).Total;
        }

        Assert.Equal(expression.Average, (double)total / rolls, 1);
    }
}
