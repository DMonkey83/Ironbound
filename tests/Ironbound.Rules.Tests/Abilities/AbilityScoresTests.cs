using Ironbound.Rules.Abilities;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Tests.Abilities;

public class AbilityScoresTests
{
    [Fact]
    public void ConstructorAssignsInCanonicalOrder()
    {
        var scores = new AbilityScores(16, 14, 13, 12, 10, 8);

        Assert.Equal(16, scores.Strength.Score);
        Assert.Equal(14, scores.Dexterity.Score);
        Assert.Equal(13, scores.Constitution.Score);
        Assert.Equal(12, scores.Intelligence.Score);
        Assert.Equal(10, scores.Wisdom.Score);
        Assert.Equal(8, scores.Charisma.Score);
    }

    [Fact]
    public void IndexerAndPropertiesReachTheSameObject()
    {
        var scores = new AbilityScores(16, 14, 13, 12, 10, 8);

        Assert.Same(scores.Strength, scores[Ability.Strength]);
        Assert.Same(scores.Charisma, scores[Ability.Charisma]);
    }

    [Fact]
    public void ModifierOfIsShorthandForTheScoreModifier()
    {
        var scores = new AbilityScores(16, 14, 13, 12, 10, 8);

        Assert.Equal(3, scores.ModifierOf(Ability.Strength));
        Assert.Equal(-1, scores.ModifierOf(Ability.Charisma));
    }

    [Fact]
    public void AllSetsEveryAbilityTheSame()
    {
        var scores = AbilityScores.All(10);

        Assert.All(AbilityInfo.All, ability => Assert.Equal(0, scores.ModifierOf(ability)));
    }

    [Fact]
    public void StandardArrayIsFifteenDownToEight()
    {
        var scores = AbilityScores.StandardArray();

        Assert.Equal([15, 14, 13, 12, 10, 8], AbilityInfo.All.Select(a => scores[a].Score));
    }

    [Fact]
    public void RollUsesFourDiceDropLowestInCanonicalOrder()
    {
        var random = new SequenceRandom(
            6, 6, 6, 1,
            5, 5, 5, 1,
            4, 4, 4, 1,
            3, 3, 3, 1,
            2, 2, 2, 1,
            1, 1, 1, 1);

        var scores = AbilityScores.Roll(random);

        Assert.Equal([18, 15, 12, 9, 6, 3], AbilityInfo.All.Select(a => scores[a].Score));
        Assert.Equal(24, random.Consumed);
    }

    [Fact]
    public void RollAcceptsADifferentMethod()
    {
        var random = SequenceRandom.Always(4);

        var scores = AbilityScores.Roll(random, DiceExpression.Parse("3d6"));

        Assert.All(AbilityInfo.All, ability => Assert.Equal(12, scores[ability].Score));
        Assert.Equal(18, random.Consumed);
    }

    [Fact]
    public void RollIsReproducibleFromASeed()
    {
        var first = AbilityScores.Roll(new PcgRandom(seed: 1234));
        var second = AbilityScores.Roll(new PcgRandom(seed: 1234));

        Assert.Equal(
            AbilityInfo.All.Select(a => first[a].Score),
            AbilityInfo.All.Select(a => second[a].Score));
    }

    [Fact]
    public void RolledScoresAreWithinTheMethodRange()
    {
        var random = new PcgRandom(seed: 99);

        for (var i = 0; i < 500; i++)
        {
            var scores = AbilityScores.Roll(random);
            Assert.All(AbilityInfo.All, ability => Assert.InRange(scores[ability].Score!.Value, 3, 18));
        }
    }

    [Fact]
    public void CanBeBuiltFromIndividualScoresIncludingANonAbility()
    {
        var scores = new AbilityScores(
        [
            new AbilityScore(Ability.Strength, 14),
            new AbilityScore(Ability.Dexterity, 14),
            AbilityScore.NonAbility(Ability.Constitution),
            new AbilityScore(Ability.Intelligence, 6),
            new AbilityScore(Ability.Wisdom, 10),
            new AbilityScore(Ability.Charisma, 5),
        ]);

        Assert.Null(scores.Constitution.Score);
        Assert.Equal(0, scores.ModifierOf(Ability.Constitution));
        Assert.Equal(2, scores.ModifierOf(Ability.Strength));
    }

    [Fact]
    public void BuildingFromAnIncompleteSetThrows()
    {
        Assert.Throws<ArgumentException>(() => new AbilityScores(
        [
            new AbilityScore(Ability.Strength, 14),
            new AbilityScore(Ability.Dexterity, 14),
        ]));
    }

    [Fact]
    public void BuildingWithADuplicateAbilityThrows()
    {
        Assert.Throws<ArgumentException>(() => new AbilityScores(
        [
            new AbilityScore(Ability.Strength, 14),
            new AbilityScore(Ability.Strength, 12),
            new AbilityScore(Ability.Dexterity, 14),
            new AbilityScore(Ability.Constitution, 12),
            new AbilityScore(Ability.Intelligence, 6),
            new AbilityScore(Ability.Wisdom, 10),
            new AbilityScore(Ability.Charisma, 5),
        ]));
    }

    [Fact]
    public void FormatsForTheCharacterSheet()
    {
        Assert.Equal(
            "Str 16 (+3), Dex 14 (+2), Con 13 (+1), Int 12 (+1), Wis 10 (+0), Cha 8 (-1)",
            new AbilityScores(16, 14, 13, 12, 10, 8).ToString());
    }
}
