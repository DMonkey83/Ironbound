using Ironbound.Rules.Combat;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Maps;

/// <summary>
/// The ring of ground a creature can strike into: from <see cref="Minimum"/> feet out to
/// <see cref="Maximum"/>.
/// </summary>
/// <remarks>
/// A band rather than a number because of reach weapons. A man with a longspear threatens ten
/// feet away and not the square beside him; an ogre with the same polearm threatens fifteen and
/// twenty feet away and nothing nearer. Natural reach has no hole in the middle — the ogre's ten
/// feet of arm still covers the squares around him — which is why the minimum is nought unless a
/// reach weapon is in hand.
/// <para>
/// One exception to the distance count, the one the book makes for ten-foot reach: the square two
/// away on a diagonal counts as within ten feet. By the alternating count it is fifteen, which would
/// leave a ten-foot reach a ring with its corners cut off — the shape the Core Rulebook's reach
/// diagrams do not draw. Only ten feet gets it; longer reach is counted as written.
/// </para>
/// </remarks>
public readonly record struct ReachBand(int Minimum, int Maximum)
{
    /// <summary>The reach the exception for the second diagonal applies to.</summary>
    public const int DiagonalExceptionFeet = 10;

    /// <summary>Reaches nothing: something Tiny with no weapon to lengthen its arm.</summary>
    public bool IsNone => Maximum <= 0;

    /// <summary>How many squares out the band can possibly reach, for walking a ring of them.</summary>
    public int Squares => Math.Max(0, Maximum / Distance.FeetPerSquare);

    /// <summary>
    /// The distance this band measures one square from another at: the ordinary count, except that
    /// with ten feet of reach the second diagonal is ten feet away rather than fifteen.
    /// </summary>
    public int FeetBetween(GridSquare from, GridSquare to)
    {
        var dx = Math.Abs(from.X - to.X);
        var dy = Math.Abs(from.Y - to.Y);

        return Maximum == DiagonalExceptionFeet && dx == 2 && dy == 2
            ? DiagonalExceptionFeet
            : Distance.Between(from, to);
    }

    /// <summary>Whether the band covers <paramref name="to"/> from someone standing at <paramref name="from"/>.</summary>
    public bool Covers(GridSquare from, GridSquare to)
    {
        if (from == to || IsNone)
        {
            return false;
        }

        var feet = FeetBetween(from, to);
        return feet >= Minimum && feet <= Maximum;
    }

    /// <summary>A creature's own reach, with nothing in its hands to lengthen it: what it touches with.</summary>
    public static ReachBand Natural(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        return new ReachBand(0, creature.Reach);
    }

    /// <summary>
    /// A creature's reach with one weapon in hand: twice its natural reach and nothing nearer than
    /// one step past it for a reach weapon, its natural reach for anything else. Something Tiny,
    /// with no reach of its own, reaches the next square with a polearm and no further.
    /// </summary>
    public static ReachBand Of(Creature creature, WeaponAttack? weapon)
    {
        ArgumentNullException.ThrowIfNull(creature);

        if (weapon is not { IsReach: true })
        {
            return Natural(creature);
        }

        var natural = creature.Reach;
        return natural <= 0
            ? new ReachBand(Distance.FeetPerSquare, Distance.FeetPerSquare)
            : new ReachBand(natural + Distance.FeetPerSquare, natural * 2);
    }

    /// <summary>"5 ft", "10 ft, not adjacent", "15-20 ft, not within 10 ft".</summary>
    public override string ToString()
    {
        if (IsNone)
        {
            return "none";
        }

        if (Minimum <= Distance.FeetPerSquare)
        {
            return Minimum == Maximum || Minimum == 0 ? $"{Maximum} ft" : $"{Minimum}-{Maximum} ft";
        }

        var inside = Minimum - Distance.FeetPerSquare;
        var hole = inside <= Distance.FeetPerSquare ? "not adjacent" : $"not within {inside} ft";

        return Minimum == Maximum ? $"{Maximum} ft, {hole}" : $"{Minimum}-{Maximum} ft, {hole}";
    }
}
