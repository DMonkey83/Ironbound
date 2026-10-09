using Ironbound.Rules.Classes;
using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Magic;

/// <summary>Whether a creature casts arcane or divine spells, by the classes that give it them.</summary>
public static class SpellTraditions
{
    /// <summary>
    /// Whether any class it has levels in casts arcane spells, and has given it a caster level
    /// to cast them with: a wizard 1 does, a fighter 1 who once read a book does not.
    /// </summary>
    public static bool CastsArcane(Creature creature) => Casts(creature, MagicTradition.Arcane);

    public static bool CastsDivine(Creature creature) => Casts(creature, MagicTradition.Divine);

    private static bool Casts(Creature creature, MagicTradition tradition)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels.Any(taken =>
            taken.Class.Tradition == tradition && taken.Class.CasterLevelAt(taken.Level) > 0);
    }

    /// <summary>The caster level of the arcane classes alone, which is what Arcane Strike counts.</summary>
    public static int ArcaneCasterLevel(Creature creature)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels
            .Where(taken => taken.Class.Tradition == MagicTradition.Arcane)
            .Select(taken => taken.Class.CasterLevelAt(taken.Level))
            .DefaultIfEmpty(0)
            .Max();
    }
}
