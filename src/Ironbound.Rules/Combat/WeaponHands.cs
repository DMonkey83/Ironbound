namespace Ironbound.Rules.Combat;

/// <summary>
/// How a weapon is held, as the equipment tables sort them: light, one-handed, two-handed or
/// ranged.
/// </summary>
/// <remarks>
/// What used to be guessed from a damage scale written beside each weapon. The tables already
/// say it, and it decides three things at once: half again Strength on damage for two hands,
/// half again Power Attack's damage likewise, and whether the shield on the other arm is doing
/// anything at all.
/// </remarks>
public enum WeaponHands
{
    /// <summary>A dagger, a short sword: one hand, and Weapon Finesse can aim it.</summary>
    Light,

    OneHanded,

    /// <summary>Both hands on the haft, and nothing left over for a shield.</summary>
    TwoHanded,

    /// <summary>Shot or thrown. Bows, crossbows, slings, javelins, guns.</summary>
    Ranged,
}

public static class WeaponHandsInfo
{
    /// <summary>"one-handed", as the tables and the weapon files write it.</summary>
    public static string Name(WeaponHands hands) => hands switch
    {
        WeaponHands.Light => "light",
        WeaponHands.OneHanded => "one-handed",
        WeaponHands.TwoHanded => "two-handed",
        _ => "ranged",
    };

    /// <summary>"one-handed" or "OneHanded", or null for anything else.</summary>
    public static WeaponHands? Parse(string? written) =>
        Enum.TryParse<WeaponHands>(written?.Replace("-", string.Empty, StringComparison.Ordinal), true, out var hands)
            ? hands
            : null;
}
