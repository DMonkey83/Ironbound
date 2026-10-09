namespace Ironbound.Rules.Combat;

/// <summary>
/// The words in the Special column of the weapon tables.
/// </summary>
/// <remarks>
/// Flags, because a guisarme is both reach and trip and a whip is four of them at once. Only some
/// of them do anything yet — see <see cref="WeaponSpecials.IsModelled"/> — and the rest are kept
/// so the catalogue says what the book says and a later rule has something to read.
/// </remarks>
[Flags]
public enum WeaponSpecial
{
    None = 0,

    /// <summary>Threatens twice as far, and not at all up close.</summary>
    Reach = 1 << 0,

    /// <summary>Trips with it; fail badly and you let go of the weapon rather than fall.</summary>
    Trip = 1 << 1,

    Disarm = 1 << 2,

    Sunder = 1 << 3,

    /// <summary>Set against a charge for double damage. There is no charging yet.</summary>
    Brace = 1 << 4,

    /// <summary>Two heads. Only the first is used until two-weapon fighting exists.</summary>
    Double = 1 << 5,

    /// <summary>A monk may flurry with it. There is no monk yet.</summary>
    Monk = 1 << 6,

    /// <summary>Hurts without killing: its damage is nonlethal.</summary>
    Nonlethal = 1 << 7,

    Performance = 1 << 8,

    /// <summary>One more point of dodge while fighting defensively or in total defence.</summary>
    Blocking = 1 << 9,

    /// <summary>Breaks on a natural 1, and a broken one that rolls another is gone.</summary>
    Fragile = 1 << 10,

    Grapple = 1 << 11,

    Distracting = 1 << 12,

    Deadly = 1 << 13,

    Improvised = 1 << 14,

    /// <summary>The book has a paragraph about it that the table could not hold.</summary>
    SeeText = 1 << 15,

    /// <summary>A gun that fires a cone of shot. Firearms only, and not modelled.</summary>
    Scatter = 1 << 16,

    /// <summary>A gun that fires a burst. Firearms only, and not modelled.</summary>
    Automatic = 1 << 17,
}

public static class WeaponSpecials
{
    /// <summary>The specials the rules act on today. Everything else is data.</summary>
    public const WeaponSpecial Modelled =
        WeaponSpecial.Reach | WeaponSpecial.Trip | WeaponSpecial.Nonlethal
        | WeaponSpecial.Blocking | WeaponSpecial.Fragile | WeaponSpecial.Double;

    public static bool IsModelled(WeaponSpecial special) => (Modelled & special) == special;

    /// <summary>Each flag on its own, in the order the enum lists them.</summary>
    public static IEnumerable<WeaponSpecial> Each(WeaponSpecial specials) =>
        Enum.GetValues<WeaponSpecial>().Where(flag => flag != WeaponSpecial.None && specials.HasFlag(flag));

    /// <summary>"see-text", as the weapon files write it.</summary>
    public static string Name(WeaponSpecial special) => special switch
    {
        WeaponSpecial.SeeText => "see-text",
        _ => special.ToString().ToLowerInvariant(),
    };

    /// <summary>"see-text", "SeeText" or "reach"; null for a word the tables do not use.</summary>
    public static WeaponSpecial? Parse(string? written) =>
        Enum.TryParse<WeaponSpecial>(written?.Replace("-", string.Empty, StringComparison.Ordinal), true, out var special)
            && special != WeaponSpecial.None
            && Each(special).Count() == 1
                ? special
                : null;
}
