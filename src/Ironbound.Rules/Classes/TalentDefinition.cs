namespace Ironbound.Rules.Classes;

/// <summary>
/// What a talent does, as far as the code is concerned: one entry per rule that needs C#.
/// </summary>
/// <remarks>
/// Rogue talents and rage powers share one enum because they share one shape — a pick from a
/// list every other level, each a small rule of its own — and because the levelling screen
/// offers either through the same <see cref="LevelChoices.Talent"/>. Which list a talent is on
/// is a fact about the content file, not about the code.
/// </remarks>
public enum TalentEffect
{
    // ---- rogue talents ----

    /// <summary>Sneak attack leaves the target bleeding a point a round per sneak die.</summary>
    BleedingAttack,

    /// <summary>A bonus combat feat.</summary>
    CombatTrick,

    /// <summary>Weapon Finesse, free.</summary>
    FinesseRogue,

    /// <summary>In the surprise round, everybody is flat-footed to her.</summary>
    SurpriseAttack,

    /// <summary>Weapon Focus, free.</summary>
    WeaponTraining,

    /// <summary>Whoever she sneak attacks takes no attacks of opportunity for a round.</summary>
    SlowReactions,

    /// <summary>Once a day, temporary hit points as she drops below nought.</summary>
    Resiliency,

    // ---- advanced rogue talents, from tenth level ----

    /// <summary>Sneak attack also deals two points of Strength damage.</summary>
    CripplingStrike,

    /// <summary>Half damage even on a failed Reflex save.</summary>
    ImprovedEvasion,

    /// <summary>Once a round, a swing at a foe an ally has just struck.</summary>
    Opportunist,

    /// <summary>Once a day, a Reflex save to take half of a blow that would drop her.</summary>
    DefensiveRoll,

    // ---- rage powers ----

    /// <summary>Declared before a swing: more damage on it. Once a rage.</summary>
    PowerfulBlow,

    /// <summary>Declared before a swing: more accuracy on it. Once a rage.</summary>
    SurpriseAccuracy,

    /// <summary>Declared before a manoeuvre: her level on the check. Once a rage.</summary>
    StrengthSurge,

    /// <summary>A move action for a dodge bonus against melee, for a few rounds.</summary>
    GuardedStance,

    /// <summary>Once a round, a bull rush instead of a swing, which hurts.</summary>
    Knockback,

    /// <summary>Better saves against magic, and no exception for her friends' magic.</summary>
    Superstition,

    /// <summary>Five more feet of speed while raging.</summary>
    SwiftFoot,

    /// <summary>Once a day, a standard action to heal herself mid-rage.</summary>
    RenewedVigor,
}

/// <summary>
/// One rogue talent or rage power, as written down.
/// </summary>
/// <remarks>
/// A content kind, like feats, so the list can grow a file at a time. The behaviour is keyed by
/// <see cref="Effect"/>; a file naming an effect the code does not have is a load-time problem
/// with the file's name attached, which is the same bargain <see cref="Feats.FeatEffect"/> makes.
/// </remarks>
public sealed record TalentDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Which list it is on: <c>rogue-talent</c> or <c>rage-power</c>.</summary>
    public required string List { get; init; }

    public TalentEffect Effect { get; init; }

    /// <summary>An advanced talent, only to be had from tenth level.</summary>
    public bool Advanced { get; init; }

    /// <summary>The level in the class it is picked from that it asks for. Renewed vigor wants four.</summary>
    public int MinimumLevel { get; init; }

    /// <summary>Talents that must already be had, by id.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    public override string ToString() => Name;
}
