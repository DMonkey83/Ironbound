using Ironbound.Rules.Dice;

namespace Ironbound.Rules.Combat;

/// <summary>
/// One strand of an attack's damage: an amount, a type, and whether a critical hit rolls it
/// again. A flaming longsword is two components — 1d8+4 slashing that multiplies, and
/// 1d6 fire that does not.
/// </summary>
public readonly record struct DamageComponent
{
    public DamageComponent(DiceExpression amount, DamageType type, bool multipliedOnCritical)
    {
        ArgumentNullException.ThrowIfNull(amount);
        Amount = amount;
        Type = type;
        MultipliedOnCritical = multipliedOnCritical;
    }

    public DiceExpression Amount { get; }

    public DamageType Type { get; }

    /// <summary>
    /// Whether a confirmed critical rolls this component once per multiple. False for the
    /// things the rules exclude: weapon special abilities such as flaming, and precision
    /// damage such as sneak attack.
    /// </summary>
    public bool MultipliedOnCritical { get; }

    /// <summary>
    /// Whether it hurts without killing, as a sap or a whip does: taken as nonlethal damage
    /// rather than off the hit points.
    /// </summary>
    /// <remarks>
    /// On the component because that is where the weapon's damage is written down, and so a save
    /// keeps it; <see cref="Strike"/> treats a blow carrying any of it as a nonlethal blow, sneak
    /// attack and all, which is how the book treats sneak attack with a sap.
    /// </remarks>
    public bool Nonlethal { get; init; }

    /// <summary>The weapon's own damage. Multiplied on a critical.</summary>
    public static DamageComponent Weapon(DiceExpression amount, DamageType type) =>
        new(amount, type, multipliedOnCritical: true);

    /// <inheritdoc cref="Weapon(DiceExpression, DamageType)"/>
    public static DamageComponent Weapon(string amount, DamageType type) =>
        Weapon(DiceExpression.Parse(amount), type);

    /// <summary>Extra damage that a critical does not multiply: flaming, sneak attack, bane.</summary>
    public static DamageComponent Extra(DiceExpression amount, DamageType type) =>
        new(amount, type, multipliedOnCritical: false);

    /// <inheritdoc cref="Extra(DiceExpression, DamageType)"/>
    public static DamageComponent Extra(string amount, DamageType type) =>
        Extra(DiceExpression.Parse(amount), type);

    public override string ToString() =>
        Nonlethal ? $"{Amount} {DamageTypes.Name(Type)} (nonlethal)" : $"{Amount} {DamageTypes.Name(Type)}";
}
