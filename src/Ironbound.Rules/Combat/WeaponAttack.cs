using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>
/// One way a creature can hit something: an <see cref="Combat.Attack"/> to see whether it lands
/// and a <see cref="DamagePacket"/> for what happens when it does. A longsword, a bite, a
/// scorching ray — the rules do not distinguish.
/// </summary>
public sealed class WeaponAttack
{
    public WeaponAttack(string name, Attack attack, DamagePacket damage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(attack);
        ArgumentNullException.ThrowIfNull(damage);

        Name = name;
        Attack = attack;
        Damage = damage;
    }

    /// <summary>
    /// The common case: a flat attack bonus and one damage expression. Anything richer — a
    /// flaming enchantment, a second bonus type — is built by hand from the parts.
    /// </summary>
    public static WeaponAttack Create(
        string name,
        int attackBonus,
        string damage,
        DamageType type,
        CriticalProfile? critical = null)
    {
        var attack = new Attack();
        if (attackBonus != 0)
        {
            attack.Modifiers.Add(attackBonus, BonusType.Untyped, name);
        }

        if (critical is { } profile)
        {
            attack.Critical = profile;
        }

        return new WeaponAttack(name, attack, DamagePacket.Weapon(damage, type));
    }

    public string Name { get; }

    public Attack Attack { get; }

    public DamagePacket Damage { get; }

    public override string ToString() =>
        $"{Name} {Attack.Modifiers.Total:+0;-0;+0} ({Damage}, {Attack.Critical})";
}
