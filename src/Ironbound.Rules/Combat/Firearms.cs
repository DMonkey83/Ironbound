namespace Ironbound.Rules.Combat;

/// <summary>Which kind of gun something is, which decides how far it shoots against touch AC.</summary>
public enum FirearmEra
{
    /// <summary>Not a gun.</summary>
    None,

    /// <summary>Muskets and pistols: powder and ball, loaded by hand.</summary>
    Early,

    /// <summary>Revolvers and rifles: cartridges.</summary>
    Advanced,
}

/// <summary>
/// What makes a gun different from a bow: a bullet that ignores armour up close, and a weapon that
/// can jam, crack, and burst in the hand.
/// </summary>
/// <remarks>
/// Ultimate Combat's rules, cut down to what a turn-based fight needs. Capacity and reloading are
/// kept as data and not enforced — every shot is assumed to be loaded — because there is no
/// action for loading yet and a gun that could never fire twice would be worse than one that
/// never runs dry. The explosion is simplified further still: see <see cref="ExplosionSaveDc"/>.
/// </remarks>
public static class Firearms
{
    /// <summary>A broken gun misfires on four more numbers than a sound one.</summary>
    public const int BrokenMisfireIncrease = 4;

    /// <summary>
    /// The Reflex save a gun bursting in somebody's hands allows, for half.
    /// </summary>
    /// <remarks>
    /// The book's explosion is a burst centred on a corner of the wielder's square that catches
    /// everyone in it. Here it catches the wielder alone, with the gun's own dice as fire damage —
    /// a simplification, and a kind one to anybody standing next to a gunslinger.
    /// </remarks>
    public const int ExplosionSaveDc = 12;

    /// <summary>
    /// The highest natural roll that misfires: the gun's own, four more while it is broken, and
    /// nothing at all for anything that is not a gun.
    /// </summary>
    public static int MisfireRange(WeaponAttack weapon, bool broken)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        if (!weapon.IsFirearm || weapon.Misfire <= 0)
        {
            return 0;
        }

        return broken ? weapon.Misfire + BrokenMisfireIncrease : weapon.Misfire;
    }

    public static bool Misfires(WeaponAttack weapon, int natural, bool broken) =>
        natural <= MisfireRange(weapon, broken);

    /// <summary>
    /// How many range increments a gun shoots against touch armour class: the first only for an
    /// early firearm, five for an advanced one. Beyond that the bullet has slowed enough for
    /// armour to matter again.
    /// </summary>
    public static int TouchIncrements(FirearmEra era) => era switch
    {
        FirearmEra.Early => 1,
        FirearmEra.Advanced => 5,
        _ => 0,
    };

    /// <summary>Whether a shot at <paramref name="feet"/> is resolved against touch armour class.</summary>
    public static bool TargetsTouch(WeaponAttack weapon, int? feet)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        return weapon.IsFirearm
            && weapon.IsRanged
            && weapon.IncrementsAt(feet ?? 0) <= TouchIncrements(weapon.Firearm);
    }
}
