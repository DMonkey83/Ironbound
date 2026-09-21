using Ironbound.Rules.Saves;

namespace Ironbound.Rules.Classes;

/// <summary>
/// What a list of class levels adds up to.
/// </summary>
/// <remarks>
/// Every one of these is a sum across classes rather than a lookup on one, because that is how
/// the rules actually work: a fighter 1 / wizard 1 has both classes' base attack and both
/// classes' saves, which is why a one-level dip into a class with a good save is such a
/// well-known piece of character building. Modelling a character as a single class and level
/// would make that unsayable, and retrofitting it later means touching every derivation at once.
/// <para>
/// Nothing here rolls dice. Building a creature has to stay a pure function of its definition —
/// the moment it consumes the random source, loading content changes what happens next.
/// </para>
/// </remarks>
public static class Progression
{
    /// <summary>Base attack bonus: every class's contribution at its own level, added up.</summary>
    public static int BaseAttack(IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return levels.Sum(taken => taken.Class.BaseAttackAt(taken.Level));
    }

    /// <summary>One saving throw, summed the same way.</summary>
    public static int SaveBase(Save save, IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return levels.Sum(taken => taken.Class.SaveAt(save, taken.Level));
    }

    /// <summary>Total character level, which is what most of the rest of the rules ask for.</summary>
    public static int TotalLevel(IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        return levels.Sum(taken => taken.Level);
    }

    /// <summary>
    /// Caster level. The highest single class rather than the sum: a wizard 3 / cleric 3 casts
    /// as a third-level caster twice over, not as a sixth-level one.
    /// </summary>
    public static int CasterLevel(IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var best = 0;
        foreach (var taken in levels)
        {
            best = Math.Max(best, taken.Class.CasterLevelAt(taken.Level));
        }

        return best;
    }

    /// <summary>The first class that casts anything, for whose ability powers the spells.</summary>
    public static ClassLevel? Caster(IEnumerable<ClassLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        foreach (var taken in levels)
        {
            if (taken.Class.Casting != CasterProgression.None && taken.Level > 0)
            {
                return taken;
            }
        }

        return null;
    }

    /// <summary>
    /// Spells per day: the class tables added up, plus what a high casting ability is worth.
    /// </summary>
    /// <remarks>
    /// The ability bonus is the rule that makes Intelligence matter to a wizard for something
    /// other than save DCs: one extra slot of every level you can already cast, and another for
    /// every four points beyond that. It is why an eighteen is worth more than the +4 suggests.
    /// </remarks>
    public static IReadOnlyDictionary<int, int> SlotsFor(
        IEnumerable<ClassLevel> levels, int abilityModifier)
    {
        ArgumentNullException.ThrowIfNull(levels);

        var slots = new Dictionary<int, int>();

        foreach (var taken in levels)
        {
            var table = taken.Class.SlotsAt(taken.Level);

            for (var spellLevel = 1; spellLevel <= table.Count; spellLevel++)
            {
                if (table[spellLevel - 1] <= 0)
                {
                    continue;
                }

                slots[spellLevel] = slots.GetValueOrDefault(spellLevel)
                    + table[spellLevel - 1]
                    + BonusSlots(abilityModifier, spellLevel);
            }
        }

        return slots;
    }

    /// <summary>
    /// Extra spells per day from a high ability: one at every level you can cast, and another
    /// for each four points past it.
    /// </summary>
    public static int BonusSlots(int abilityModifier, int spellLevel)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(spellLevel, 1);

        return abilityModifier < spellLevel ? 0 : ((abilityModifier - spellLevel) / 4) + 1;
    }

    /// <summary>
    /// Hit points before Constitution: the full die for the character's very first level, and
    /// whatever the rule options say for every level after it, whichever class they were taken in.
    /// </summary>
    /// <remarks>
    /// <see cref="HitPointGeneration.Rolled"/> is treated as average here. Rolling belongs to
    /// character creation, which has a random source to hand and a record of what it rolled;
    /// deriving it from a definition would mean a creature's hit points depended on how many
    /// other creatures had been built first.
    /// </remarks>
    public static int HitPointsBase(IEnumerable<ClassLevel> levels, RuleOptions? rules = null)
    {
        ArgumentNullException.ThrowIfNull(levels);
        rules ??= RuleOptions.Pathfinder;

        var total = 0;
        var first = true;

        foreach (var taken in levels)
        {
            for (var level = 1; level <= taken.Level; level++)
            {
                total += first || rules.HitPointGeneration == HitPointGeneration.Maximum
                    ? taken.Class.HitDie
                    : (taken.Class.HitDie / 2) + 1;

                first = false;
            }
        }

        return total;
    }
}
