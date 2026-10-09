using Ironbound.Rules.Abilities;
using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Saves;

/// <summary>A creature's three saving throws, wired to the abilities that govern them.</summary>
public sealed class SavingThrows
{
    private readonly SavingThrow[] _saves;

    public SavingThrows(AbilityScores abilities, int fortitude = 0, int reflex = 0, int will = 0)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        _saves =
        [
            new SavingThrow(Save.Fortitude, abilities[Ability.Constitution], fortitude),
            new SavingThrow(Save.Reflex, abilities[Ability.Dexterity], reflex),
            new SavingThrow(Save.Will, abilities[Ability.Wisdom], will),
        ];
    }

    public SavingThrow this[Save save] => _saves[(int)save];

    public SavingThrow Fortitude => _saves[(int)Save.Fortitude];

    public SavingThrow Reflex => _saves[(int)Save.Reflex];

    public SavingThrow Will => _saves[(int)Save.Will];

    /// <summary>Shorthand for <c>this[save].Roll(...)</c>, which is what callers usually want.</summary>
    public SavingThrowResult Attempt(
        Save save,
        int difficultyClass,
        IRandomSource random,
        RuleOptions? rules = null,
        IEnumerable<Modifiers.Modifier>? situational = null) =>
        this[save].Roll(difficultyClass, random, rules, situational);

    public override string ToString() => string.Join(", ", _saves.Select(s => s.ToString()));
}
