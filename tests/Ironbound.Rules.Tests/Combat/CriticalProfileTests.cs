using Ironbound.Rules.Combat;

namespace Ironbound.Rules.Tests.Combat;

public class CriticalProfileTests
{
    [Fact]
    public void StandardIsTwentyForDoubleDamage()
    {
        Assert.Equal(20, CriticalProfile.Standard.ThreatsOn);
        Assert.Equal(2, CriticalProfile.Standard.Multiplier);
    }

    [Theory]
    [InlineData(20, 19, false)]
    [InlineData(20, 20, true)]
    [InlineData(19, 18, false)]
    [InlineData(19, 19, true)]
    [InlineData(19, 20, true)]
    [InlineData(18, 17, false)]
    [InlineData(18, 18, true)]
    [InlineData(18, 1, false)]
    public void ThreatensAtAndAboveItsRange(int threatsOn, int natural, bool threatens)
    {
        Assert.Equal(threatens, new CriticalProfile(threatsOn, 2).Threatens(natural));
    }

    [Theory]
    [InlineData(20, 1)]
    [InlineData(19, 2)]
    [InlineData(18, 3)]
    public void ReportsHowManyRollsThreaten(int threatsOn, int width)
    {
        Assert.Equal(width, new CriticalProfile(threatsOn, 2).ThreatRangeWidth);
    }

    [Theory]
    [InlineData(20, 19)]
    [InlineData(19, 17)]
    [InlineData(18, 15)]
    public void WideningDoublesTheThreatRange(int threatsOn, int widened)
    {
        var profile = new CriticalProfile(threatsOn, 3).Widened();

        Assert.Equal(widened, profile.ThreatsOn);
        Assert.Equal(3, profile.Multiplier);
    }

    [Theory]
    [InlineData(20, 2, "20/x2")]
    [InlineData(19, 2, "19-20/x2")]
    [InlineData(18, 4, "18-20/x4")]
    public void FormatsLikeAWeaponEntry(int threatsOn, int multiplier, string text)
    {
        Assert.Equal(text, new CriticalProfile(threatsOn, multiplier).ToString());
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(21, 2)]
    [InlineData(20, 1)]
    [InlineData(20, 11)]
    public void RejectsImpossibleProfiles(int threatsOn, int multiplier)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CriticalProfile(threatsOn, multiplier));
    }
}
