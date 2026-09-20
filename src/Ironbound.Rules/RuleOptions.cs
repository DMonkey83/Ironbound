namespace Ironbound.Rules;

/// <summary>How a character's hit points per level are decided.</summary>
public enum HitPointGeneration
{
    /// <summary>Roll the hit die. Swingy and memorable; two identical characters differ.</summary>
    Rolled,

    /// <summary>Half the die plus one — d8 gives 5, d10 gives 6. Predictable and the usual default.</summary>
    Average,

    /// <summary>The full die every level. Generous; mostly useful for testing and for a forgiving mode.</summary>
    Maximum,
}

/// <summary>
/// The rules that are allowed to vary. Every default reproduces Pathfinder exactly, so a game
/// with no difficulty system and a game whose difficulty sits at its defaults run the same code.
/// </summary>
/// <remarks>
/// Deliberately immutable and deliberately passed by hand rather than read from a global: a
/// static would break replay determinism and make test order matter. Whatever ends up owning a
/// saved game must store this next to the random source's state, because a replay under
/// different options diverges from the recording.
/// <para>
/// The intended growth is toward difficulty expressed as *AI competence* — worse target
/// selection, wasted rounds, forgotten consumables — rather than as fudged numbers. Damage
/// multipliers and hidden stat adjustments are deliberately absent: a difficulty setting should
/// make the opponent worse, not make the rules lie.
/// </para>
/// </remarks>
public sealed record RuleOptions
{
    /// <summary>The rules as written. The default for everything.</summary>
    public static RuleOptions Pathfinder { get; } = new();

    public HitPointGeneration HitPointGeneration { get; init; } = HitPointGeneration.Average;

    /// <summary>
    /// True: a creature at 0 hit points is disabled, below 0 is dying, and death waits until
    /// its Constitution score in negatives. False: 0 hit points is dead, full stop.
    /// </summary>
    public bool DeathsDoor { get; init; } = true;

    /// <summary>
    /// True: a threatened critical needs a second attack roll to confirm. False: a threat is
    /// immediately a critical, which is faster, swingier, and one fewer roll per attack.
    /// </summary>
    /// <remarks>
    /// Worth knowing when tuning a high-armour boss: under confirmation, an attacker who can
    /// only hit on a natural 20 must roll a second natural 20 to confirm, so criticals all but
    /// vanish (0.25% per attack). Turning confirmation off is one way to keep a desperate fight
    /// winnable without touching anyone's numbers.
    /// </remarks>
    public bool ConfirmCriticals { get; init; } = true;

    /// <summary>
    /// True (the rules as written): a natural 20 hits whatever the armour class, so every attack
    /// keeps a 5% chance and no fight is ever mathematically unwinnable. False: armour class is
    /// absolute, and an under-levelled or wrongly built party genuinely cannot land a blow.
    /// </summary>
    /// <remarks>
    /// This is the floor that stops a high-armour boss from being unreachable without a specific
    /// build. Note that Pathfinder gives no such floor to *skill* checks, which is how content
    /// stays gated: a fight is always technically winnable, a DC 40 lock is not.
    /// </remarks>
    public bool NaturalTwentyAlwaysHits { get; init; } = true;

    /// <summary>
    /// True (the rules as written): a natural 1 misses however large the bonus, so even a
    /// trivial enemy lands a blow eventually. Kept separate from
    /// <see cref="NaturalTwentyAlwaysHits"/> on purpose — a harsher mode usually wants to remove
    /// the player's floor while keeping the ceiling.
    /// </summary>
    public bool NaturalOneAlwaysMisses { get; init; } = true;
}
