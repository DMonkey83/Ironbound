using Ironbound.Rules.Abilities;
using Ironbound.Rules.Defense;
using Ironbound.Rules.Modifiers;

namespace Ironbound.Rules.Combat;

/// <summary>How much of an ability modifier reaches damage. Weapon-dependent, not creature-dependent.</summary>
public enum AbilityDamageScale
{
    /// <summary>A crossbow, or anything that does not care how strong you are.</summary>
    None,

    /// <summary>An off-hand weapon.</summary>
    Half,

    /// <summary>One hand on the hilt.</summary>
    Full,

    /// <summary>Both hands on the hilt.</summary>
    OneAndAHalf,
}

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

    /// <summary>The weapon's own contribution to damage: enhancement, bane, a flaming brand's flat part.</summary>
    public ModifierStack DamageModifiers { get; } = new();

    /// <summary>
    /// What the weapon is made of and aligned with, for getting past damage reduction. The
    /// physical damage types it deals are added automatically when damage is mitigated, so only
    /// the extras — silver, cold iron, magic, an alignment — belong here.
    /// </summary>
    public DamageBypass Qualities { get; set; } = DamageBypass.None;

    /// <summary>
    /// Which ability feeds the attack roll — Dexterity for a finessed blade, Strength otherwise.
    /// Null means the weapon's own modifiers are the whole story, which keeps hand-built attacks
    /// working exactly as before.
    /// </summary>
    public Ability? AttackAbility { get; set; }

    /// <summary>Which ability feeds damage. Usually Strength even when the attack is finessed.</summary>
    public Ability? DamageAbility { get; set; }

    public AbilityDamageScale DamageScale { get; set; } = AbilityDamageScale.Full;

    /// <summary>Penalty per range increment past the first.</summary>
    public const int RangePenaltyPerIncrement = -2;

    /// <summary>How far this reaches before accuracy starts to suffer. Zero means melee.</summary>
    public int RangeIncrement { get; set; }

    /// <summary>
    /// How many increments the weapon can manage at all: ten for something you shoot, five for
    /// something you throw. Beyond it the attack is not made at a penalty — it cannot be made.
    /// </summary>
    public int MaximumIncrements { get; set; } = ProjectileIncrements;

    public const int ProjectileIncrements = 10;

    public const int ThrownIncrements = 5;

    public bool IsRanged => RangeIncrement > 0;

    /// <summary>The furthest this weapon can reach, or null for a melee weapon.</summary>
    public int? MaximumRange => IsRanged ? RangeIncrement * MaximumIncrements : null;

    /// <summary>
    /// Which increment a shot of <paramref name="feet"/> falls in, counting from one. Anything
    /// inside the first increment is still the first.
    /// </summary>
    public int IncrementsAt(int feet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(feet);

        return !IsRanged || feet <= RangeIncrement
            ? 1
            : ((feet + RangeIncrement - 1) / RangeIncrement);
    }

    public bool IsWithinRange(int feet) =>
        !IsRanged || IncrementsAt(feet) <= MaximumIncrements;

    /// <summary>The accuracy cost of distance: nothing up close, then -2 an increment.</summary>
    public int RangePenalty(int feet) =>
        IsRanged ? (IncrementsAt(feet) - 1) * RangePenaltyPerIncrement : 0;

    /// <summary>
    /// A melee weapon that draws on the wielder rather than carrying pre-computed numbers.
    /// The damage is dice only — "1d8", not "1d8+6" — because the wielder supplies the rest.
    /// </summary>
    public static WeaponAttack Melee(
        string name,
        string damageDice,
        DamageType type,
        CriticalProfile? critical = null,
        AbilityDamageScale scale = AbilityDamageScale.Full,
        Ability attackAbility = Ability.Strength,
        Ability damageAbility = Ability.Strength)
    {
        var attack = new Attack();
        if (critical is { } profile)
        {
            attack.Critical = profile;
        }

        return new WeaponAttack(name, attack, DamagePacket.Weapon(damageDice, type))
        {
            AttackAbility = attackAbility,
            DamageAbility = damageAbility,
            DamageScale = scale,
        };
    }

    /// <summary>
    /// A bow, a crossbow, a thrown axe. Dexterity aims it, and by default strength does not
    /// push it any harder — a longbow that cares how strong you are is a composite one, which
    /// says so by setting a damage scale.
    /// </summary>
    public static WeaponAttack Ranged(
        string name,
        string damageDice,
        DamageType type,
        int rangeIncrement,
        CriticalProfile? critical = null,
        int maximumIncrements = ProjectileIncrements,
        AbilityDamageScale scale = AbilityDamageScale.None,
        Ability attackAbility = Ability.Dexterity,
        Ability damageAbility = Ability.Strength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rangeIncrement, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumIncrements, 1);

        var attack = new Attack();
        if (critical is { } profile)
        {
            attack.Critical = profile;
        }

        return new WeaponAttack(name, attack, DamagePacket.Weapon(damageDice, type))
        {
            AttackAbility = attackAbility,
            DamageAbility = damageAbility,
            DamageScale = scale,
            RangeIncrement = rangeIncrement,
            MaximumIncrements = maximumIncrements,
        };
    }

    /// <summary>The ability modifier this weapon turns into damage, floored.</summary>
    public int ScaleDamage(int abilityModifier) => DamageScale switch
    {
        AbilityDamageScale.None => 0,
        AbilityDamageScale.Half => abilityModifier >> 1,
        AbilityDamageScale.OneAndAHalf => (abilityModifier * 3) >> 1,
        _ => abilityModifier,
    };

    public override string ToString() =>
        $"{Name} {Attack.Modifiers.Total:+0;-0;+0} ({Damage}, {Attack.Critical}"
        + (IsRanged ? $", {RangeIncrement} ft" : string.Empty) + ")";
}
