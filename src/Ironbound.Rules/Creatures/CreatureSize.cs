namespace Ironbound.Rules.Creatures;

/// <summary>The size categories, smallest first.</summary>
public enum CreatureSize
{
    Fine,
    Diminutive,
    Tiny,
    Small,
    Medium,
    Large,
    Huge,
    Gargantuan,
    Colossal,
}

public static class CreatureSizes
{
    /// <summary>
    /// The size modifier, which applies to armour class <em>and</em> attack rolls alike — small
    /// things are harder to hit and better at hitting, large things the reverse.
    /// </summary>
    public static int Modifier(CreatureSize size) => size switch
    {
        CreatureSize.Fine => 8,
        CreatureSize.Diminutive => 4,
        CreatureSize.Tiny => 2,
        CreatureSize.Small => 1,
        CreatureSize.Medium => 0,
        CreatureSize.Large => -1,
        CreatureSize.Huge => -2,
        CreatureSize.Gargantuan => -4,
        CreatureSize.Colossal => -8,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    /// <summary>
    /// The size modifier used by combat maneuvers, which runs the other way.
    /// </summary>
    /// <remarks>
    /// Not a mistake, and worth stating plainly because the sign trips everyone up: being small
    /// makes you harder to <em>hit</em> and easier to <em>shove</em>. A goblin gets +1 to armour
    /// class and -1 to keeping its feet, and an ogre the reverse.
    /// </remarks>
    public static int ManeuverModifier(CreatureSize size) => -Modifier(size);

    /// <summary>
    /// Natural reach in feet. Tiny and smaller have <b>none</b>: they have to be in your square
    /// to do anything to you, which is the rule as written rather than a rounding to five.
    /// </summary>
    public static int Reach(CreatureSize size) => size switch
    {
        CreatureSize.Fine or CreatureSize.Diminutive or CreatureSize.Tiny => 0,
        CreatureSize.Small or CreatureSize.Medium => 5,
        CreatureSize.Large => 10,
        CreatureSize.Huge => 15,
        CreatureSize.Gargantuan => 20,
        CreatureSize.Colossal => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    public static string Name(CreatureSize size) => size.ToString();
}
