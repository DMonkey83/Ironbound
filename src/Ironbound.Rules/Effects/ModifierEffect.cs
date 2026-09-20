using Ironbound.Rules.Abilities;
using Ironbound.Rules.Creatures;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Effects;

/// <summary>
/// The common case: an effect that grants modifiers and takes them back when it ends.
/// Bull's Strength, Haste, Bless, Shaken, ability damage — all of them are this.
/// </summary>
/// <remarks>
/// The targets are described rather than selected by a delegate, so the whole effect can be
/// written to a save file and rebuilt. That was the price of being able to save mid-fight with
/// buffs still running, and it is cheaper than it looks.
/// </remarks>
public sealed class ModifierEffect(string name, Duration duration) : Effect(name, duration)
{
    private readonly List<ModifierGrant> _grants = [];

    /// <summary>What it hands out, and where. Written straight into a save.</summary>
    public IReadOnlyList<ModifierGrant> GrantedModifiers => _grants;

    public ModifierEffect Grants(int value, BonusType type, ModifierTarget target)
    {
        _grants.Add(new ModifierGrant(target, value, type));
        return this;
    }

    /// <summary>Buff or damage an ability score. A negative value is ability damage.</summary>
    public ModifierEffect GrantsToAbility(Ability ability, int value, BonusType type) =>
        Grants(value, type, ModifierTarget.Ability(ability));

    public ModifierEffect GrantsToArmorClass(int value, BonusType type) =>
        Grants(value, type, ModifierTarget.ArmorClass);

    public ModifierEffect GrantsToAttack(int value, BonusType type) =>
        Grants(value, type, ModifierTarget.Attack);

    public ModifierEffect GrantsToDamage(int value, BonusType type) =>
        Grants(value, type, ModifierTarget.Damage);

    public ModifierEffect GrantsToSpeed(int value, BonusType type) =>
        Grants(value, type, ModifierTarget.Speed);

    public ModifierEffect GrantsToSave(Saves.Save save, int value, BonusType type) =>
        Grants(value, type, ModifierTarget.Save(save));

    protected override void OnApply(Creature target)
    {
        foreach (var grant in _grants)
        {
            Grant(grant.Target.On(target), grant.Value, grant.Type);
        }
    }

    /// <summary>
    /// After a load the modifiers are already in their stacks, restored with everything else.
    /// Re-granting would double them, so the effect only needs to remember where they went.
    /// </summary>
    protected override void OnReattach(Creature target)
    {
        foreach (var grant in _grants)
        {
            Track(grant.Target.On(target));
        }
    }
}
