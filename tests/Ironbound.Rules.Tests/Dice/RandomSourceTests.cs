using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Tests.Dice;

public class PcgRandomTests
{
    [Fact]
    public void SameSeedProducesTheSameStream()
    {
        var first = new PcgRandom(seed: 42);
        var second = new PcgRandom(seed: 42);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(first.NextDie(20), second.NextDie(20));
        }
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var first = new PcgRandom(seed: 1);
        var second = new PcgRandom(seed: 2);

        var rolls = Enumerable.Range(0, 50).Select(_ => (first.NextDie(20), second.NextDie(20)));

        Assert.Contains(rolls, pair => pair.Item1 != pair.Item2);
    }

    [Fact]
    public void DifferentSequencesDiverge()
    {
        var combat = new PcgRandom(seed: 42, sequence: 1);
        var loot = new PcgRandom(seed: 42, sequence: 2);

        var rolls = Enumerable.Range(0, 50).Select(_ => (combat.NextDie(20), loot.NextDie(20)));

        Assert.Contains(rolls, pair => pair.Item1 != pair.Item2);
    }

    [Fact]
    public void RestoringAStateResumesTheIdenticalStream()
    {
        var random = new PcgRandom(seed: 99);
        for (var i = 0; i < 17; i++)
        {
            random.NextDie(6);
        }

        var save = random.Capture();
        var expected = Enumerable.Range(0, 100).Select(_ => random.NextDie(6)).ToArray();

        random.Restore(save);
        var replayed = Enumerable.Range(0, 100).Select(_ => random.NextDie(6)).ToArray();

        Assert.Equal(expected, replayed);
    }

    [Fact]
    public void AStateReloadedIntoAFreshGeneratorContinuesTheStream()
    {
        var random = new PcgRandom(seed: 5);
        random.NextDie(20);
        var save = random.Capture();

        var expected = Enumerable.Range(0, 50).Select(_ => random.NextDie(20)).ToArray();
        var reloaded = PcgRandom.FromState(save);

        Assert.Equal(expected, Enumerable.Range(0, 50).Select(_ => reloaded.NextDie(20)).ToArray());
    }

    [Fact]
    public void StaysInsideTheRequestedRange()
    {
        var random = new PcgRandom(seed: 123);

        for (var i = 0; i < 20_000; i++)
        {
            Assert.InRange(random.Next(-5, 5), -5, 4);
        }
    }

    [Fact]
    public void ASingleValueRangeAlwaysReturnsThatValue()
    {
        var random = new PcgRandom(seed: 1);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(7, random.Next(7, 8));
        }
    }

    [Fact]
    public void EmptyRangeThrows()
    {
        var random = new PcgRandom(seed: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 4));
    }

    [Fact]
    public void FacesComeUpEvenly()
    {
        var random = new PcgRandom(seed: 2026);
        var counts = new int[21];
        const int rolls = 210_000;

        for (var i = 0; i < rolls; i++)
        {
            counts[random.NextDie(20)]++;
        }

        Assert.Equal(0, counts[0]);
        for (var face = 1; face <= 20; face++)
        {
            // Expected 10,500 per face; 3% is far outside plausible drift but would catch
            // a modulo bias, which skews the low faces by roughly one part in a thousand.
            Assert.InRange(counts[face], 10_185, 10_815);
        }
    }
}

public class SequenceRandomTests
{
    [Fact]
    public void HandsOutValuesInOrder()
    {
        var random = new SequenceRandom(3, 1, 4);

        Assert.Equal(3, random.NextDie(6));
        Assert.Equal(1, random.NextDie(6));
        Assert.Equal(4, random.NextDie(6));
    }

    [Fact]
    public void RunningOutThrows()
    {
        var random = new SequenceRandom(3);
        random.NextDie(6);

        Assert.Throws<InvalidOperationException>(() => random.NextDie(6));
    }

    [Fact]
    public void AValueOutsideTheRequestedRangeThrows()
    {
        var random = new SequenceRandom(9);

        Assert.Throws<InvalidOperationException>(() => random.NextDie(6));
    }

    [Fact]
    public void AlwaysRepeatsForever()
    {
        var random = SequenceRandom.Always(20);

        for (var i = 0; i < 1000; i++)
        {
            Assert.Equal(20, random.NextDie(20));
        }
    }

    [Fact]
    public void CountsWhatItHandedOut()
    {
        var random = new SequenceRandom(1, 2, 3);
        random.NextDie(6);
        random.NextDie(6);

        Assert.Equal(2, random.Consumed);
    }

    [Fact]
    public void RequiresAtLeastOneValue()
    {
        Assert.Throws<ArgumentException>(() => new SequenceRandom());
    }
}
