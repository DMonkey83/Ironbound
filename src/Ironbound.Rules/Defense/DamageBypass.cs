using Ironbound.Rules.Combat;

namespace Ironbound.Rules.Defense;

/// <summary>
/// The qualities an attack can carry that let it slip past damage reduction: what the weapon is
/// made of, what it is aligned with, and how it hurts. A creature's DR names the qualities it
/// cannot stop, so the two sides speak the same vocabulary.
/// </summary>
[Flags]
public enum DamageBypass
{
    None = 0,

    Bludgeoning = 1 << 0,
    Piercing = 1 << 1,
    Slashing = 1 << 2,

    Magic = 1 << 3,
    Silver = 1 << 4,
    ColdIron = 1 << 5,
    Adamantine = 1 << 6,

    Good = 1 << 7,
    Evil = 1 << 8,
    Lawful = 1 << 9,
    Chaotic = 1 << 10,

    Epic = 1 << 11,
}

/// <summary>Whether damage reduction needs every named quality, or only one of them.</summary>
public enum BypassMode
{
    /// <summary>"DR 10/silver or good" — any one quality is enough.</summary>
    Any,

    /// <summary>"DR 10/good and silver" — the attack needs all of them.</summary>
    All,
}

public static class DamageBypasses
{
    /// <summary>The bypass quality a physical damage type carries by itself.</summary>
    public static DamageBypass Of(DamageType type) => type switch
    {
        DamageType.Bludgeoning => DamageBypass.Bludgeoning,
        DamageType.Piercing => DamageBypass.Piercing,
        DamageType.Slashing => DamageBypass.Slashing,
        _ => DamageBypass.None,
    };

    public static string Name(DamageBypass flag) => flag switch
    {
        DamageBypass.ColdIron => "cold iron",
        _ => flag.ToString().ToLowerInvariant(),
    };

    /// <summary>"silver", "good and silver", "—" for reduction nothing bypasses.</summary>
    public static string Describe(DamageBypass flags, BypassMode mode)
    {
        if (flags == DamageBypass.None)
        {
            return "—";
        }

        var names = Enum.GetValues<DamageBypass>()
            .Where(flag => flag != DamageBypass.None && flags.HasFlag(flag))
            .Select(Name);

        return string.Join(mode == BypassMode.All ? " and " : " or ", names);
    }
}
