using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Effects;

/// <summary>
/// The common case: an effect that grants modifiers and takes them back when it ends.
/// Bull's Strength, Haste, Bless, Shaken, ability damage — all of them are this.
/// </summary>
/// <remarks>
/// The stack is chosen by a delegate, which is fine while effects are defined in code. When
/// effects become content data the selector becomes a path ("ability.strength") and this class
/// gains a data-driven sibling; the stacking behaviour and cleanup do not change.
/// </remarks>
public sealed class ModifierEffect(string name, Duration duration) : Effect(name, duration)
{
    private readonly List<(Func<Creature, ModifierStack> Select, int Value, BonusType Type)> _grants = [];

    public ModifierEffect Grants(int value, BonusType type, Func<Creature, ModifierStack> stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _grants.Add((stack, value, type));
        return this;
    }

    /// <summary>Buff or damage an ability score. A negative value is ability damage.</summary>
    public ModifierEffect GrantsToAbility(Ability ability, int value, BonusType type) =>
        Grants(value, type, creature => creature.Abilities[ability].Modifiers);

    public ModifierEffect GrantsToArmorClass(int value, BonusType type) =>
        Grants(value, type, creature => creature.ArmorClass.Modifiers);

    protected override void OnApply(Creature target)
    {
        foreach (var (select, value, type) in _grants)
        {
            Grant(select(target), value, type);
        }
    }
}
