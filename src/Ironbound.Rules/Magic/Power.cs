using Ironbound.Rules.Encounters;

namespace Ironbound.Rules.Magic;

/// <summary>What using a power actually does, beyond what its spell-shaped effect says.</summary>
public enum PowerUse
{
    /// <summary>Resolves <see cref="Power.Effect"/> exactly as a spell would be.</summary>
    Effect,

    /// <summary>
    /// Casts a real spell from the spellbook without a slot: an arcane bond. A spell in every
    /// sense but the slot, so a raging barbarian could no more do it than cast one.
    /// </summary>
    Spell,

    /// <summary>Moves the user to the square aimed at, paying for the distance in feet.</summary>
    Teleport,

    /// <summary>
    /// Lends the user a feat she does not have for a round: a War cleric's Weapon Master. The
    /// feat is whichever her file chose, so it cannot be written as a spell effect.
    /// </summary>
    BorrowFeat,
}

/// <summary>What has to be true of the target before a power will go off.</summary>
public enum PowerRequirement
{
    None,

    /// <summary>Somebody below nought hit points and still alive: Rebuke Death.</summary>
    TargetDying,

    /// <summary>Only in a rage: the rage powers that are actions.</summary>
    Raging,
}

/// <summary>
/// Something a creature can do a few times a day that is not a spell from a slot: channel
/// energy, a domain's touch, a school's missile, a rage power that heals.
/// </summary>
/// <remarks>
/// Shaped as a <see cref="Spell"/> on purpose. The targeting, the save, the damage and the
/// effects are already a solved problem there, and so is everything that draws them — so a
/// power is a spell-shaped effect plus the things a spell does not have: a pool it draws on,
/// whether it provokes, and a difficulty class and caster level of its own.
/// <para>
/// Built fresh from the creature's class features whenever it is asked for, so the dice grow
/// with the cleric without anything being updated. Two powers are the same power when their ids
/// match; <see cref="Creatures.Creature.UsesLeft"/> and the action that uses one look it up by id.
/// </para>
/// </remarks>
public sealed record Power(string Id, string Name, Spell Effect, ActionCost Cost)
{
    /// <summary>
    /// The daily allowance it draws on. Several powers can share one: every spell an arcane
    /// bond can cast comes out of the same once a day.
    /// </summary>
    public string Pool { get; init; } = Id;

    /// <summary>How much of the pool one use spends. Dimensional steps spend feet, so it varies.</summary>
    public int UseCost { get; init; } = 1;

    /// <summary>
    /// Whether using it in somebody's reach gives them a free swing. Spell-like abilities do, as
    /// spells do; supernatural ones such as channel energy do not.
    /// </summary>
    public bool Provokes { get; init; }

    /// <summary>The save difficulty, which follows its own formula: channel energy is ten plus half the level plus Charisma.</summary>
    public int DifficultyClass { get; init; }

    /// <summary>The level it works at, for anything in its effect that scales or lasts per level.</summary>
    public int CasterLevel { get; init; }

    public PowerUse Use { get; init; } = PowerUse.Effect;

    public PowerRequirement Requires { get; init; } = PowerRequirement.None;

    /// <summary>One line on what it does, for a tooltip.</summary>
    public string Description { get; init; } = string.Empty;

    public override string ToString() => Name;
}
