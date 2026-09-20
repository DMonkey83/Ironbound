using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Abilities;

/// <summary>A creature's six ability scores.</summary>
public sealed class AbilityScores
{
    /// <summary>The usual rolling method: four six-sided dice, drop the lowest.</summary>
    public static DiceExpression FourDiceDropLowest { get; } = DiceExpression.Parse("4d6kh3");

    private readonly AbilityScore[] _scores;

    public AbilityScores(
        int strength,
        int dexterity,
        int constitution,
        int intelligence,
        int wisdom,
        int charisma)
    {
        _scores =
        [
            new AbilityScore(Ability.Strength, strength),
            new AbilityScore(Ability.Dexterity, dexterity),
            new AbilityScore(Ability.Constitution, constitution),
            new AbilityScore(Ability.Intelligence, intelligence),
            new AbilityScore(Ability.Wisdom, wisdom),
            new AbilityScore(Ability.Charisma, charisma),
        ];
    }

    public AbilityScores(IEnumerable<AbilityScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        _scores = new AbilityScore[AbilityInfo.Count];
        foreach (var score in scores)
        {
            var index = (int)score.Ability;
            if (_scores[index] is not null)
            {
                throw new ArgumentException($"{AbilityInfo.Abbreviate(score.Ability)} given twice.", nameof(scores));
            }

            _scores[index] = score;
        }

        foreach (var ability in AbilityInfo.All)
        {
            if (_scores[(int)ability] is null)
            {
                throw new ArgumentException($"{AbilityInfo.Abbreviate(ability)} missing.", nameof(scores));
            }
        }
    }

    /// <summary>Every ability at the same value. Handy for test fixtures: All(10) is a blank slate.</summary>
    public static AbilityScores All(int value) => new(value, value, value, value, value, value);

    /// <summary>15, 14, 13, 12, 10, 8 in canonical order, for players who would rather not roll.</summary>
    public static AbilityScores StandardArray() => new(15, 14, 13, 12, 10, 8);

    /// <summary>
    /// Rolls each ability in canonical order, so a given seed always produces the same character.
    /// Assignment to abilities is the player's problem, not this method's.
    /// </summary>
    public static AbilityScores Roll(IRandomSource random, DiceExpression? method = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        var dice = method ?? FourDiceDropLowest;

        return new AbilityScores(AbilityInfo.All
            .Select(ability => new AbilityScore(ability, dice.Roll(random).Total))
            .ToArray());
    }

    public AbilityScore this[Ability ability] => _scores[(int)ability];

    public AbilityScore Strength => _scores[(int)Ability.Strength];

    public AbilityScore Dexterity => _scores[(int)Ability.Dexterity];

    public AbilityScore Constitution => _scores[(int)Ability.Constitution];

    public AbilityScore Intelligence => _scores[(int)Ability.Intelligence];

    public AbilityScore Wisdom => _scores[(int)Ability.Wisdom];

    public AbilityScore Charisma => _scores[(int)Ability.Charisma];

    /// <summary>Shorthand for <c>this[ability].Modifier</c>, which is most of what callers want.</summary>
    public int ModifierOf(Ability ability) => this[ability].Modifier;

    public override string ToString() => string.Join(", ", _scores.Select(s => s.ToString()));
}
