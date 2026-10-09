using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Combat;

/// <summary>
/// How a weapon's dice change with the size of the creature it was made for.
/// </summary>
/// <remarks>
/// The tables give the dice for a Small and a Medium weapon side by side, so those two are read
/// straight from the file. Everything else is a step along the Core Rulebook's table: a Large
/// longsword is a Medium one moved up a step, 1d8 to 2d6, and a Tiny one is a Small one moved
/// down. The steps below are that table, written out — they are not a formula, because the
/// book's are not; 1d10 goes up to 2d8 while 1d8 goes up to 2d6.
/// <para>
/// Weapons are assumed to be made for whoever carries them. A human picking up an ogre's club
/// would swing it at a penalty in the book; nothing here asks, because nobody in the game does.
/// </para>
/// </remarks>
public static class WeaponDice
{
    // One size up, as the table gives Medium to Large, and carried on past it the way the
    // table's own columns continue for Huge and beyond.
    private static readonly Dictionary<string, string> Up = new(StringComparer.Ordinal)
    {
        ["1"] = "1d2",
        ["1d2"] = "1d3",
        ["1d3"] = "1d4",
        ["1d4"] = "1d6",
        ["1d6"] = "1d8",
        ["1d8"] = "2d6",
        ["1d10"] = "2d8",
        ["1d12"] = "3d6",
        ["2d3"] = "2d4",
        ["2d4"] = "2d6",
        ["2d6"] = "3d6",
        ["2d8"] = "3d8",
        ["2d10"] = "4d8",
        ["2d12"] = "4d8",
        ["3d6"] = "4d6",
        ["3d8"] = "4d8",
        ["4d6"] = "6d6",
        ["4d8"] = "6d8",
        ["6d6"] = "8d6",
        ["6d8"] = "8d8",
        ["8d6"] = "12d6",
        ["8d8"] = "12d8",
    };

    // One size down, as the table gives Medium to Small. Where two dice step up to the same
    // one, the way down is the book's Small column, not a guess at an inverse.
    private static readonly Dictionary<string, string> Down = new(StringComparer.Ordinal)
    {
        ["1d2"] = "1",
        ["1d3"] = "1d2",
        ["1d4"] = "1d3",
        ["1d6"] = "1d4",
        ["1d8"] = "1d6",
        ["1d10"] = "1d8",
        ["1d12"] = "1d10",
        ["2d3"] = "1d4",
        ["2d4"] = "1d6",
        ["2d6"] = "1d10",
        ["2d8"] = "2d6",
        ["2d10"] = "2d8",
        ["2d12"] = "2d10",
        ["3d6"] = "2d6",
        ["3d8"] = "2d8",
        ["4d6"] = "3d6",
        ["4d8"] = "3d8",
        ["6d6"] = "4d6",
        ["6d8"] = "4d8",
    };

    /// <summary>One size larger: "1d8" becomes "2d6". Dice the table does not list stay as they are.</summary>
    public static string Larger(string dice)
    {
        ArgumentNullException.ThrowIfNull(dice);
        return Up.GetValueOrDefault(dice.Trim(), dice);
    }

    /// <summary>One size smaller: "1d8" becomes "1d6", and a single point stays a single point.</summary>
    public static string Smaller(string dice)
    {
        ArgumentNullException.ThrowIfNull(dice);
        return Down.GetValueOrDefault(dice.Trim(), dice);
    }

    /// <summary>
    /// The dice for a weapon made for a creature of <paramref name="size"/>: the Small or Medium
    /// dice as written, and a step along the table from the nearer of them for anything else.
    /// </summary>
    /// <param name="small">The table's Small column, or null to step down from Medium.</param>
    public static string For(CreatureSize size, string medium, string? small = null)
    {
        ArgumentNullException.ThrowIfNull(medium);

        var smallDice = small ?? Smaller(medium);

        return size switch
        {
            CreatureSize.Medium => medium,
            CreatureSize.Small => smallDice,
            < CreatureSize.Small => Step(smallDice, CreatureSize.Small - size, Smaller),
            _ => Step(medium, size - CreatureSize.Medium, Larger),
        };
    }

    private static string Step(string dice, int steps, Func<string, string> step)
    {
        for (var i = 0; i < steps; i++)
        {
            dice = step(dice);
        }

        return dice;
    }
}
