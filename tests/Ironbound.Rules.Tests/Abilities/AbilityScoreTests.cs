using Ironbound.Rules.Abilities;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Tests.Abilities;

public class AbilityScoreTests
{
    [Theory]
    [InlineData(0, -5)]
    [InlineData(1, -5)]
    [InlineData(2, -4)]
    [InlineData(3, -4)]
    [InlineData(4, -3)]
    [InlineData(5, -3)]
    [InlineData(6, -2)]
    [InlineData(7, -2)]
    [InlineData(8, -1)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(13, 1)]
    [InlineData(18, 4)]
    [InlineData(19, 4)]
    [InlineData(20, 5)]
    [InlineData(21, 5)]
    [InlineData(30, 10)]
    public void ModifierFollowsTheTable(int score, int modifier)
    {
        Assert.Equal(modifier, AbilityScore.ModifierFor(score));
        Assert.Equal(modifier, new AbilityScore(Ability.Strength, score).Modifier);
    }

    [Fact]
    public void OddScoresBelowTenRoundDownNotTowardZero()
    {
        // The trap: integer division truncates, so (7 - 10) / 2 is -1. The answer is -2.
        Assert.Equal(-2, AbilityScore.ModifierFor(7));
        Assert.Equal(-1, AbilityScore.ModifierFor(9));
        Assert.Equal(-5, AbilityScore.ModifierFor(1));
    }

    [Fact]
    public void ScoreStartsAtItsBase()
    {
        var strength = new AbilityScore(Ability.Strength, 16);

        Assert.Equal(16, strength.Score);
        Assert.Equal(3, strength.Modifier);
    }

    [Fact]
    public void EnhancementBonusRaisesTheScore()
    {
        var strength = new AbilityScore(Ability.Strength, 16);

        strength.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");

        Assert.Equal(20, strength.Score);
        Assert.Equal(5, strength.Modifier);
    }

    [Fact]
    public void TwoEnhancementBonusesDoNotStack()
    {
        var strength = new AbilityScore(Ability.Strength, 16);

        strength.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");
        strength.Modifiers.Add(2, BonusType.Enhancement, "Belt of Giant Strength");

        Assert.Equal(20, strength.Score);
    }

    [Fact]
    public void AbilityDamageStacksBecauseItIsAPenalty()
    {
        var strength = new AbilityScore(Ability.Strength, 16);

        strength.Modifiers.Add(-2, BonusType.Untyped, "Shadow Touch");
        strength.Modifiers.Add(-3, BonusType.Untyped, "Spider Venom");

        Assert.Equal(11, strength.Score);
        Assert.Equal(0, strength.Modifier);
    }

    [Fact]
    public void ABuffAndDamageResolveTogether()
    {
        var strength = new AbilityScore(Ability.Strength, 16);
        strength.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");
        strength.Modifiers.Add(-6, BonusType.Untyped, "Spider Venom");

        Assert.Equal(14, strength.Score);
        Assert.Equal(2, strength.Modifier);
    }

    [Fact]
    public void RemovingABuffRestoresTheScore()
    {
        var strength = new AbilityScore(Ability.Strength, 16);
        strength.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");
        Assert.Equal(20, strength.Score);

        strength.Modifiers.RemoveAllFrom("Bull's Strength");

        Assert.Equal(16, strength.Score);
    }

    [Fact]
    public void DrainMovesTheBaseScore()
    {
        var constitution = new AbilityScore(Ability.Constitution, 14);

        constitution.Base -= 2;

        Assert.Equal(12, constitution.Score);
        Assert.Equal(1, constitution.Modifier);
    }

    [Fact]
    public void ScoreCannotFallBelowZero()
    {
        var strength = new AbilityScore(Ability.Strength, 8);

        strength.Modifiers.Add(-20, BonusType.Untyped, "Ray of Enfeeblement");

        Assert.Equal(0, strength.Score);
        Assert.Equal(-5, strength.Modifier);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void BaseScoreOutsideTheAllowedRangeThrows(int score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityScore(Ability.Strength, score));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbilityScore(Ability.Strength, 10).Base = score);
    }

    [Fact]
    public void ANonAbilityHasNoScoreAndAZeroModifier()
    {
        var constitution = AbilityScore.NonAbility(Ability.Constitution);

        Assert.False(constitution.HasScore);
        Assert.Null(constitution.Score);

        // Not -5: a skeleton has no Constitution at all, rather than a Constitution of 0.
        Assert.Equal(0, constitution.Modifier);
    }

    [Fact]
    public void ANonAbilityRejectsChangesToItsBase()
    {
        var constitution = AbilityScore.NonAbility(Ability.Constitution);

        Assert.Throws<InvalidOperationException>(() => constitution.Base = 10);
    }

    [Fact]
    public void ANonAbilityIgnoresModifiers()
    {
        var constitution = AbilityScore.NonAbility(Ability.Constitution);

        constitution.Modifiers.Add(4, BonusType.Enhancement, "Bear's Endurance");

        Assert.Null(constitution.Score);
        Assert.Equal(0, constitution.Modifier);
    }

    [Fact]
    public void ExplainShowsWhichModifiersCounted()
    {
        var strength = new AbilityScore(Ability.Strength, 16);
        strength.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");
        strength.Modifiers.Add(2, BonusType.Enhancement, "Belt of Giant Strength");

        var breakdown = strength.Explain();

        Assert.Equal(4, breakdown.Total);
        var lost = Assert.Single(breakdown.Suppressed);
        Assert.Equal("Belt of Giant Strength", lost.Modifier.Source);
    }

    [Fact]
    public void FormatsForTheCharacterSheet()
    {
        Assert.Equal("Str 16 (+3)", new AbilityScore(Ability.Strength, 16).ToString());
        Assert.Equal("Dex 9 (-1)", new AbilityScore(Ability.Dexterity, 9).ToString());
        Assert.Equal("Con — (+0)", AbilityScore.NonAbility(Ability.Constitution).ToString());

        var buffed = new AbilityScore(Ability.Strength, 16);
        buffed.Modifiers.Add(4, BonusType.Enhancement, "Bull's Strength");
        Assert.Equal("Str 20 (+5) [16 base, +4 enhancement (Bull's Strength) = 4]", buffed.ToString());
    }
}
