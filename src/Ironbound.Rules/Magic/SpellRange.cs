using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Magic;

public enum SpellRangeKind
{
    /// <summary>Only ever yourself.</summary>
    Personal,

    /// <summary>As far as you can reach, which grows if you do.</summary>
    Touch,

    Close,
    Medium,
    Long,

    /// <summary>A flat number of feet, for anything that does not follow the table.</summary>
    Fixed,
}

/// <summary>
/// How far a spell carries. The three named bands grow with caster level, which is why this is a
/// kind rather than a number.
/// </summary>
public readonly record struct SpellRange
{
    private SpellRange(SpellRangeKind kind, int feet)
    {
        Kind = kind;
        Feet = feet;
    }

    public SpellRangeKind Kind { get; }

    /// <summary>Only meaningful for <see cref="SpellRangeKind.Fixed"/>.</summary>
    public int Feet { get; }

    public static SpellRange Personal { get; } = new(SpellRangeKind.Personal, 0);

    public static SpellRange Touch { get; } = new(SpellRangeKind.Touch, 0);

    public static SpellRange Close { get; } = new(SpellRangeKind.Close, 0);

    public static SpellRange Medium { get; } = new(SpellRangeKind.Medium, 0);

    public static SpellRange Long { get; } = new(SpellRangeKind.Long, 0);

    public static SpellRange Of(int feet)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(feet);
        return new SpellRange(SpellRangeKind.Fixed, feet);
    }

    public int InFeet(Creature caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return InFeet(caster, caster.Spells.CasterLevel);
    }

    /// <summary>
    /// The range at a given level rather than the caster's spellcasting one: a cleric's domain
    /// power reaches as far as her cleric level says, whatever else she casts at.
    /// </summary>
    public int InFeet(Creature caster, int level)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return Kind switch
        {
            SpellRangeKind.Personal => 0,

            // Your own reach, so an enlarged wizard really does touch further — and only your own:
            // a polearm in the other hand lengthens nobody's touch.
            SpellRangeKind.Touch => Maps.ReachBand.Natural(caster).Maximum,

            SpellRangeKind.Close => 25 + (5 * (level / 2)),
            SpellRangeKind.Medium => 100 + (10 * level),
            SpellRangeKind.Long => 400 + (40 * level),
            _ => Feet,
        };
    }

    /// <summary>
    /// Whether the spell carries from one square to another. A touch spell asks the caster's own
    /// reach, so the second diagonal a ten-foot reach gets is a touch too, and a polearm is not.
    /// </summary>
    public bool Reaches(Creature caster, Maps.GridSquare from, Maps.GridSquare to, int level)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return Kind == SpellRangeKind.Touch
            ? from == to || Maps.ReachBand.Natural(caster).Covers(from, to)
            : Maps.Distance.Between(from, to) <= InFeet(caster, level);
    }

    public override string ToString() =>
        Kind == SpellRangeKind.Fixed ? $"{Feet} ft" : Kind.ToString().ToLowerInvariant();
}
