using Ironbound.Rules.Abilities;
using Ironbound.Rules.Dice;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Saves;

/// <summary>
/// One saving throw: a base from class progression, the governing ability read live, and a
/// <see cref="ModifierStack"/> for everything else — a cloak of resistance, a bless, a curse.
/// </summary>
/// <remarks>
/// The ability is held by reference rather than copied, so Bear's Endurance raises Fortitude the
/// moment it lands, exactly as it does for hit points and armour class.
/// </remarks>
public sealed class SavingThrow
{
    public const int DieSides = 20;

    private int _base;

    public SavingThrow(Save save, AbilityScore ability, int baseSave = 0)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentOutOfRangeException.ThrowIfNegative(baseSave);

        Save = save;
        Ability = ability;
        _base = baseSave;
    }

    public Save Save { get; }

    /// <summary>Constitution, Dexterity or Wisdom, depending on the save.</summary>
    public AbilityScore Ability { get; }

    /// <summary>From class levels. Moves on level-up, not in combat.</summary>
    public int Base
    {
        get => _base;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _base = value;
        }
    }

    public ModifierStack Modifiers { get; } = new();

    public int Total => _base + Ability.Modifier + Modifiers.Total;

    /// <summary>The whole number accounted for, base and ability included.</summary>
    /// <param name="situational">Bonuses that hold against this one thing only — bravery
    /// against fear, superstition against magic — and so live with the roll, not the stack.</param>
    public ModifierBreakdown Explain(IEnumerable<Modifier>? situational = null)
    {
        var stack = new ModifierStack();
        if (_base != 0)
        {
            stack.Add(Modifier.Untyped(_base, "Base Save"));
        }

        if (Ability.Modifier != 0)
        {
            stack.Add(Modifier.Untyped(Ability.Modifier, AbilityInfo.Abbreviate(Ability.Ability)));
        }

        foreach (var modifier in Modifiers.Modifiers)
        {
            stack.Add(modifier);
        }

        foreach (var modifier in situational ?? [])
        {
            stack.Add(modifier);
        }

        return stack.Explain();
    }

    /// <param name="difficultyClass">Set by whatever is being resisted. Meeting it is enough.</param>
    public SavingThrowResult Roll(
        int difficultyClass,
        IRandomSource random,
        RuleOptions? rules = null,
        IEnumerable<Modifier>? situational = null)
    {
        ArgumentNullException.ThrowIfNull(random);
        rules ??= RuleOptions.Pathfinder;

        var bonus = Explain(situational);
        var natural = random.NextDie(DieSides);
        var total = natural + bonus.Total;

        var automatic = (natural == DieSides && rules.NaturalTwentyAlwaysSaves)
            || (natural == 1 && rules.NaturalOneAlwaysFailsSaves);

        var succeeded = natural switch
        {
            DieSides when rules.NaturalTwentyAlwaysSaves => true,
            1 when rules.NaturalOneAlwaysFailsSaves => false,
            _ => total >= difficultyClass,
        };

        return new SavingThrowResult
        {
            Save = Save,
            NaturalRoll = natural,
            Bonus = bonus,
            Total = total,
            DifficultyClass = difficultyClass,
            Succeeded = succeeded,
            DecidedByNaturalRoll = automatic,
        };
    }

    public override string ToString() => $"{SaveInfo.Abbreviate(Save)} {Total:+0;-0;+0}";
}
