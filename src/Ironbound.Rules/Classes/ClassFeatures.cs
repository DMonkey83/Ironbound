using Ironbound.Rules.Creatures;

namespace Ironbound.Rules.Classes;

/// <summary>A line of a character sheet's class features: a name, and what it is worth now.</summary>
public sealed record FeatureLine(string Name, string Detail)
{
    public override string ToString() => Detail.Length == 0 ? Name : $"{Name}: {Detail}";
}

/// <summary>
/// The questions every class feature asks of a creature: does it have this, how many times
/// over, and from how many levels of which classes.
/// </summary>
/// <remarks>
/// Everything is derived from <see cref="Creature.Levels"/> and the class files on demand,
/// never cached. A level gained mid-day, a save reloaded, a second class taken — all of them
/// change the answer at once, and nothing has to be told to refresh. The rules that act on the
/// answers live beside the rules they change: sneak attack with the strike, bravery with the
/// saving throw.
/// </remarks>
public static partial class ClassFeatures
{
    /// <summary>Whether any of the creature's classes has handed out this feature yet.</summary>
    public static bool Has(Creature creature, string featureId) => Rank(creature, featureId) > 0;

    /// <summary>
    /// How many rows of this feature the creature's classes have reached between them. Sneak
    /// attack at rogue 5 is three; bravery at fighter 6 is two.
    /// </summary>
    /// <remarks>
    /// Summed across classes, which is the rule for the features that stack — a rogue 3 /
    /// barbarian 3 has trap sense +2 — and harmless for the ones that only appear in one.
    /// </remarks>
    public static int Rank(Creature creature, string featureId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels.Sum(taken =>
            taken.Class.FeaturesAt(taken.Level).Count(feature => feature.Id == featureId));
    }

    /// <summary>The rows of one feature reached so far, in table order, with their parameters.</summary>
    public static IEnumerable<ClassFeatureDefinition> Rows(Creature creature, string featureId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels.SelectMany(taken =>
            taken.Class.FeaturesAt(taken.Level).Where(feature => feature.Id == featureId));
    }

    /// <summary>
    /// The levels in whichever classes grant this feature at all, added up. Trapfinding is half
    /// the rogue's level; rage rounds are counted from the barbarian's.
    /// </summary>
    public static int LevelOf(Creature creature, string featureId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels
            .Where(taken => taken.Class.FeaturesAt(taken.Level).Any(feature => feature.Id == featureId))
            .Sum(taken => taken.Level);
    }

    /// <summary>The level in one class by id, or nought.</summary>
    public static int ClassLevel(Creature creature, string classId)
    {
        ArgumentNullException.ThrowIfNull(creature);

        return creature.Levels
            .Where(taken => string.Equals(taken.Class.Id, classId, StringComparison.Ordinal))
            .Sum(taken => taken.Level);
    }

    /// <summary>Half, rounded down, and never less than one: the shape of half the class features.</summary>
    internal static int HalfMinOne(int level) => Math.Max(1, level / 2);
}
