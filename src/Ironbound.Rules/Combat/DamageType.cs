namespace Ironbound.Rules.Combat;

/// <summary>
/// What a packet of damage is made of. The split matters downstream: damage reduction
/// applies to physical damage, energy resistance to energy damage, and neither touches force.
/// </summary>
public enum DamageType
{
    Bludgeoning = 0,
    Piercing,
    Slashing,

    Acid,
    Cold,
    Electricity,
    Fire,
    Sonic,

    Force,
    Negative,
    Positive,
    Untyped,
}

public static class DamageTypes
{
    /// <summary>Weapon damage — what damage reduction is written against.</summary>
    public static bool IsPhysical(DamageType type) =>
        type is DamageType.Bludgeoning or DamageType.Piercing or DamageType.Slashing;

    /// <summary>The five energy types creatures resist by name.</summary>
    public static bool IsEnergy(DamageType type) =>
        type is DamageType.Acid or DamageType.Cold or DamageType.Electricity
            or DamageType.Fire or DamageType.Sonic;

    public static string Name(DamageType type) => type.ToString().ToLowerInvariant();
}
